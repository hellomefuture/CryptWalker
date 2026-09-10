namespace CryptWalker.HotUpdate.Core
{
    /// <summary>
    /// 系统模块公共契约（对应框架设计 3.9）。
    /// 所有"管理一类能力"的系统（UIManager / AudioManager / PoolManager ...）实现此接口。
    /// 注意：这是 Manager（能力模块），不是 MVC 的 Model（数据）——Model 管数据，Manager 管能力。
    /// </summary>
    public interface IModule
    {
        /// <summary>游戏启动时统一调用（GameRoot.Run → Modules.InitAll → 本方法）</summary>
        void Init();

        /// <summary>退出/切场景时统一清理（可选；不需要清理的模块留空实现）</summary>
        void Shutdown();
    }
}
