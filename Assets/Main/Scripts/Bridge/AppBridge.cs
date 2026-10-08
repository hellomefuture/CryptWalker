using System.Threading.Tasks;
using UnityEngine;
using CryptWalker.Main.Data;
using CryptWalker.Main.Service;

namespace CryptWalker.Main
{
    /// <summary>
    /// 桥接实现：主工程为热更程序集注入的真实能力。
    /// </summary>
    public sealed class AppBridge : IAppBridge
    {
        public string PersistentDataPath { get; }

        // 关卡配置引用（由场景里的 LevelConfigHolder 在 Start 时通过 SetLevelConfig 注入）
        private LevelConfig _levelConfig;

        // UI 配置引用（由场景里的 UIConfigHolder 在 Start 时通过 SetUIConfig 注入）
        private UIConfig _uiConfig;

        /// <summary>
        /// 当前活动的 AppBridge 实例（主工程启动时构造并赋值）。
        /// 用于场景里的 MonoBehaviour（LevelConfigHolder）通过静态访问注入数据。
        /// </summary>
        public static AppBridge Instance { get; private set; }

        public AppBridge(string persistentDataPath)
        {
            Instance = this;
            PersistentDataPath = persistentDataPath;
        }

        /// <summary>
        /// 场景里的 LevelConfigHolder 调用，把持有 SO 的 MonoBehaviour 注入到桥接层。
        /// 热更端后续通过 GetLevelConfig() 拿。
        /// </summary>
        public void SetLevelConfig(LevelConfig config)
        {
            _levelConfig = config;
        }

        public LevelConfig GetLevelConfig() => _levelConfig;

        /// <summary>注入 UI 配置（UIConfigHolder 调用）</summary>
        public void SetUIConfig(UIConfig config) => _uiConfig = config;

        public UIConfig GetUIConfig() => _uiConfig;

        public void Log(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Info:
                    Debug.Log($"[HotUpdate] {message}");
                    break;
                case LogLevel.Warn:
                    Debug.LogWarning($"[HotUpdate] {message}");
                    break;
                case LogLevel.Error:
                    Debug.LogError($"[HotUpdate] {message}");
                    break;
            }
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ===================================================================
        // 资源加载：把 AOT 侧的 YooAssetService 包成桥接能力
        // 为什么这样写：
        //   1) 热更侧只认 IAppBridge，不直接引用 CryptWalker.Main.Service.YooAssetService
        //      → 资源层的实现可以被替换/升级而不影响热更 dll
        //   2) 统一在桥接里 try/catch 降级：资源加载失败只返回 null + 一条日志，
        //      不让异常穿透到热更业务（否则 async Task 里的异常容易被吞或变成未观察异常）
        // ===================================================================

        public bool IsAssetAvailable => YooAssetService.IsAvailable;

        public async Task<T> LoadAssetAsync<T>(string address) where T : UnityEngine.Object
        {
            if (!YooAssetService.IsAvailable) return null;
            try
            {
                return await YooAssetService.LoadAssetAsync<T>(address);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AppBridge] LoadAssetAsync 失败: {address} | {e.Message}");
                return null;
            }
        }

        public async Task<GameObject> InstantiateAsync(string address, Transform parent = null)
        {
            if (!YooAssetService.IsAvailable) return null;
            try
            {
                return await YooAssetService.InstantiateAsync(address, parent);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AppBridge] InstantiateAsync 失败: {address} | {e.Message}");
                return null;
            }
        }

        public void ReleaseAsset(string address)
        {
            if (string.IsNullOrEmpty(address)) return;
            YooAssetService.ReleaseAsset(address);
            // 预制体走的是独立句柄缓存，一并释放，保证"加载/卸载成对"
            YooAssetService.ReleasePrefab(address);
        }
    }
}