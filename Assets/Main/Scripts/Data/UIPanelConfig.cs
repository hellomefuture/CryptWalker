using UnityEngine;

namespace CryptWalker.Main.Data
{
    /// <summary>
    /// UI 层级（枚举值 = Canvas SortingOrder，AGENTS.md §3.4 硬约束 18：0/100/200/300）。
    ///
    /// 为什么放 AOT 端：UIPanelConfig 是"配置类型"，两侧都能引用（与 LevelConfig / UIConfig 同一约定），
    /// 不破坏"HotUpdate 严禁直接调用 AOT API"的硬约束。
    /// </summary>
    public enum UILayer
    {
        HUD     = 0,     // 战斗 HUD（血条/经验条/技能栏/伤害数字）
        Normal  = 100,   // 普通面板（主菜单/背包/装备）
        Popup   = 200,   // 弹窗（设置/升级三选一/结算）
        Loading = 300,   // 全屏加载
    }

    /// <summary>
    /// 单个 UI 面板配置（AGENTS.md §3.4-20「数据驱动 UI 使用 ScriptableObject」+ §5.3 UIManager）。
    ///
    /// 两种资源来源（C2 双字段方案，兼容开发期与发布期）：
    ///   1) address 非空 → UIManager 走 YooAsset 异步加载（进资源热更链路，正式打包用）
    ///   2) address 为空 → 用 prefab 直引用（开发期零配置，改 prefab 立即生效）
    ///
    /// 使用步骤：
    ///   1. 右键 Create → CryptWalker → UI → PanelConfig 建一个面板配置
    ///   2. 填 panelId（代码里用它开面板，如 "MainMenu"）
    ///   3. 选 layer，拖 prefab（开发期）；后期配 YooAsset 地址时填 address
    ///   4. 把 UIPanelConfig 拖进 UIConfig.panels 列表
    /// </summary>
    [CreateAssetMenu(menuName = "CryptWalker/UI/PanelConfig", fileName = "UIPanelConfig")]
    public class UIPanelConfig : ScriptableObject
    {
        [Tooltip("面板唯一 ID（代码里用它开面板），如 MainMenu / HUD / LevelUp / Settle")]
        public string panelId;

        [Tooltip("挂载层级：HUD(0) / Normal(100) / Popup(200) / Loading(300)")]
        public UILayer layer = UILayer.Normal;

        [Tooltip("YooAsset 地址（发布期用）。填了就走异步加载 + 资源热更链路；留空则用下方 prefab")]
        public string address;

        [Tooltip("面板 prefab（开发期兜底）。address 为空时用它；两者都为空则开面板失败")]
        public GameObject prefab;
    }
}
