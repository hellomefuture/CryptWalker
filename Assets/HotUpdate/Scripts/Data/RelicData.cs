using UnityEngine;

namespace CryptWalker.HotUpdate.Data
{
    /// <summary>遗物稀有度（策划 §4.2：用枚举避免字符串拼写错误）</summary>
    public enum ERelicRarity { Common, Rare, Epic, Legendary }

    /// <summary>遗物效果类型（策划案 §4.2）</summary>
    public enum ERelicEffectType
    {
        ConditionalStat,    // 条件型（策划案狂战护符：hp_below_50pct → atk +30%）
        ReactionDamageBoost, // 反应伤害加成（策划案元素核心）
        Multiplier,         // 乘法器（策划案多重箭矢：弹道+1）
        LifeStealOnKill,    // 击杀回血（策划案回血护符）
        CritChain,          // 暴击触发连锁（策划案刺客匕首）
    }

    /// <summary>
    /// 遗物数据（策划案 §4.2 全局被动）。
    /// 首版 5 件：狂战护符 / 元素核心 / 刺客匕首 / 多重箭矢 / 回血护符。
    ///
    /// 修复：
    ///   - rarity 改 enum（之前 string 容易拼写错误）
    ///   - 加 configID（来自 GameConfigBase），策划 §10 数据驱动要求全局唯一 ID
    ///   - 加 iconPath 给 UI 显示
    /// </summary>
    [CreateAssetMenu(menuName = "CryptWalker/Data/Relic", fileName = "Relic_xxx")]
    public class RelicData : GameConfigBase
    {
        [Header("基础")]
        public ERelicRarity rarity = ERelicRarity.Common;
        [Tooltip("显示名（策划 §4.2 relicName，RelicManager.cs 用它做日志/UI 文本）")]
        public string relicName;

        [Header("效果")]
        public ERelicEffectType effectType;
        public string triggerCondition;     // 条件型触发条件（如 "hp_below_50pct"）
        public string statTarget;            // 影响哪个属性（"attack"/"maxHp" 等）
        public float statValue;              // 数值（百分比 0~1 或固定值）

        [Header("叠加规则")]
        public bool stackable = false;       // unique / stackable

        [Header("显示")]
        [TextArea(1, 3)] public string description;
        public Sprite icon;
    }
}