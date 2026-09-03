using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

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
    /// </summary>
    public sealed class DataService
    {
        private static readonly DataService s_instance = new DataService();
        public static DataService Instance => s_instance;

        // 仅缓存 GameConfigBase 子类
        private readonly Dictionary<string, GameConfigBase> _cache = new Dictionary<string, GameConfigBase>();

        private DataService() { }

        public void Initialize()
        {
            _cache.Clear();
            Debug.Log("[DataService] 数据服务就绪");
        }

        /// <summary>统一地址构造：asset://data/{resourceFolder}/{id}</summary>
        public static string MakeAddress(string resourceFolder, string id)
            => $"asset://data/{resourceFolder}/{id}";

        /// <summary>异步按 (resourceFolder, id) 加载并缓存</summary>
        public async Task<T> GetAsync<T>(string resourceFolder, string id) where T : GameConfigBase
        {
            string address = MakeAddress(resourceFolder, id);
            if (_cache.TryGetValue(address, out var cached)) return (T)cached;

            T loaded = await CryptWalker.Main.Service.YooAssetService.LoadAssetAsync<T>(address);
            if (loaded == null)
            {
                Debug.LogError($"[DataService] 加载配置失败: {address}");
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
            Debug.Log($"[DataService] 注入配置: {address}");
        }

        /// <summary>清理某地址缓存（热更后让业务重新加载）</summary>
        public void Evict(string address)
        {
            _cache.Remove(address);
            CryptWalker.Main.Service.YooAssetService.ReleaseAsset(address);
        }

        /// <summary>清空所有缓存（热更后批量刷新）</summary>
        public void ClearAll()
        {
            foreach (var addr in new List<string>(_cache.Keys))
                CryptWalker.Main.Service.YooAssetService.ReleaseAsset(addr);
            _cache.Clear();
        }
    }
}