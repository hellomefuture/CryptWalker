using System.Collections.Generic;
using System.Threading.Tasks;
using CryptWalker.HotUpdate.Core;

namespace CryptWalker.HotUpdate.Data
{
    /// <summary>
    /// 数据驱动核心服务：统一加载/缓存所有 SO 配置，按 ID 查询。
    /// 数据来源为 YooAsset 数据 bundle（运行时下载/加载），可随热更包增量下发。
    ///
    /// 使用约定：
    ///   - resourceFolder = 子目录, 如 "weapon"/"enemy"/"buff"
    ///   - id = 配置唯一ID(也是 asset 名)
    ///   - 最终 address = asset://data/{resourceFolder}/{id}
    ///   - DataService 会去 YooAsset 拉 address 对应的 SO 资产
    ///
    /// 实现 IModule：作为"配置模块"注册进 Modules，由 Modules.InitAll() 统一启动，
    /// GameRoot 不再直接调用（框架设计 3.9 模块化）。
    /// </summary>
    public sealed class DataService : IModule
    {
        private static readonly DataService s_instance = new DataService();
        public static DataService Instance => s_instance;

        // 仅缓存 GameConfigBase 子类
        private readonly Dictionary<string, GameConfigBase> _cache = new Dictionary<string, GameConfigBase>();

        private DataService() { }

        /// <summary>IModule.Init：由 Modules.InitAll() 在启动时调用</summary>
        public void Init()
        {
            _cache.Clear();
            Log.Info("[DataService] 数据服务就绪");
        }

        /// <summary>IModule.Shutdown：退出/重载时清空缓存并释放句柄</summary>
        public void Shutdown()
        {
            ClearAll();
        }

        /// <summary>统一地址构造：asset://data/{resourceFolder}/{id}</summary>
        public static string MakeAddress(string resourceFolder, string id)
            => $"asset://data/{resourceFolder}/{id}";

        /// <summary>异步按 (resourceFolder, id) 加载并缓存</summary>
        public async Task<T> GetAsync<T>(string resourceFolder, string id) where T : GameConfigBase
        {
            string address = MakeAddress(resourceFolder, id);
            if (_cache.TryGetValue(address, out var cached)) return (T)cached;

            // 修复：资源加载必须经 IAppBridge（AGENTS.md §3.1-2），不再直接引用 AOT 的 YooAssetService
            if (GameRoot.Bridge == null)
            {
                Log.Error("[DataService] IAppBridge 未注入，无法加载配置（GameRoot.Run 是否已执行？）");
                return null;
            }

            T loaded = await GameRoot.Bridge.LoadAssetAsync<T>(address);
            if (loaded == null)
            {
                Log.Error($"[DataService] 加载配置失败: {address}");
                return null;
            }

            _cache[address] = loaded;
            return loaded;
        }

        /// <summary>同步取已缓存；未加载则返回 null</summary>
        public T Get<T>(string resourceFolder, string id) where T : GameConfigBase
        {
            string address = MakeAddress(resourceFolder, id);
            return _cache.TryGetValue(address, out var v) ? (T)v : null;
        }

        /// <summary>手动注入（开发期/单测/数据热更刷新）</summary>
        public void RegisterConfig(string resourceFolder, string id, GameConfigBase config)
        {
            if (config == null) return;
            string address = MakeAddress(resourceFolder, id);
            _cache[address] = config;
            Log.Info($"[DataService] 注入配置: {address}");
        }

        /// <summary>清理某地址缓存（热更后让业务重新加载）</summary>
        public void Evict(string address)
        {
            _cache.Remove(address);
            GameRoot.Bridge?.ReleaseAsset(address);   // 经桥接释放，保持加载/卸载成对（§3.9-44）
        }

        /// <summary>清空所有缓存（热更后批量刷新）</summary>
        public void ClearAll()
        {
            foreach (var addr in new List<string>(_cache.Keys))
                GameRoot.Bridge?.ReleaseAsset(addr);
            _cache.Clear();
        }
    }
}
