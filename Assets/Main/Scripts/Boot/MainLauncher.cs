using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CryptWalker.Main;
using CryptWalker.Main.Service;
using CryptWalker.Main.UI;
using UnityEngine;

namespace CryptWalker.Main.Boot
{
    /// <summary>
    /// 主工程(AOT) 启动入口。挂载在游戏启动场景的 Boot 物体上。
    /// 启动链路：
    ///   YooAsset 初始化(可选) → 加载 AOT 补充元数据 → Assembly.Load 热更 dll
    ///   → 反射调用 CryptWalker.HotUpdate.GameRoot.Run(bridge) → 注册每帧驱动。
    /// </summary>
    public class MainLauncher : MonoBehaviour
    {
        // AOT 补充元数据目录（相对 StreamingAssets）。正式包由 CryptWalker/构建/① 拷入这里。
        private const string AotMetadataStreamingSubDir = "HybridCLR/AOTMetadata";

        // 开发期直接读 Generate/All 的产物（相对工程根）。
        // 修复：旧值写的是 "HybridCLRData/AssembliesPostIl2CppPatch"，那个目录 HybridCLR 根本不会产出，
        //      所以编辑器里也从来没加载过 AOT 补充元数据。真正的产物目录是 AssembliesPostIl2CppStrip。
        private const string AotStripRelative = "HybridCLRData/AssembliesPostIl2CppStrip";

        // AOT 补充元数据 dll 名称清单（HybridCLR Generate/All 后生成在 HybridCLRData/AssembliesPostIl2CppPatch）。
        // 注意：主工程自身 CryptWalker.Main 不在此列——它就是 IL2CPP 编译产物本身，无需补充元数据。
        // 通常是 Unity 系统 dll + 用到反射/泛型的第三方运行时程序集。
        private readonly string[] _aotAssemblyNames =
        {
            "mscorlib",
            "System",
            "System.Core",
        };

        // 热更入口参数
        private const string HotAssemblyType = "CryptWalker.HotUpdate.GameRoot";

        private IAppBridge _bridge;
        private Action _hotUpdateTick;

        [Tooltip("启动时是否显示加载界面。默认【关闭】—— 启动很快（读本地 dll 几十毫秒），\n" +
                 "先闪一下加载画面反而多余。进战斗生成地牢时才会用到它（由 GameFlowMachine 驱动）。\n" +
                 "将来接 YooAsset 资源热更下载（Host 模式）时，启动会真的变慢，那时把它勾上即可。")]
        [SerializeField] private bool showLoadingOnBoot = false;

        private void Awake()
        {
            // AppBridge 必须在所有其他 Start 之前创建，否则 LevelConfigHolder.Start() 拿不到 Instance
            _bridge = new AppBridge(Application.persistentDataPath);
        }

        private async void Start()
        {
            Debug.Log("[Main] CryptWalker 启动中 (AOT)");

            // 启动阶段的加载界面（默认不显示）：启动只是读本地 dll，几十毫秒就完了，
            // 弹一下加载画面反而像"多余的开场"。真正的等待在【进战斗生成地牢】那一步，
            // 由 GameFlowMachine 经 IAppBridge 驱动（那才是它该出现的地方）。
            void Progress(float p) { if (showLoadingOnBoot) LoadingScreen.SetProgress(p); }

            if (showLoadingOnBoot) LoadingScreen.Show(0.05f);

            // 1) YooAsset 初始化 —— 开发期用编辑器模拟模式；失败不阻断热更链路
            Progress(0.15f);
            try
            {
                var ok = await YooAssetService.InitializeAsync(YooAssetService.EPlayMode.EditorSimulate);
                if (!ok) Debug.LogWarning("[Main] YooAsset 初始化未成功，进入降级启动");
                else Debug.Log($"[Main] YooAsset mode={YooAssetService.CurrentMode}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Main] YooAsset 初始化异常，降级启动: " + e.Message);
            }

            // 2) 启动热更链路（华佗未装时由 TryBootHotUpdate 内部降级）
            //    这一步最耗时（读 AOT 元数据 + Assembly.Load + 反射调 GameRoot.Run），占进度的大头
            Progress(0.35f);
            bool hotOk = TryBootHotUpdate();

            if (hotOk) Debug.Log("[Main] 热更程序集加载成功，业务已启动");
            else Debug.LogWarning("[Main] 热更程序集未启动（HybridCLR 未装或加载失败），请检查 YooAsset / HybridCLR 配置");

            if (showLoadingOnBoot)
            {
                LoadingScreen.SetProgress(1f);
                LoadingScreen.Hide();
            }
        }

