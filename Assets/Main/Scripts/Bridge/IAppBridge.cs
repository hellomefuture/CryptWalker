namespace CryptWalker.Main
{
    /// <summary>
    /// 热更程序集访问主工程能力的唯一通道。
    /// 纪律：热更代码只能通过本桥接访问主工程能力，禁止直接绑定 AOT 业务类，
    /// 以保证主工程(AOT)保持轻薄、业务全部可热更。
    /// </summary>
    public interface IAppBridge
    {
        /// <summary>可写持久化目录（用于放置下载的热更 dll / 资源缓存）</summary>
        string PersistentDataPath { get; }

        /// <summary>日志（透传到主工程的统一日志）</summary>
        void Log(LogLevel level, string message);

        /// <summary>请求安全退出游戏</summary>
        void Quit();
    }

    public enum LogLevel
    {
        Trace,
        Info,
        Warn,
        Error,
    }
}