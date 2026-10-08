using UnityEngine;

namespace CryptWalker.HotUpdate.Data
{
    /// <summary>Buff 稀有度（策划案 §4.1）</summary>
    public enum EBuffRarity { White, Blue, Purple, Gold }

    /// <summary>Buff 效果类型（策划案 §4.1 分类）</summary>
    public enum EBuffEffectType
    {
        AttackPct,         // 攻击力加成 %（如 atk_up）
        AttackSpeedPct,    // 攻速加成 %
        CritRate,          // 暴击率
        CritDamage,        // 暴伤
        MaxHpFlat,         // 最大生命值（固定值）
        MoveSpeedPct,      // 移速加成 %
        DodgeCDPct,        // 闪避冷却缩短 %
        PierceFlat,        // 穿透（固定值）
        FlatAttack,        // 固定附加攻击
        MaxHpPct,          // 最大生命值 %
    }

    /// <summary>
    /// Buff 数据（策划案 §4.1 Buff 池）。
    /// SO 形式，按稀有度区分白/蓝/紫/金。
    /// </summary>
    [CreateAssetMenu(menuName = "CryptWalker/Data/Buff", fileName = "Buff_xxx")]
    public class BuffData : GameConfigBase
    {
        [Header("稀有度（影响权重）")]
        public EBuffRarity rarity = EBuffRarity.White;

        [Header("效果")]
        public EBuffEffectType effectType;
        public float value;                 // 数值（百分比 0~1 或固定值，按 effectType 解读）

        [Header("叠加规则（策划案 §4.1：同 Buff 2 次×0.7, 3 次×0.5）")]
        public int maxStack = 3;

        [Header("显示")]
        public string displayName;
        [TextArea] public string description;
    }
}