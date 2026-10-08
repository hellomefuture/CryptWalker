using System;
using System.IO;
using UnityEngine;

namespace CryptWalker.Main.Service
{
    /// <summary>
    /// 热更 dll 字节来源。
    /// 编辑器阶段：直接从 HybridCLR 构建输出的本地 dll 读取，便于先跑通热更链路；
    /// 真机/正式：改为由 YooAsset 下载的资源包提供（见 HotUpdateProvider 注释）。
    /// </summary>
    public static class HotUpdateProvider
    {
        /// <summary>
        /// HybridCLR 默认热更 dll 输出根目录（HybridCLRSettings.hotUpdateDllCompileOutputRootDir 的默认值）
        /// 实际产物在 {根目录}/{BuildTarget}/ 下，例如 HybridCLRData/HotUpdateDlls/StandaloneWindows64/
        /// 修复：旧值 "BuildOutput/HotUpdate" 拼上 nameof(UnityEngine) 后路径无意义，永远找不到 dll
        /// </summary>
        private const string HybridClrDllRoot = "HybridCLRData/HotUpdateDlls";

        private const string HotDllName = "CryptWalker.HotUpdate.dll";

        /// <summary>加载热更程序集字节。<paramref name="scriptAssembliesDir"/> 由构建产出注入。</summary>
        public static byte[] LoadHotUpdateDll(string scriptAssembliesDir)
        {
            string path = LocateDll(scriptAssembliesDir, HotDllName);
            byte[] bytes = File.ReadAllBytes(path);
            Debug.Log($"[HotUpdateProvider] 已读取热更 dll: {path} ({bytes.Length} bytes)");
            return bytes;
        }

        /// <summary>加载跑 AOT 补充元数据所需的 dll 列表（HybridCLR LoadMetadataForAOTAssembly 用）</summary>
        public static byte[][] LoadAotMetadataDlls(string scriptAssembliesDir, string[] aotAssemblyNames)
        {
            var list = new System.Collections.Generic.List<byte[]>(aotAssemblyNames.Length);
            foreach (var name in aotAssemblyNames)
            {
                string path = LocateDll(scriptAssembliesDir, name + ".dll");
                if (File.Exists(path))
                    list.Add(File.ReadAllBytes(path));
            }
            return list.ToArray();
        }

        private static string LocateDll(string scriptAssembliesDir, string dllName)
        {
            string[] roots = GetSearchRoots(scriptAssembliesDir);

            foreach (var root in roots)
            {
                string p = Path.Combine(root, dllName);
                if (File.Exists(p)) return p;
            }
            throw new FileNotFoundException($"热更 dll 未找到: {dllName}，请先执行 HybridCLR Build 或确认路径: {string.Join(" / ", roots)}");
        }

        /// <summary>
        /// 按优先级给出 dll 搜索根目录：
        ///   1) HybridCLR Build 产物（带平台子目录，正式构建链路）
        ///   2) HybridCLR Build 产物根目录（部分版本不带平台子目录）
        ///   3) StreamingAssets/HybridCLR/HotUpdateDlls（随包内置的热更 dll）
        ///   4) Library/ScriptAssemblies（Unity 对本 asmdef 的直接编译产物，开发期免 Build 兜底）
        /// </summary>
        private static string[] GetSearchRoots(string scriptAssembliesDir)
        {
            string projectRoot = Path.Combine(Application.dataPath, "..");
            var roots = new System.Collections.Generic.List<string>(4);

            // 1) 带平台子目录的 Build 产物（需编辑器 API 取当前构建目标）
#if UNITY_EDITOR
            string buildTarget = UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
            roots.Add(Path.Combine(projectRoot, HybridClrDllRoot, buildTarget));
#endif
            // 2) 不带平台子目录
            roots.Add(Path.Combine(projectRoot, HybridClrDllRoot));
            // 3) 随包内置（真机 StreamingAssets）
            roots.Add(Path.Combine(Application.streamingAssetsPath, "HybridCLR", "HotUpdateDlls"));
            // 4) 开发期兜底
            roots.Add(scriptAssembliesDir);

            return roots.ToArray();
        }
    }
}