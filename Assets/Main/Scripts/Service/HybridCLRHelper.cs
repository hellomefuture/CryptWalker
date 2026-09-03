using System;
using System.Reflection;
using UnityEngine;

namespace CryptWalker.Main.Service
{
    /// <summary>
    /// HybridCLR(华佗) 运行时代码热更装载助手。
    /// 职责：加载 AOT 补充元数据 + Assembly.Load 热更程序集 + 反射调用入口。
    /// 真正的 dll 字节由 HotUpdateProvider/YooAsset 提供，本类不做下载。
    ///
    /// 依赖：必须安装 HybridCLR 包（运行时程序集名 HybridCLR.Runtime）。
    /// </summary>
    public static class HybridCLRHelper
    {
        /// <summary>HybridCLR 是否可用（包是否安装）</summary>
        public const bool IsAvailable = true;

        /// <summary>
        /// 注册 IL2CPP AOT 程序集的补充元数据。
        /// 必须在使用任何泛型/反射前的早期阶段调用。
        /// </summary>
        public static void EnsureAotRuntime(byte[][] aotAssemblyDlls)
        {
            if (aotAssemblyDlls == null) return;
            foreach (var bytes in aotAssemblyDlls)
            {
                if (bytes == null || bytes.Length == 0) continue;
                var code = HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(
                    bytes, HybridCLR.HomologousImageMode.SuperSet);
                if (code != HybridCLR.LoadImageErrorCode.OK)
                    Debug.LogWarning($"[HybridCLRHelper] AOT 元数据加载失败: {code}");
            }
        }

        /// <summary>从字节加载热更程序集，返回程序集实例（业务入口通过反射调用）</summary>
        public static Assembly LoadHotUpdateAssembly(byte[] hotUpdateDllBytes)
        {
            if (hotUpdateDllBytes == null || hotUpdateDllBytes.Length == 0)
                throw new ArgumentException("热更 dll 字节为空", nameof(hotUpdateDllBytes));
            return Assembly.Load(hotUpdateDllBytes);
        }

        /// <summary>反射获取热更程序集入口类型（如 CryptWalker.HotUpdate.GameRoot）</summary>
        public static Type ResolveHotType(Assembly asm, string fullTypeName)
        {
            if (asm == null) throw new ArgumentNullException(nameof(asm));
            Type t = asm.GetType(fullTypeName);
            if (t == null) throw new InvalidOperationException($"热更入口类型不存在: {fullTypeName}");
            return t;
        }
    }
}