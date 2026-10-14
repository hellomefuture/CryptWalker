using System;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// HybridCLR 出包流水线 —— 补上"把热更 dll 和 AOT 补充元数据拷进 StreamingAssets"这一步。
    ///
    /// ===== 为什么必须自己做 =====
    /// HybridCLR v8.5.1 **没有任何菜单**会自动做这件事（官方要求你写构建脚本）。
    /// 缺这一步的后果已经实测过：
    ///   出的包能启动（AOT 层 CryptWalker.Main 正常），但**运行不了** ——
    ///   MainLauncher 找不到 CryptWalker.HotUpdate.dll，
    ///   场景/预制体上所有 CryptWalker.HotUpdate.* 组件全部报 "referenced script is missing"。
    ///
    /// ===== 出一个能跑的包需要三样东西 =====
    ///   1) HybridCLRGenerate/link.xml  —— 防止 IL2CPP 把热更类型裁掉（由 Generate/All 产出，
    ///      必须在 Build 之前就位，所以本脚本把它一起跑了）
    ///   2) StreamingAssets/HybridCLR/HotUpdateDlls/*.dll   ← 本脚本拷贝
    ///   3) StreamingAssets/HybridCLR/AOTMetadata/*.dll     ← 本脚本拷贝
    /// 其中 2、3 的目录名与运行时搜索路径严格对应：
    ///   2 → HotUpdateProvider.GetSearchRoots() 的第 3 项
    ///   3 → MainLauncher.GetAotMetadataDirs() 的第 1 项
    /// </summary>
    public static class HybridClrBuild
    {
        private const string StreamingAssetsRoot = "Assets/StreamingAssets";
        private const string HotUpdateDllSubDir = "HybridCLR/HotUpdateDlls";
        private const string AotMetadataSubDir = "HybridCLR/AOTMetadata";

        /// <summary>出包目录（改成你要的位置即可）</summary>
        private const string OutputDir = "F:/Unity/毕设打包/1.0.1";

        /// <summary>patchAOTAssemblies 为空时的兜底清单（HybridCLR 的标准默认值）</summary>
        private static readonly string[] FallbackAotAssemblies = { "mscorlib", "System", "System.Core" };

        [MenuItem("CryptWalker/构建/① 生成 HybridCLR 产物 + 拷入 StreamingAssets", priority = 100)]
        public static void GenerateAndCopy()
        {
            Run();
        }

        [MenuItem("CryptWalker/构建/② 出包（Windows x64）", priority = 101)]
        public static void BuildWindows64()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] Build Settings 里没有启用任何场景，先在 File > Build Settings 勾上启动场景");
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64
                && !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[Build] 切换构建目标到 StandaloneWindows64 失败，请手动在 Build Settings 里切换");
                return;
            }

            // 出包前必须重新生成 + 拷贝（Generate/All 的产物、StreamingAssets 的内容都可能过期）
            Run();

            Directory.CreateDirectory(OutputDir);
            var opt = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutputDir, "CryptWalker.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            Debug.Log($"[Build] 开始出包 → {opt.locationPathName}（IL2CPP 全量编译，可能十几分钟，别关 Unity）");
            BuildReport report = BuildPipeline.BuildPlayer(opt);
            BuildSummary sum = report.summary;

            if (sum.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build] 出包成功 ✓  {opt.locationPathName}\n" +
                          $"        用时 {sum.totalTime.TotalMinutes:F1} 分钟  体积 {sum.totalSize / (1024 * 1024)} MB\n" +
                          $"        验证：启动包内 CryptWalker.exe，Player.log 里应出现「已读取热更 dll」与「热更程序集加载成功」");
            }
            else
            {
                Debug.LogError($"[Build] 出包失败 ✗ {sum.result}，共 {sum.totalErrors} 条错误，详见 Console");
            }
        }

        /// <summary>Generate/All + 把两类 dll 拷进 StreamingAssets</summary>
        private static void Run()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var settings = HybridCLRSettings.Instance;
            if (settings == null || !settings.enable)
            {
                Debug.LogError("[Build] HybridCLR 设置不可用（enable=false？）。先跑 CryptWalker/重新应用 HybridCLR 设置（诊断）");
                return;
            }

            // ---------- 1) Generate/All ----------
            // 产出：HybridCLRGenerate/link.xml、AOTGenericReferences.cs、MethodBridge、
            //       HotUpdateDlls/<target>/*.dll、AssembliesPostIl2CppStrip/<target>/*.dll
            Debug.Log("[Build] 执行 HybridCLR/Generate/All ...");
            PrebuildCommand.GenerateAll();

            // ---------- 2) 热更 dll → StreamingAssets/HybridCLR/HotUpdateDlls ----------
            // 只拷 HybridCLR 配置里的热更程序集。HotUpdateDlls 目录里还会有依赖程序集的编译产物
            // （例如 CryptWalker.Main.Runtime.dll），那些**不能**进包 —— 它们在 AOT 里已经有了，
            // 再装一份会出现同名双程序集，热更侧的类型全部对不上。
            string hotDir = Path.Combine(SettingsUtil.ProjectDir, SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target));
            var hotNames = SettingsUtil.HotUpdateAssemblyNamesExcludePreserved;
            if (hotNames == null || hotNames.Count == 0)
            {
                Debug.LogError("[Build] 没有配置热更程序集。到 HybridCLR/Settings... 把 " +
                               "Assets/HotUpdate/Scripts/CryptWalker.HotUpdate.asmdef 加进 Hot Update Assembly Definitions");
                return;
            }

            string hotDst = Path.Combine(StreamingAssetsRoot, HotUpdateDllSubDir);
            ResetDir(hotDst);   // 先清空，避免改名/删除过的旧 dll 残留进包
            int hotOk = hotNames.Count(n => CopyIfExists(Path.Combine(hotDir, n + ".dll"), Path.Combine(hotDst, n + ".dll")));

            // ---------- 3) AOT 补充元数据 → StreamingAssets/HybridCLR/AOTMetadata ----------
            // 注意源目录是 AssembliesPostIl2CppStrip（IL2CPP 裁剪后的 AOT dll），
            // 不是 AssembliesPostIl2CppPatch —— 后者不存在。
            string stripDir = Path.Combine(SettingsUtil.ProjectDir, SettingsUtil.GetAssembliesPostIl2CppStripDir(target));
            string[] aotNames = (settings.patchAOTAssemblies != null && settings.patchAOTAssemblies.Length > 0)
                ? settings.patchAOTAssemblies
                : FallbackAotAssemblies;

            string aotDst = Path.Combine(StreamingAssetsRoot, AotMetadataSubDir);
            ResetDir(aotDst);
            int aotOk = aotNames.Count(n => CopyIfExists(Path.Combine(stripDir, n + ".dll"), Path.Combine(aotDst, n + ".dll")));

            AssetDatabase.Refresh();

            Debug.Log($"[Build] 完成：热更 dll {hotOk}/{hotNames.Count} → {HotUpdateDllSubDir}；" +
                      $"AOT 元数据 {aotOk}/{aotNames.Length} → {AotMetadataSubDir}\n" +
                      $"        link.xml: {DescribeLinkXml(settings.outputLinkFile)}\n" +
                      $"        下一步：CryptWalker/构建/② 出包（Windows x64）");
        }

        private static string DescribeLinkXml(string relativePath)
        {
            string inProject = Path.Combine(SettingsUtil.ProjectDir, relativePath);
            string inAssets = Path.Combine(Application.dataPath, relativePath);
            if (File.Exists(inAssets)) return $"已生成 ✓  {inAssets}";
            if (File.Exists(inProject)) return $"已生成 ✓  {inProject}";
            return $"【缺失 ✗ 请检查 Generate/All 的报错】期望在 {inProject}";
        }

        private static void ResetDir(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
        }

        private static bool CopyIfExists(string src, string dst)
        {
            if (!File.Exists(src)) return false;
            File.Copy(src, dst, true);
            return true;
        }
    }
}
