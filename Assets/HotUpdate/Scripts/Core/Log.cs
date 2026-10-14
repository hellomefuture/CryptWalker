using System.Diagnostics;   // ConditionalAttribute
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
    ///
    /// ===== 分层策略（[Conditional] 编译期剔除）=====
    /// Info 挂了 [Conditional("DEBUG")] + [Conditional("UNITY_EDITOR")]：
    ///   · 编辑器和 Development Build → 保留（要看得到日志）
    ///   · Release 包 → 调用点被【编译器整句删除】，连字符串拼接都不发生
    ///
    /// 为什么必须用 [Conditional] 而不是运行时 if：
    ///   运行时 if 只能拦住"要不要输出"，拦不住"要不要拼字符串"——
    ///   Log.Info($"[X] {a} {b}") 里的 $"" 在调用【之前】就已经算完了，
    ///   所以哪怕是 Release，每帧的热路径日志照样在产生 GC 垃圾。
    ///   [Conditional] 是编译期行为，整个调用表达式一起消失，零开销。
    ///
    /// ⚠️ 两条使用纪律：
    ///   1. 不要在日志参数里写有副作用的表达式 —— Release 下它不会被执行
    ///      ❌ Log.Info($"[X] {list.Count}")   ← Count 读取本身没副作用，安全
    ///      ❌ Log.Info($"[X] {NextId()}")     ← NextId() 在 Release 下不执行，语义变了
    ///   2. Warn / Error 【故意不加】Conditional：
    ///      它们频率低、且通常意味着"配置错了/出问题了"——
    ///      恰恰是 Release 包里最需要看到的东西，剔除等于让线上排查变瞎。
    ///
    /// 想在 Release 包里临时看 Info：把下面两个 [Conditional] 注释掉、重跑
    ///   CryptWalker/构建/① （只重编热更 dll，几秒），不用重出整包。
    /// </summary>
    public static class Log
    {
        private static IAppBridge _bridge;

        /// <summary>必须最先调用（拿到 AOT 桥接后立刻初始化）</summary>
        public static void Init(IAppBridge bridge)
        {
            _bridge = bridge;
        }

        [Conditional("DEBUG")]
        [Conditional("UNITY_EDITOR")]
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
