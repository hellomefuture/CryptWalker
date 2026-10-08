using System.Collections.Generic;
using CryptWalker.HotUpdate.Data;
// 别名：让 ModifierPipeline 内部能直接写 EEffectType，不用每次写全限定名
using EEffectType = CryptWalker.HotUpdate.Data.EBuffEffectType;

namespace CryptWalker.HotUpdate.Combat
{
    /// <summary>
    /// 数值修饰聚合管道（策划案 §3.3 "全部加成走统一 Modifier 聚合管道"）。
    ///
    /// 目的：把玩家身上的 Buff/词条/遗物/元素附伤统一聚合到一次计算，
    /// 避免在 Weapon/Buff/Relic 里各自算一次导致漏算/多算。
    ///
    /// 用法：
    ///   var m = ModifierPipeline.Build(player);
    ///   float dmg = DamageCalculator.Calc(weapon.BaseAttack, weapon.Multiplier, target.Defense, m, weapon.ElementType, isCrit);
    ///
    /// 字段设计：
    ///   attackPct        攻击力加成 %（如 +15% → 0.15，Buff + 词条 + 条件遗物 之和）
    ///   flatAtk          固定附加攻击力
    ///   pierce           穿透（降低敌防减伤效果）
    ///   critRate         暴击率（0~1）
    ///   critMult         暴击倍率（基础 1.5）
    ///   eleDmgByType     各元素附伤（key=EElementType, value=附伤数值）
    ///   difficultyFactor 难度修正
    ///
    /// 修复：之前 Build() 只返回默认值，没真正读 BuffManager/RelicManager——这次接上
    /// </summary>
    public class ModifierSnapshot
    {
        public float AttackPct;
        public float FlatAtk;
        public float Pierce;
        public float CritRate;
        public float CritMult = 1.5f;
        public Dictionary<EElementType, float> EleDmgByType = new Dictionary<EElementType, float>();
        public float DifficultyFactor = 1f;

        public void Reset()
        {
            AttackPct = 0f;
            FlatAtk = 0f;
            Pierce = 0f;
            CritRate = 0f;
            CritMult = 1.5f;
            DifficultyFactor = 1f;
            EleDmgByType.Clear();
        }
    }

    /// <summary>
    /// Modifier 聚合管道（Builder 模式）。
    /// Build() 读玩家身上的所有 Buff/Relic/词条 → 填进 ModifierSnapshot。
    /// </summary>
    public static class ModifierPipeline
    {
        /// <summary>
        /// 从攻击方身上聚合所有 Modifier。
        ///
        /// 修复：之前返回默认值——现在读：
        ///   - BuffManager（攻击力加成/暴击/攻速/最大生命等）
        ///   - RelicManager（条件型加成如 hp_below_50pct → atk+30%）
        /// </summary>
        public static ModifierSnapshot Build(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker)
        {
            var snap = new ModifierSnapshot();

            // 玩家 prefab 没挂 BuffManager / RelicManager 时不抛异常（开发期 fallback）
            var buffMgr = attacker != null ? attacker.GetComponent<CryptWalker.HotUpdate.BuffSystem.BuffManager>() : null;
            var relicMgr = attacker != null ? attacker.GetComponent<CryptWalker.HotUpdate.BuffSystem.RelicManager>() : null;

            if (attacker != null && !attacker.IsDead)
            {
                float hpPct = attacker.MaxHp > 0f ? attacker.CurrentHp / attacker.MaxHp : 1f;

                // Buff 聚合
                if (buffMgr != null)
                {
                    snap.AttackPct += buffMgr.AggregateValue(EEffectType.AttackPct);
                    snap.FlatAtk += buffMgr.AggregateValue(EEffectType.FlatAttack);
                    snap.Pierce += buffMgr.AggregateValue(EEffectType.PierceFlat);
                    snap.CritRate += buffMgr.AggregateValue(EEffectType.CritRate);
                    snap.CritMult = 1.5f + buffMgr.AggregateValue(EEffectType.CritDamage);
                }

                // 遗物聚合（策划 §4.2 条件型）
                if (relicMgr != null)
                {
                    snap.AttackPct += relicMgr.ReadConditionalAttackBonus(hpPct);
                }
            }

            return snap;
        }
    }
}