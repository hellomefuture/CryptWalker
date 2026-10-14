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

        /// <summary>
        /// 获取关卡配置（ScriptableObject，prefab 直接引用 Art/Prefabs/）。
        /// 由场景里的 LevelConfigHolder (AOT MonoBehaviour) 在 Awake 时通过 SetLevelConfig 注入。
        /// HotUpdate 端 GameFlowMachine 在 GameState.OnEnter 调用。
        /// </summary>
        CryptWalker.Main.Data.LevelConfig GetLevelConfig();

        /// <summary>
        /// 获取 UI 配置（ScriptableObject，prefab 直接引用 Art/Prefabs/UI_prefabs/）。
        /// 由场景里的 UIConfigHolder (AOT MonoBehaviour) 在 Start 时通过 SetUIConfig 注入。
        /// HotUpdate 端 UI 面板（MainMenuPanel/HUDPanel...）通过 IAppBridge.GetUIConfig() 拿 prefab 引用。
        /// </summary>
        CryptWalker.Main.Data.UIConfig GetUIConfig();

        // ===================================================================
        // 资源加载（AGENTS.md §3.9：所有资源必须走 YooAsset；
        //          §3.1-2：热更侧只能经本接口访问 AOT 能力，不得直接引用 YooAssetService）
        // ===================================================================

        /// <summary>资源系统是否就绪（YooAsset 未初始化时为 false，业务侧需降级到直引用兜底）</summary>
        bool IsAssetAvailable { get; }

        /// <summary>
        /// 异步加载资源（按 address）。失败返回 null 且不抛异常 —— 由桥接内部降级 + 记录日志。
        /// 注意：加载到的句柄由桥接缓存，业务侧不再使用时需调 ReleaseAsset(address) 成对释放（§3.9-44）。
        /// </summary>
        System.Threading.Tasks.Task<T> LoadAssetAsync<T>(string address) where T : UnityEngine.Object;

        /// <summary>异步实例化预制体（按 address）。失败返回 null。句柄同样需 ReleaseAsset(address) 释放。</summary>
        System.Threading.Tasks.Task<UnityEngine.GameObject> InstantiateAsync(string address, UnityEngine.Transform parent = null);

        /// <summary>
        /// 为已加载的 address 追加一份引用（不重复加载）。返回 false = 该地址当前没被加载。
        /// 用于资源被多处共享时，让"几处使用就几处 Release"能对上。
        /// </summary>
        bool AttachAsset(string address);

        /// <summary>
        /// 释放 address 的一份引用（与 LoadAssetAsync / InstantiateAsync / AttachAsset 成对）。
        /// 引用计数归零才会真正释放底层句柄 —— 所以多释放一次不会误伤别人，但也别忘记释放。
        /// </summary>
        void ReleaseAsset(string address);

        // ===================================================================
        // 加载界面（AOT 侧实现；热更侧在切场景 / 生成长耗时内容时驱动）
        //
        // 为什么它必须走桥接、且实现在 AOT：
        //   加载界面要在【热更 dll 加载之前】就能显示出来（否则"加载界面自己还没加载出来"），
        //   所以它天然属于 AOT；热更侧只能通过本接口驱动它。
        // ===================================================================

        /// <summary>显示加载界面。progress 是初始进度 0~1。</summary>
        void ShowLoading(float progress = 0f);

        /// <summary>
        /// 更新加载进度（0~1）。会顺带保证界面可见（进度来了就该显示）。
        /// 提示文案由 AOT 侧按间隔自动轮播，热更侧不用管。
        /// </summary>
        void SetLoadingProgress(float progress);

        /// <summary>隐藏加载界面（淡出）。</summary>
        void HideLoading();
    }

    public enum LogLevel
    {
        Trace,
        Info,
        Warn,
        Error,
    }
}