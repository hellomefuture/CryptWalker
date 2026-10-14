using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace CryptWalker.Main.Service
{
    /// <summary>
    /// YooAsset 资源服务（封装层）—— 针对已安装的 YooAsset 2.3.16 编写。
    ///
    /// 设计目标：
    ///   1) 业务系统不直接碰 YooAsset API（避免 API 变更传染、热更升级成本）
    ///   2) 统一"按 address 取资源"的入口 + 缓存
    ///   3) 三个工作模式：编辑器模拟 / 离线单机 / 联网热更
    ///   4) 接口稳定：所有方法签名不暴露 YooAsset 类型
    ///
    /// ⚠️ 1.5.8 → 2.3.16 Breaking Changes（已适配）：
    ///   - 类型重命名：`AssetOperationHandle` → `AssetHandle`（其他 OperationHandle 同理）
    ///   - DestroyPackage(string) 移除，改用 package.DestroyAsync() + RemovePackage(string)
    ///   - HandleBase.Task / Status / LastError / IsValid / Release() 都保留，可继续 await
    ///   - HandleBase.WaitForAsyncComplete() 是同步等待替代方案
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
        private static readonly Dictionary<string, AssetHandle> s_assetHandleCache
            = new Dictionary<string, AssetHandle>();
        private static readonly Dictionary<string, AllAssetsHandle> s_allHandleCache
            = new Dictionary<string, AllAssetsHandle>();
        private static readonly Dictionary<string, SubAssetsHandle> s_subHandleCache
            = new Dictionary<string, SubAssetsHandle>();
        private static readonly Dictionary<string, RawFileHandle> s_rawHandleCache
            = new Dictionary<string, RawFileHandle>();
        // 预制体实例化句柄缓存（修复：原 InstantiateAsync 加载后既不缓存也不释放 → 引用计数只增不减）
        private static readonly Dictionary<string, AssetHandle> s_prefabHandleCache
            = new Dictionary<string, AssetHandle>();
        // 场景句柄缓存（修复：原 LoadSceneAsync 同样丢失句柄）
        private static readonly Dictionary<string, SceneHandle> s_sceneHandleCache
            = new Dictionary<string, SceneHandle>();

        // —— 引用计数（AGENTS.md §3.9-44「资源加载/卸载必须成对，避免引用计数泄漏」）——
        //
        // 为什么必须自己数：YooAsset 原生 handle.Release() 不区分"我自己的释放"和"共享的释放"。
        // 两处业务加载同一 address 时，先结束的那处一 Release 就把句柄销毁了 → 另一处立刻空引用。
        // 加上引用计数后，"几处 Load 就几处 Release"，只有归零才真正释放底层句柄。
        //
        // 注意：按 address 计数。YooAsset 内部还会自行对 AssetBundle 计数，
        // 所以"不同 address 恰好同包"的情况是安全的（不会因为释放 A 而弄坏 B）。
        private static readonly Dictionary<string, int> s_assetRefCount
            = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> s_prefabRefCount
            = new Dictionary<string, int>();

        private static void AddRef(Dictionary<string, int> table, string address)
        {
            table.TryGetValue(address, out int count);
            table[address] = count + 1;
        }

        /// <summary>减一次引用。返回 true 表示归零、调用方应当真正释放句柄。</summary>
        private static bool DropRef(Dictionary<string, int> table, string address)
        {
            if (!table.TryGetValue(address, out int count)) return false;
            count--;
            if (count > 0)
            {
                table[address] = count;
                return false;
            }
            table.Remove(address);
            return true;
        }

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

            // 2.3.16 顺序：必须先 CreatePackage（GetPackage 在没有时会 LogError）
            _package = YooAssets.CreatePackage(DefaultPackageName);

            InitializeParameters initParams = BuildParameters(mode);
            InitializationOperation initOp = _package.InitializeAsync(initParams);
            await initOp.Task;   // AsyncOperationBase.Task 保留

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
                    // 2.3.16：SimulateManifestFilePath 字段删除，改用 EditorFileSystemParameters
                    p.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                        packageRoot: GetEditorPackageRoot());
                    return p;
                }
                case EPlayMode.Offline:
                {
                    var p = new OfflinePlayModeParameters();
                    p.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                    return p;
                }
                case EPlayMode.Host:
                {
                    var p = new HostPlayModeParameters();
                    p.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                    // CacheFileSystemParameters 需要 IRemoteServices（CDN 服务），开发期留空
                    return p;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        private static string GetEditorPackageRoot()
        {
#if UNITY_EDITOR
            return $"{Application.dataPath}/../Temp/YooAsset/{DefaultPackageName}";
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
            {
                AddRef(s_assetRefCount, address);   // 命中缓存 = 又一处使用者，引用 +1（调用方之后要 Release 一次）
                return cached.AssetObject as T;
            }

            AssetHandle handle = _package.LoadAssetAsync<T>(address);  // 2.3.16 仍保留 .Task
            await handle.Task;
            if (handle.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetService] LoadAssetAsync 失败: {address} | {handle.LastError}");
                return null;
            }
            s_assetHandleCache[address] = handle;
            AddRef(s_assetRefCount, address);
            return handle.AssetObject as T;
        }

        /// <summary>释放一份资源引用（与 LoadAssetAsync / Attach 成对）。只有引用归零才真正销毁句柄。</summary>
        public static void ReleaseAsset(string address)
        {
            if (!DropRef(s_assetRefCount, address)) return;

            if (s_assetHandleCache.TryGetValue(address, out var h))
            {
                h.Release();
                s_assetHandleCache.Remove(address);
            }
        }

        /// <summary>
        /// 为已加载的资源再取一份引用（不重复加载），交给另一处使用者。
        /// 返回 false = 该 address 当前没被加载（调用方应先 LoadAssetAsync）。
        /// 用途：资源被多处共享时，让"几处使用就几处 Release"能对上，
        ///       避免第一处用完整理性地把别人还在用的资源释放掉。
        /// </summary>
        public static bool Attach(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;

            // 同 address 只会落在其中一个缓存里（取决于用的是 Load 还是 Instantiate）
            if (s_prefabHandleCache.TryGetValue(address, out var prefab) && prefab.IsValid)
            {
                AddRef(s_prefabRefCount, address);
                return true;
            }
            if (s_assetHandleCache.TryGetValue(address, out var asset) && asset.IsValid)
            {
                AddRef(s_assetRefCount, address);
                return true;
            }
            return false;
        }

        // ===================================================================
        // 3. 整目录加载
        // ===================================================================

        public static async Task<IReadOnlyList<T>> LoadAllAssetsAsync<T>(string locationOrTag) where T : UnityEngine.Object
        {
            if (!IsAvailable) throw new InvalidOperationException("[YooAssetService] 未初始化");
            if (s_allHandleCache.TryGetValue(locationOrTag, out var cached) && cached.IsValid)
                return FilterByType<T>(cached.AllAssetObjects);

            AllAssetsHandle handle = _package.LoadAllAssetsAsync<T>(locationOrTag);
            await handle.Task;
            if (handle.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetService] LoadAllAssetsAsync 失败: {locationOrTag} | {handle.LastError}");
                handle.Release();
                return null;
            }
            s_allHandleCache[locationOrTag] = handle;
            return FilterByType<T>(handle.AllAssetObjects);
        }

        /// <summary>释放整目录句柄（与 LoadAllAssetsAsync 成对调用）</summary>
        public static void ReleaseAllAssets(string locationOrTag)
        {
            if (s_allHandleCache.TryGetValue(locationOrTag, out var h))
            {
                h.Release();
                s_allHandleCache.Remove(locationOrTag);
            }
        }

        /// <summary>
        /// 修复：AllAssetObjects 的运行时类型是 IReadOnlyList&lt;UnityEngine.Object&gt;，
        /// 原先的 `AllAssetObjects as IReadOnlyList&lt;T&gt;` 属反向协变强转，C# 协变只支持"子→父"，
        /// 因此该表达式恒为 null（调用方永远拿不到资源）。改为显式遍历 + is 判定得到真实的 List&lt;T&gt;。
        /// </summary>
        private static IReadOnlyList<T> FilterByType<T>(IReadOnlyList<UnityEngine.Object> source) where T : UnityEngine.Object
        {
            if (source == null) return new List<T>(0);
            var result = new List<T>(source.Count);
            for (int i = 0; i < source.Count; i++)
                if (source[i] is T item) result.Add(item);
            return result;
        }

        // ===================================================================
        // 4. 原始字节（用于热更 dll 加载）
        // ===================================================================

        public static async Task<byte[]> LoadRawFileAsync(string address)
        {
            if (!IsAvailable) throw new InvalidOperationException("[YooAssetService] 未初始化");
            if (s_rawHandleCache.TryGetValue(address, out var cached) && cached.IsValid)
                return cached.GetRawFileData();

            RawFileHandle handle = _package.LoadRawFileAsync(address);
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

            // 命中缓存：句柄仍有效则直接再实例化一份（不重复加载），但仍要计一份引用 ——
            // 每个实例都算一处使用者，各自的 ReleasePrefab 要能对上
            if (s_prefabHandleCache.TryGetValue(address, out var cached) && cached.IsValid)
            {
                AddRef(s_prefabRefCount, address);
                return cached.InstantiateSync(parent);
            }

            AssetHandle handle = _package.LoadAssetAsync<GameObject>(address);
            await handle.Task;
            if (handle.Status != EOperationStatus.Succeed || handle.AssetObject == null)
            {
                Debug.LogError($"[YooAssetService] InstantiateAsync 失败: {address} | {handle.LastError}");
                handle.Release();   // 失败也必须释放，否则引用计数泄漏
                return null;
            }
            // 修复：缓存句柄，交给 ReleasePrefab(address) 成对释放（AGENTS.md §3.9-44）
            s_prefabHandleCache[address] = handle;
            AddRef(s_prefabRefCount, address);
            // 2.3.16 提供 handle.InstantiateSync(parent)，避免 new InstantiateOperation
            return handle.InstantiateSync(parent);
        }

        /// <summary>
        /// 释放一份预制体引用（与 InstantiateAsync 成对）：引用归零才真正释放句柄。
        ///
        /// ⚠️ 调用时机：应当在【实例已经销毁之后】再释放。
        /// 句柄归零会让 YooAsset 卸载对应的 AssetBundle，而实例可能还共享着包里的图集/贴图；
        /// 先销毁实例再释放，可以避免"物体还在、贴图已经没了"的画面缺失。
        /// </summary>
        public static void ReleasePrefab(string address)
        {
            if (!DropRef(s_prefabRefCount, address)) return;

            if (s_prefabHandleCache.TryGetValue(address, out var h))
            {
                h.Release();
                s_prefabHandleCache.Remove(address);
            }
        }

        // ===================================================================
        // 6. 场景加载
        // ===================================================================

        public static async Task<bool> LoadSceneAsync(string sceneAddress, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (!IsAvailable) return false;

            // 先释放同地址旧句柄，避免重复加载同一场景导致引用计数叠加
            ReleaseScene(sceneAddress);

            SceneHandle handle = _package.LoadSceneAsync(sceneAddress, mode);
            await handle.Task;
            if (handle.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetService] LoadSceneAsync 失败: {sceneAddress} | {handle.LastError}");
                handle.Release();   // 失败也必须释放
                return false;
            }
            // 修复：缓存句柄（原先加载完直接丢句柄，无法释放）
            s_sceneHandleCache[sceneAddress] = handle;
            return true;
        }

        /// <summary>释放场景句柄（与 LoadSceneAsync 成对调用）</summary>
        public static void ReleaseScene(string sceneAddress)
        {
            if (s_sceneHandleCache.TryGetValue(sceneAddress, out var h))
            {
                h.Release();
                s_sceneHandleCache.Remove(sceneAddress);
            }
        }

        // ===================================================================
        // 7. 联网热更（仅 Host 模式生效）
        // ===================================================================

        /// <summary>请求最新版本号（2.3.16 API：appendTimeTicks 默认 true，timeout 默认 60s）</summary>
        public static async Task<string> RequestPackageVersionAsync()
        {
            if (!IsAvailable) return null;
            var op = _package.RequestPackageVersionAsync();
            await op.Task;
            return op.Status == EOperationStatus.Succeed ? op.PackageVersion : null;
        }

        /// <summary>更新清单（2.3.16 API：新增 timeout 默认 60s）</summary>
        public static async Task<bool> UpdatePackageManifestAsync(string packageVersion)
        {
            if (!IsAvailable) return false;
            var op = _package.UpdatePackageManifestAsync(packageVersion);
            await op.Task;
            return op.Status == EOperationStatus.Succeed;
        }

        /// <summary>创建并启动下载器（API 不变）。</summary>
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
            foreach (var h in s_prefabHandleCache.Values) h?.Release();
            foreach (var h in s_sceneHandleCache.Values) h?.Release();
            s_assetHandleCache.Clear();
            s_allHandleCache.Clear();
            s_subHandleCache.Clear();
            s_rawHandleCache.Clear();
            s_prefabHandleCache.Clear();
            s_sceneHandleCache.Clear();
            s_assetRefCount.Clear();
            s_prefabRefCount.Clear();
            Debug.Log("[YooAssetService] 句柄缓存已清空");
        }

        /// <summary>
        /// 关闭服务（2.3.16 改动）：
        ///   - 旧 YooAssets.DestroyPackage(name) 移除
        ///   - 改用 _package.DestroyAsync() 异步销毁包 → 完成后 YooAssets.RemovePackage(name)
        ///   - 最后 YooAssets.Destroy() 关全局
        /// </summary>
        public static async void Shutdown()
        {
            if (!_initialized) return;
            ClearAllCache();

            // 2.3.16：异步销毁包后，再从 YooAssets 列表移除
            if (_package != null)
            {
                var destroyOp = _package.DestroyAsync();
                await destroyOp.Task;
                YooAssets.RemovePackage(DefaultPackageName);
                _package = null;
            }
            YooAssets.Destroy();  // 2.3.16：全局销毁（无参数）

            _initialized = false;
            Debug.Log("[YooAssetService] 已关闭");
        }
    }
}