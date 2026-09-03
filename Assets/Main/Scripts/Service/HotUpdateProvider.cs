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
        /// <summary>HybridCLR 构建后热更 dll 所在根目录（编辑器本地路径）</summary>
        private const string LocalBuildRoot = "BuildOutput/HotUpdate";

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
            // 候选顺序：构建产物根 → Library/ScriptAssemblies（内置编译产物）
            string[] roots =
            {
                Path.Combine(Application.dataPath, "..", LocalBuildRoot, nameof(UnityEngine), "HotUpdate"),
                scriptAssembliesDir,
            };

            foreach (var root in roots)
            {
                string p = Path.Combine(root, dllName);
                if (File.Exists(p)) return p;
            }
            throw new FileNotFoundException($"热更 dll 未找到: {dllName}，请先执行 HybridCLR Build 或确认路径: {string.Join(" / ", roots)}");
        }
    }
}