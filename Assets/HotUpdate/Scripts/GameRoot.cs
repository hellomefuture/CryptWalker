using CryptWalker.Main;
using CryptWalker.HotUpdate.Data;

namespace CryptWalker.HotUpdate
{
    /// <summary>
    /// 热更程序集根入口。
    /// 由主工程(AOT) 反射调用 Run()，并收取 OnUpdateTick 每帧驱动。
    /// 所有游戏业务系统在此初始化。
    /// </summary>
    public static class GameRoot
    {
        /// <summary>主工程注入的桥接（访问持久化路径 / 日志 / 退出等 AOT 能力）</summary>
        public static IAppBridge Bridge { get; private set; }

        /// <summary>主工程 Launcher 在 Update 中每帧调用此委托</summary>
        public static System.Action OnUpdateTick;

        /// <summary>热更主流程状态机（骨架内先留引用，业务状态后续挂接）</summary>
        private static GameFlow.GameFlowMachine _flow;

        public static void Run(IAppBridge bridge)
        {
            Bridge = bridge;
            bridge.Log(LogLevel.Info, "[HotUpdate] GameRoot 启动");

            // 1) 数据层
            DataService.Instance.Initialize();

            // 2) 主流程状态机
            _flow = new GameFlow.GameFlowMachine();
            _flow.Start();

            // 3) 每帧驱动绑定
            OnUpdateTick = Tick;
        }

        private static void Tick()
        {
            _flow?.Tick();
        }
    }
}