        private void Update()
        {
            // 每帧驱动热更侧业务逻辑（若尚未加载则空转）
            _hotUpdateTick?.Invoke();
        }

        /// <summary>加载 AOT 元数据 + 热更程序集并调用入口</summary>
        private bool TryBootHotUpdate()
        {
            try
            {
                // a) 加载 AOT 补充元数据（HybridCLR 运行前提）
                byte[][] aotDlls = LoadAotMetadataDlls();
                if (aotDlls.Length > 0)
                    HybridCLRHelper.EnsureAotRuntime(aotDlls);

                // b) 获取热更程序集。
                //    ⚠️ 必须先查 AppDomain 里有没有同名程序集，不能无脑 Assembly.Load(bytes)：
                //      编辑器下 Assets/HotUpdate 是普通 asmdef，Unity 启动时就加载了它，
                //      场景/Prefab 上的 EnemyBase、PlayerController 组件实例都属于【那一份】。
                //      再 Assembly.Load 一份 → 出现两个同名程序集 → 两个不同的 EnemyBase Type →
                //      热更侧 GetComponent<EnemyBase>() / FindObjectsOfType<EnemyBase>() 全部返回 null。
                //      （真机走 else 分支：FilterHotFixAssemblies 已把热更程序集从包里剔除，不会重复）
                //    详见 HybridCLRHelper.FindLoadedAssembly 的注释。
                Assembly asm = HybridCLRHelper.FindLoadedAssembly(HybridCLRHelper.HotUpdateAssemblyName);
                if (asm != null)
                {
                    Debug.Log($"[Main] 复用 Unity 已加载的热更程序集 {HybridCLRHelper.HotUpdateAssemblyName}（编辑器模式，避免双程序集类型不匹配）");
                }
                else
                {
                    string scriptAssemblies = Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies");
                    byte[] hotDll = HotUpdateProvider.LoadHotUpdateDll(scriptAssemblies);
                    asm = HybridCLRHelper.LoadHotUpdateAssembly(hotDll);
                    Debug.Log($"[Main] 已从 dll 字节加载热更程序集 {asm.GetName().Name}（真机 / 纯热更模式）");
                }

                // c) 反射调用入口 GameRoot.Run(bridge)
                Type rootType = HybridCLRHelper.ResolveHotType(asm, HotAssemblyType);
                var runMethod = rootType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                if (runMethod == null) throw new MissingMethodException(HotAssemblyType, "Run");
                runMethod.Invoke(null, new object[] { _bridge });

                // d) 收取每帧驱动回调（热更侧应暴露 static Action OnUpdateTick）
                var field = rootType.GetField("OnUpdateTick", BindingFlags.Public | BindingFlags.Static);
                if (field != null)
                    _hotUpdateTick = field.GetValue(null) as Action;

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Main] 热更启动未完成（开发期正常，HybridCLR Build 后可加载热更 dll）: {e.Message}");
                return false;
            }
        }

        private byte[][] LoadAotMetadataDlls()
        {
            string[] dirs = GetAotMetadataDirs();
            var list = new List<byte[]>(_aotAssemblyNames.Length);

            foreach (var name in _aotAssemblyNames)
            {
                foreach (var dir in dirs)
                {
                    string file = Path.Combine(dir, name + ".dll");
                    if (!File.Exists(file)) continue;
                    list.Add(File.ReadAllBytes(file));
                    break;
                }
            }

            if (list.Count == 0)
                Debug.LogWarning($"[Main] 未找到 AOT 补充元数据，跳过（需先执行 HybridCLR/Generate/All，" +
                                 $"并用 CryptWalker/构建/① 拷入 StreamingAssets）。已查找: {string.Join(" / ", dirs)}");
            return list.ToArray();
        }

        /// <summary>AOT 补充元数据候选目录，按优先级排列</summary>
        private static string[] GetAotMetadataDirs()
        {
            var dirs = new List<string>(3);

            // 1) 随包发布路径（正式包/真机）
            dirs.Add(Path.Combine(Application.streamingAssetsPath, AotMetadataStreamingSubDir));
#if UNITY_EDITOR
            // 2) 开发期：直接用 Generate/All 的产物，省掉每次拷贝
            string projectRoot = Path.Combine(Application.dataPath, "..");
            string target = UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
            dirs.Add(Path.Combine(projectRoot, AotStripRelative, target));
            dirs.Add(Path.Combine(projectRoot, AotStripRelative));
#endif
            return dirs.ToArray();
        }
    }
}