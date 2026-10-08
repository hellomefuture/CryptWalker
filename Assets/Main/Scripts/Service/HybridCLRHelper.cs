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

        /// <summary>热更程序集名（与 Assets/HotUpdate/CryptWalker.HotUpdate.asmdef 的 name 一致）</summary>
        public const string HotUpdateAssemblyName = "CryptWalker.HotUpdate";

        /// <summary>从字节加载热更程序集，返回程序集实例（业务入口通过反射调用）</summary>
        public static Assembly LoadHotUpdateAssembly(byte[] hotUpdateDllBytes)
        {
            if (hotUpdateDllBytes == null || hotUpdateDllBytes.Length == 0)
                throw new ArgumentException("热更 dll 字节为空", nameof(hotUpdateDllBytes));
            return Assembly.Load(hotUpdateDllBytes);
        }

        /// <summary>
        /// 在 AppDomain 里找已加载的同名程序集；没有返回 null。
        ///
        /// ⚠️ 这是本项目最关键的一个修复点，务必看懂再用：
        ///
        ///   【编辑器】Assets/HotUpdate 是一个普通的 asmdef 程序集，Unity 启动时就已经把它加载进 AppDomain
        ///   （否则场景/Prefab 上挂的 EnemyBase、PlayerController 等组件根本无法存在）。
        ///   如果这里再 Assembly.Load(bytes)，就会得到【第二份】同名程序集。
        ///   Assembly.Load(byte[]) 在 Mono 下不会去重，它会实实在在地新建一个程序集对象。
        ///
        ///   两份程序集里的 CryptWalker.HotUpdate.Entity.Enemy.EnemyBase 是【两个不同的 Type】：
        ///     - 第 1 份（Unity 加载）：持有所有 Prefab/场景上的组件实例，由 Unity 驱动生命周期
        ///     - 第 2 份（Assembly.Load）：承载 GameRoot / DungeonGenerator 等全部游戏逻辑
        ///
        ///   于是热更侧代码里任何跨边界的类型判断都会失败：
        ///     GetComponent&lt;EnemyBase&gt;()、is EnemyBase、as EnemyBase、FindObjectsOfType&lt;EnemyBase&gt;()
        ///     → 对真实的敌人组件全部返回 null / false。
        ///   典型症状：DungeonGenerator 报"prefab 上找不到 EnemyBase 组件"，
        ///             且 enemy.Target / enemy.SetNavGrid 静默失效（敌人 A* 寻路退化回直线撞墙）。
        ///
        ///   【真机】HybridCLR 的 FilterHotFixAssemblies 会把热更程序集从打包列表中剔除
        ///   （见 HybridCLR.Editor.BuildProcessors.FilterHotFixAssemblies），
        ///   所以 AppDomain 里没有同名程序集 → 这时才走 Assembly.Load 分支，不会重复。
        /// </summary>
        public static Assembly FindLoadedAssembly(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return null;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                string name = assemblies[i].GetName().Name;
                if (string.Equals(name, assemblyName, StringComparison.Ordinal))
                    return assemblies[i];
            }
            return null;
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