using System.Collections.Generic;
using UnityEngine;

namespace CryptWalker.Main.Data
{
    /// <summary>
    /// UI 资源集合（AGENTS.md §3.4-20 数据驱动 UI + §5.3 UIManager 面板）。
    ///
    /// 数据流：
    ///   场景里的 UIConfigHolder (AOT MonoBehaviour) SerializeField 持有本 SO
    ///   → Start 时通过 AppBridge.SetUIConfig(config) 注入桥接层
    ///   → HotUpdate 端 UIManager.Init() 通过 IAppBridge.GetUIConfig() 拿
    ///   → UIManager.OpenPanelAsync(panelId) 按 panelId 查 UIPanelConfig → 异步开面板
    ///
    /// 设计变化（对比旧版单个 mainMenuPrefab 字段）：
    ///   旧：每个面板一个字段，加面板 = 加字段 + 改 UIConfig + 改 UIManager（三处）
    ///   新：一个 List<UIPanelConfig>，加面板 = 只加一个 UIPanelConfig 资产并拖进列表（一处）
    ///   → 这是"开闭原则"落地：对扩展开放（加配置），对修改封闭（不改 UIManager）
    /// </summary>
    [CreateAssetMenu(menuName = "CryptWalker/UIConfig", fileName = "UIConfig")]
    public class UIConfig : ScriptableObject
    {
        [Tooltip("所有 UI 面板配置（每个面板一个 UIPanelConfig 资产）")]
        public List<UIPanelConfig> panels = new List<UIPanelConfig>();
    }
}
