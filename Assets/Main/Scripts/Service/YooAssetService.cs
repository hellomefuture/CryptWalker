using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace CryptWalker.Main.Service
{
    /// <summary>
    /// YooAsset 资源服务（封装层）—— 针对已安装的 YooAsset 1.5.8 编写。
    ///
    /// 设计目标：
    ///   1) 业务系统不直接碰 YooAsset API（避免 API 变更传染、热更升级成本）
    ///   2) 统一"按 address 取资源"的入口 + 缓存
    ///   3) 三个工作模式：编辑器模拟 / 离线单机 / 联网热更
    ///   4) 接口稳定：所有方法签名不暴露 YooAsset 类型
    /// </summary>
    public static class YooAssetService
    {
        public const string DefaultPackageName = "CryptWalker";

        // —— 装配点（由调用方根据运行场景选择）——
        public enum EPlayMode
        {
            EditorSimulate,   // 编辑器内模拟（开发期常用）
            Offline,          // 离线单机（不连服）
            Host,             // 联网热更下载（真机/正式）
        }

        // —— 内部状态 ——
        private static ResourcePackage _package;
        private static bool _initialized;
        private static EPlayMode _currentMode;

        // 句柄缓存：key=address，value=加载好的句柄；避免重复加载
        private static readonly Dictionary<string, AssetOperationHandle> s_assetHandleCache
            = new Dictionary<string, AssetOperationHandle>();
        private static readonly Dictionary<string, AllAssetsOperationHandle> s_allHandleCache
            = new Dictionary<string, AllAssetsOperationHandle>();
        private static readonly Dictionary<string, SubAssetsOperationHandle> s_subHandleCache
            = new Dictionary<string, SubAssetsOperationHandle>();
        private static readonly Dictionary<string, RawFileOperationHandle> s_rawHandleCache
            = new Dictionary<string, RawFileOperationHandle>();

        // —— 对外只读状态 ——
        public static bool IsAvailable => _initialized && _package != null;
        public static EPlayMode CurrentMode => _currentMode;

        // ===================================================================
        // 1. 初始化
        // ===================================================================

        /// <summary>默认 EditorSimulate 模式</summary>
        public static async Task<bool> InitializeAsync()
            => await InitializeAsync(EPlayMode.EditorSimulate);

        /// <summary>完整初始化。失败返回 false，业务可降级</summary>
        public static async Task<bool> InitializeAsync(EPlayMode mode)
        {
            if (_initialized) return true;
            _currentMode = mode;

            YooAssets.Initialize();

            // 1.5.8 顺序：必须先 CreatePackage（GetPackage 在没有时会 LogError）
            _package = YooAssets.CreatePackage(DefaultPackageName);

            InitializeParameters initParams = BuildParameters(mode);
            InitializationOperation initOp = _package.InitializeAsync(initParams);
            await initOp.Task;   // 1.5.8: AsyncOperationBase 自带 .Task 属性

            if (initOp.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetService] InitializeAsync 失败: {initOp.Error}");
                return false;
            }

            _initialized = true;
            Debug.Log($"[YooAssetService] 初始化完成 mode={mode}");
            return true;
        }

        private static InitializeParameters BuildParameters(EPlayMode mode)
        {
            switch (mode)
            {
                case EPlayMode.EditorSimulate:
                {
                    var p = new EditorSimulateModeParameters();
                    p.SimulateManifestFilePath = GetSimulateManifestFilePath();
                    return p;
                }
                case EPlayMode.Offline:
                {
                    return new OfflinePlayModeParameters();
                }
                case EPlayMode.Host:
                {
                    var p = new HostPlayModeParameters();
                    // 真机/正式环境接 CDN 时实现 IRemoteServices
                    // p.RemoteServices = new YourRemoteServices();
                    return p;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        private static string GetSimulateManifestFilePath()
        {
#if UNITY_EDITOR
            return $"{Application.dataPath}/../Temp/YooAsset/{DefaultPackageName}_SimulateManifest.json";
#else
            return string.Empty;
#endif
        }

        // ===================================================================
        // 2. 单个资源加载
        // ===================================================================

        /// <summary>同步取已加载资源；未加载时返回 null</summary>
        public static T GetAsset<T>(string address) where T : UnityEngine.Object
        {
            if (!IsAvailable) return null;
            if (s_assetHandleCache.TryGetValue(address, out var h) && h.IsValid)
                return h.AssetObject as T;
            return null;
        }

        /// <summary>异步加载并缓存</summary>
        public static async Task<T> LoadAssetAsync<T>(string address) where T : UnityEngine.Object
        {
            if (!IsAvailable)
                throw new InvalidOperationException("[YooAssetService] 未初始化或不可用");

            if (s_assetHandleCache.TryGetValue(address, out var cached) && cached.IsValid)
                return cached.AssetObject as T;

            AssetOperationHandle handle = _package.LoadAssetAsync<T>(address);
            await handle.Task;   // 1.5.8 API
            if (handle.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetService] LoadAssetAsync 失败: {address} | {handle.LastError}");
                return null;
            }
            s_assetHandleCache[address] = handle;
            return handle.AssetObject as T;
        }

        /// <summary>释放单资源句柄缓存</summary>
        public static void ReleaseAsset(string address)
        {
            if (s_assetHandleCache.TryGetValue(address, out var h))
            {
                h.Release();
                s_assetHandleCache.Remove(address);
            }
        }

        // ===================================================================
        // 3. 整目录加载
        // ===================================================================

        public static async Task<IReadOnlyList<T>> LoadAllAssetsAsync<T>(string locationOrTag) where T : UnityEngine.Object
        {
            if (!IsAvailable) throw new InvalidOperationException("[YooAssetService] 未初始化");
            if (s_allHandleCache.TryGetValue(locationOrTag, out var cached) && cached.IsValid)
                return cached.AllAssetObjects as IReadOnlyList<T>;

            AllAssetsOperationHandle handle = _package.LoadAllAssetsAsync<T>(locationOrTag);
            await handle.Task;
            if (handle.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetService] LoadAllAssetsAsync 失败: {locationOrTag} | {handle.LastError}");
                return null;
            }
            s_allHandleCache[locationOrTag] = handle;
            return handle.AllAssetObjects as IReadOnlyList<T>;
        }

        // ===================================================================
        // 4. 原始字节（用于热更 dll 加载）
        // ===================================================================

        public static async Task<byte[]> LoadRawFileAsync(string address)
        {
            if (!IsAvailable) throw new InvalidOperationException("[YooAssetService] 未初始化");
            if (s_rawHandleCache.TryGetValue(address, out var cached) && cached.IsValid)
                return cached.GetRawFileData();

            RawFileOperationHandle handle = _package.LoadRawFileAsync(address);
            await handle.Task;
            if (handle.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetService] LoadRawFileAsync 失败: {address} | {handle.LastError}");
                return null;
            }
            s_rawHandleCache[address] = handle;
            return handle.GetRawFileData();
        }

        // ===================================================================
        // 5. 实例化（预制体用）
        // ===================================================================

        public static async Task<GameObject> InstantiateAsync(string address, Transform parent = null)
        {
            if (!IsAvailable) return null;
            AssetOperationHandle handle = _package.LoadAssetAsync<GameObject>(address);
            await handle.Task;
            if (handle.Status != EOperationStatus.Succeed || handle.AssetObject == null)
                return null;
            // 1.5.8 提供 InstantiateAsync()，它会再开一个 InstantiateOperation；同步版更省事
            return UnityEngine.Object.Instantiate(handle.AssetObject as GameObject, parent);
        }

        // ===================================================================
        // 6. 场景加载
        // ===================================================================

        public static async Task<bool> LoadSceneAsync(string sceneAddress, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (!IsAvailable) return false;
            SceneOperationHandle handle = _package.LoadSceneAsync(sceneAddress, mode);
            await handle.Task;
            return handle.Status == EOperationStatus.Succeed;
        }

        // ===================================================================
        // 7. 联网热更（仅 Host 模式生效）
        // ===================================================================

        /// <summary>请求最新版本号（YooAsset 1.5.8 API，无参数）</summary>
        public static async Task<string> RequestPackageVersionAsync()
        {
            if (!IsAvailable) return null;
            var op = _package.UpdatePackageVersionAsync();
            await op.Task;
            return op.Status == EOperationStatus.Succeed ? op.PackageVersion : null;
        }

        /// <summary>更新清单（YooAsset 1.5.8 API）</summary>
        public static async Task<bool> UpdatePackageManifestAsync(string packageVersion)
        {
            if (!IsAvailable) return false;
            var op = _package.UpdatePackageManifestAsync(packageVersion);
            await op.Task;
            return op.Status == EOperationStatus.Succeed;
        }

        /// <summary>
        /// 创建并启动下载器（YooAsset 1.5.8 走回调而非轮询）。
        /// 业务侧注册 OnDownloadProgressCallback / OnDownloadOverCallback 即可。
        /// </summary>
        public static ResourceDownloaderOperation CreateDownloader()
        {
            if (!IsAvailable) return null;
            return _package.CreateResourceDownloader(downloadingMaxNumber: 10, failedTryAgain: 3);
        }

        // ===================================================================
        // 8. 关闭/清空
        // ===================================================================

        public static void ClearAllCache()
        {
            foreach (var h in s_assetHandleCache.Values) h?.Release();
            foreach (var h in s_allHandleCache.Values) h?.Release();
            foreach (var h in s_subHandleCache.Values) h?.Release();
            foreach (var h in s_rawHandleCache.Values) h?.Release();
            s_assetHandleCache.Clear();
            s_allHandleCache.Clear();
            s_subHandleCache.Clear();
            s_rawHandleCache.Clear();
            Debug.Log("[YooAssetService] 句柄缓存已清空");
        }

        public static void Shutdown()
        {
            if (!_initialized) return;
            ClearAllCache();
            YooAssets.DestroyPackage(DefaultPackageName);   // 1.5.8: 按 string 名字
            _package = null;
            _initialized = false;
            Debug.Log("[YooAssetService] 已关闭");
        }
    }
}