using CryptWalker.Main;

namespace CryptWalker.HotUpdate.Core
{
    /// <summary>
    /// 统一日志（对应框架设计 3.7）。
    /// 职责：把 bridge.Log 的 LogLevel 细节包起来，业务侧只调 Info/Warn/Error。
    /// 好处：
    ///   1. 业务不依赖 AOT 的 LogLevel 枚举（面向语义编程）
    ///   2. 统一入口 —— 以后要加"日志写文件 / Release 过滤"只改这一个类
    /// 使用：GameRoot.Run 里最先 Log.Init(bridge)，之后全局可用。
    /// </summary>
    public static class Log
    {
        private static IAppBridge _bridge;

        /// <summary>必须最先调用（拿到 AOT 桥接后立刻初始化）</summary>
        public static void Init(IAppBridge bridge)
        {
            _bridge = bridge;
        }

        public static void Info(string message)
        {
            _bridge?.Log(LogLevel.Info, message);
        }

        public static void Warn(string message)
        {
            _bridge?.Log(LogLevel.Warn, message);
        }

        public static void Error(string message)
        {
            _bridge?.Log(LogLevel.Error, message);
        }
    }
}
