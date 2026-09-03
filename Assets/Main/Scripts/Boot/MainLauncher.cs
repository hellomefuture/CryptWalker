using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CryptWalker.Main;
using CryptWalker.Main.Service;
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
        // HybridCLR 构建生成 AOT 补充元数据的目录（相对工程根）
        private const string AotPatchRelative = "HybridCLRData/AssembliesPostIl2CppPatch";

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

        private async void Start()
        {
            Debug.Log("[Main] CryptWalker 启动中 (AOT)");
            _bridge = new AppBridge(Application.persistentDataPath);

            // 1) YooAsset 初始化 —— 开发期用编辑器模拟模式；失败不阻断热更链路
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
            if (TryBootHotUpdate())
                Debug.Log("[Main] 热更程序集加载成功，业务已启动");
            else
                Debug.LogWarning("[Main] 热更程序集未启动（HybridCLR 未装或加载失败），请检查 YooAsset / HybridCLR 配置");
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

                // b) 获取并加载热更程序集字节（编辑器阶段走本地构建产物；正式走 YooAsset 下载）
                string scriptAssemblies = Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies");
                byte[] hotDll = HotUpdateProvider.LoadHotUpdateDll(scriptAssemblies);
                Assembly asm = HybridCLRHelper.LoadHotUpdateAssembly(hotDll);

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
            string patchDir = Path.Combine(Application.dataPath, "..", AotPatchRelative);
            var list = new List<byte[]>(_aotAssemblyNames.Length);
            if (!Directory.Exists(patchDir))
            {
                Debug.LogWarning($"[Main] 未找到 AOT 补充元数据目录，跳过（需先执行 HybridCLR Generate/All）: {patchDir}");
                return list.ToArray();
            }
            foreach (var name in _aotAssemblyNames)
            {
                string file = Path.Combine(patchDir, name + ".dll");
                if (File.Exists(file)) list.Add(File.ReadAllBytes(file));
            }
            return list.ToArray();
        }
    }
}