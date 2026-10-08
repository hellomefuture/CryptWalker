using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CryptWalker.Main.Data;
using CryptWalker.HotUpdate.Core;
using UnityEngine;

namespace CryptWalker.HotUpdate.UI
{
    /// <summary>
    /// UI 面板管理器（AGENTS.md §5.3「面板打开/关闭用 UIManager」+ §3.4-21「UI 加载异步化」）。
    ///
    /// 职责（单一职责：只管"面板的生命周期"）：
    ///   1) 启动时把 UIConfig.panels 读成 panelId → UIPanelConfig 字典
    ///   2) OpenPanelAsync(panelId)：异步拿 prefab → 实例化到对应层 Canvas → 缓存
    ///   3) ClosePanel(panelId) / GetPanel(panelId)：关闭 / 查询已开面板
    ///
    /// 资源来源（C2 双字段，兼容开发期与发布期）：
    ///   - config.address 非空且桥接资源可用 → IAppBridge.LoadAssetAsync（进资源热更链路）
    ///   - 否则 → config.prefab 直引用（开发期零配置）
    ///   - 两者都失败 → 返回 null 并打 Error（调用方降级，不崩）
    ///
    /// 为什么单独一个 Manager 而不是塞进 UIService：
    ///   UIService 负责【渲染基础设施】（4 层 Canvas + UI 相机 + ScreenSpaceCamera 配置），
    ///   UIManager 负责【面板业务生命周期】。职责分离后，换渲染方案不动面板逻辑，反之亦然。
    /// </summary>
    public class UIManager : IModule
    {
        public static UIManager Instance { get; private set; }

        // panelId → 面板配置（启动时一次性建好）
        private readonly Dictionary<string, UIPanelConfig> _configs = new Dictionary<string, UIPanelConfig>();

        // panelId → 已打开的面板实例
        private readonly Dictionary<string, GameObject> _opened = new Dictionary<string, GameObject>();

        // 正在异步加载中的 panelId（用于处理"加载中就被要求关闭"的竞态）
        private readonly HashSet<string> _loading = new HashSet<string>();
        private readonly HashSet<string> _closedWhileLoading = new HashSet<string>();

        public void Init()
        {
            Instance = this;
            _configs.Clear();
            _opened.Clear();
            _loading.Clear();
            _closedWhileLoading.Clear();

            var cfg = GameRoot.Bridge != null ? GameRoot.Bridge.GetUIConfig() : null;
            if (cfg == null)
            {
                Log.Warn("[UIManager] UIConfig 为空 — 请在场景 _Boot 上挂 UIConfigHolder 并拖 UIConfig.asset");
                return;
            }

            foreach (var p in cfg.panels)
            {
                if (p == null || string.IsNullOrEmpty(p.panelId))
                {
                    Log.Warn("[UIManager] 跳过面板配置：panelId 未填");
                    continue;
                }
                if (_configs.ContainsKey(p.panelId))
                {
                    Log.Warn($"[UIManager] panelId 重复，后者被忽略: {p.panelId}");
                    continue;
                }
                _configs[p.panelId] = p;

                // 配置体检：address 填"资产路径"是最常见的误用（把文件夹/资产拖进字符串字段，
                // Unity 会把该资产的 project 路径写进字符串 → YooAsset 查不到 → 加载抛异常）
                if (!string.IsNullOrEmpty(p.address) && LooksLikeAssetPath(p.address))
                    Log.Warn($"[UIManager] {p.panelId}.address 看起来是【资产路径】而不是 YooAsset 地址：\"{p.address}\""
                           + "。开发期请把 address 留空（用 prefab 字段），发布期才填资源收集器里配置的 address。");
            }
            Log.Info($"[UIManager] 面板配置就绪 {_configs.Count} 个");
        }

        /// <summary>粗判字符串是不是"资产路径"（含盘符/反斜杠，或以 Assets/ 开头）</summary>
        private static bool LooksLikeAssetPath(string s)
            => s.IndexOf('\\') >= 0
            || s.Contains(":/")
            || s.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);

        public void Shutdown()
        {
            CloseAll();
            _configs.Clear();
            _loading.Clear();
            _closedWhileLoading.Clear();
            Instance = null;
        }

