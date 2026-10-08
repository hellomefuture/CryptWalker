using System.Collections.Generic;
using CryptWalker.HotUpdate.Data;
using CryptWalker.HotUpdate.Pool;

namespace CryptWalker.HotUpdate.Core
{
    /// <summary>
    /// 模块注册表 + 统一初始化入口（对应框架设计 3.9）。
    /// 目标：GameRoot 只调 Modules.InitAll()，新增模块不改启动代码。
    /// 使用：
    ///   public static PoolManager Pool = Register(new PoolManager());   // 声明即注册
    ///   GameRoot.Run() 里调 Modules.InitAll()，按注册顺序逐个 Init。
    /// </summary>
    public static class Modules
    {
        // 已注册模块列表。Init 按注册顺序（被依赖方先注册先就绪）；
        // Shutdown 逆序（后初始化的先清理）。
        private static readonly List<IModule> s_modules = new List<IModule>();

        // —— 模块实例（声明即注册，按依赖顺序排列：被依赖方在上）——
        // 配置模块：其他业务/模块 Init 时可能查配置，放最前
        public static readonly DataService Data = Register(DataService.Instance);
        // 对象池（必须在 UI 和 Audio 之前——AGENTS.md 3.3 硬约束）
        public static readonly PoolManager Pool = Register(new PoolManager());
        // UI 服务（必须晚于 Pool —— UI 内部可能用到池）
        public static readonly UI.UIService UI = Register(new UI.UIService());
        // UI 面板管理器（必须晚于 UIService —— 开面板要按 UILayer 取对应的 Canvas 层）
        public static readonly UI.UIManager UIManager = Register(new UI.UIManager());
        // public static AudioManager  Audio  = Register(new AudioManager());   // 以后

        /// <summary>
        /// 注册模块，并返回自身。
        /// 返回自身是为了支持一行式声明："声明静态字段 = 顺便完成注册"。
        /// </summary>
        public static T Register<T>(T module) where T : IModule
        {
            s_modules.Add(module);
            return module;
        }

        /// <summary>按注册顺序初始化所有模块（先注册 = 先 Init = 被依赖方先就绪）</summary>
        public static void InitAll()
        {
            for (int i = 0; i < s_modules.Count; i++)
                s_modules[i].Init();
            Log.Info($"[Modules] 已初始化 {s_modules.Count} 个模块");
        }

        /// <summary>逆序清理（后初始化的先清理），并清空注册表</summary>
        public static void ShutdownAll()
        {
            for (int i = s_modules.Count - 1; i >= 0; i--)
                s_modules[i].Shutdown();
            s_modules.Clear();
        }
    }
}