        /// <summary>
        /// 异步打开面板。
        /// 已打开 → 直接返回已有实例（幂等）；正在加载 → 返回 null（避免重复实例化）；
        /// 失败 → 返回 null 并打 Error（调用方自行降级）。
        /// </summary>
        public async Task<GameObject> OpenPanelAsync(string panelId)
        {
            if (string.IsNullOrEmpty(panelId)) return null;

            // 幂等：已开过就直接返回（Unity 的 null 重载能识别已销毁对象）
            if (_opened.TryGetValue(panelId, out var existing) && existing != null)
                return existing;

            if (_loading.Contains(panelId))
            {
                Log.Warn($"[UIManager] 面板 {panelId} 正在加载中，忽略重复请求");
                return null;
            }

            if (!_configs.TryGetValue(panelId, out var conf))
            {
                Log.Error($"[UIManager] 未注册的面板 panelId={panelId}（检查 UIConfig.panels）");
                return null;
            }

            Transform parent = UIService.Instance != null ? UIService.Instance.GetLayerRoot(conf.layer) : null;
            if (parent == null)
            {
                Log.Error($"[UIManager] 面板 {panelId} 找不到挂载层 {conf.layer}（UIService 未初始化？）");
                return null;
            }

            _loading.Add(panelId);
            GameObject panel = null;
            bool fromAddress = false;
            // 修复：资源加载统一经 IAppBridge（AGENTS.md §3.1-2），
            // 不再直接引用 AOT 的 CryptWalker.Main.Service.YooAssetService
            var bridge = GameRoot.Bridge;
            try
            {
                // 1) 先试 YooAsset 地址（发布期 / 资源热更链路）
                //    ⚠️ 必须用【内层 try/catch】隔离：地址非法时 YooAsset 会直接抛异常，
                //    若和下面的 prefab 兜底共用同一个 catch，异常会让兜底代码被跳过 → 面板开不出来。
                if (!string.IsNullOrEmpty(conf.address))
                {
                    if (bridge != null && bridge.IsAssetAvailable)
                    {
                        try
                        {
                            // 经桥接「加载 + 实例化」一步到位（句柄由 AOT 侧缓存，便于后续成对释放）
                            panel = await bridge.InstantiateAsync(conf.address, parent);
                            if (panel != null)
                            {
                                fromAddress = true;
                            }
                            else
                            {
                                Log.Warn($"[UIManager] 面板 {panelId} 地址加载返回空: address=\"{conf.address}\"");
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Warn($"[UIManager] 面板 {panelId} 地址加载异常: address=\"{conf.address}\" | {e.Message}");
                        }
                    }
                    else
                    {
                        Log.Warn($"[UIManager] 面板 {panelId} 配了 address 但 YooAsset 不可用，跳过: \"{conf.address}\"");
                    }
                }

                // 2) 兜底：prefab 直引用（开发期）。地址未配 / 返回空 / 抛异常都会走到这里
                if (panel == null && conf.prefab != null)
                {
                    panel = UnityEngine.Object.Instantiate(conf.prefab, parent);
                    if (!string.IsNullOrEmpty(conf.address))
                        Log.Warn($"[UIManager] 面板 {panelId} 已回退到 prefab 直引用（address 未生效）");
                }
            }
            finally
            {
                // 无论成败都要解除"加载中"标记，否则该面板后续永远被判定为"正在加载"
                _loading.Remove(panelId);
            }

            // 竞态：加载期间被要求关闭 → 丢弃刚实例化的对象
            if (_closedWhileLoading.Remove(panelId))
            {
                if (panel != null) UnityEngine.Object.Destroy(panel);
                Log.Info($"[UIManager] 面板 {panelId} 加载完成时已被关闭，直接丢弃");
                return null;
            }

            if (panel == null)
            {
                // 诊断信息要能立刻看出是【哪个来源】失败，避免含糊日志浪费时间
                Log.Error($"[UIManager] 面板 {panelId} 开不出来："
                        + $"address={(string.IsNullOrEmpty(conf.address) ? "<未填>" : $"\"{conf.address}\"")}，"
                        + $"prefab={(conf.prefab == null ? "<未拖>" : conf.prefab.name)}"
                        + "（开发期请把 address 留空、并给 prefab 拖上预制体）");
                return null;
            }

            panel.name = panelId;   // 覆盖 prefab 名 + "(Clone)" 后缀，避免场景里名字混乱
            _opened[panelId] = panel;
            Log.Info($"[UIManager] 打开面板 {panelId}（层={conf.layer}，来源={(fromAddress ? "address" : "prefab")}）");
            return panel;
        }

        /// <summary>关闭面板（未开则忽略；加载中则打标记，等加载完自行丢弃）</summary>
        public void ClosePanel(string panelId)
        {
            if (string.IsNullOrEmpty(panelId)) return;

            if (_opened.TryGetValue(panelId, out var go))
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                _opened.Remove(panelId);
                Log.Info($"[UIManager] 关闭面板 {panelId}");
            }
            else if (_loading.Contains(panelId))
            {
                // 只在确实有加载请求在飞时才打标记，避免标记残留导致下次开面板被误销毁
                _closedWhileLoading.Add(panelId);
            }
        }

        /// <summary>取已打开的面板实例（未开返回 null）</summary>
        public GameObject GetPanel(string panelId)
            => !string.IsNullOrEmpty(panelId) && _opened.TryGetValue(panelId, out var go) ? go : null;

        public void CloseAll()
        {
            foreach (var id in new List<string>(_opened.Keys))
                ClosePanel(id);
            _opened.Clear();
        }
    }
}
