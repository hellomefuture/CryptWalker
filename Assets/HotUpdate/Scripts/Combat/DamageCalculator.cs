using UnityEngine;

namespace CryptWalker.HotUpdate.Combat
{
    /// <summary>
    /// 统一伤害计算器（策划案 §3.3 + §0 修正）。
    ///
    /// 公式（策划案 §3.3）：
    ///   最终伤害 = (基础攻击力 × 武器倍率 × (1 + 攻击力加成%) + 固定附加伤害 + 元素附伤)
    ///            × 暴击倍率(若触发，150%起，上限300%)
    ///            × 伤害系数(1 - 减伤比例)，减伤比例 = 敌防 / (敌防 + 玩家攻击×穿透)
    ///              （策划案原文的 "敌防/(敌防+攻击×穿透)" 是【减伤比例】，
    ///                不能直接当伤害系数乘上去，否则敌防越高伤害越高 —— 见 Calc 内 §2 修正说明）
    ///            × 难度修正系数
    ///            × 随机浮动 0.95~1.05
    ///
    /// ⚠️ 元素附伤说明（策划 §0 修正）：
    ///   - 元素附伤走主公式（武器 baseAttack 的 elementDamage 平A附伤）
    ///   - 元素反应伤害独立结算（不进主公式，由 ElementReactionTrigger 处理）
    ///   - 因此本方法只算"武器附带的元素附伤"，不算反应伤害
    ///
    /// 设计：
    ///   - 静态方法，无副作用，便于在任意时机调用
    ///   - 不直接读 Buff/词条，由 ModifierSnapshot 提供（解耦）
    ///   - 暴击上限 300%（策划案 §3.3 约束）
    /// </summary>
    public static class DamageCalculator
    {
        /// <summary>
        /// 穿透保底值：Pierce=0 时若直接代入公式，分母只剩敌防、减伤比例恒为 100% → 伤害归零。
        /// 策划案未定义该边界，这里取 0.05 作为"毫无穿透"时的最小有效穿透，保证伤害为正。
        /// </summary>
        private const float MinPierce = 0.05f;

        /// <summary>
        /// 计算一次攻击的最终伤害。
        /// </summary>
        /// <param name="baseAttack">武器基础攻击</param>
        /// <param name="weaponMultiplier">武器倍率（长剑 1.0 / 长弓 1.3 / 法杖 1.2 等）</param>
        /// <param name="targetDefense">敌人防御（章节区间：第一章 10~20 等）</param>
        /// <param name="modifier">聚合修饰（攻击力加成/穿透/暴击/难度系数/元素附伤）</param>
        /// <param name="weaponElementType">武器元素类型（策划 §3.3 元素附伤走主公式）。None 表示无元素</param>
        /// <param name="isCrit">外部传入是否触发暴击（策划 §3.3 暴击由上层控制，这里只是计算倍率）</param>
        /// <returns>最终伤害（一定 ≥ 0）</returns>
        public static float Calc(float baseAttack, float weaponMultiplier, float targetDefense,
                                ModifierSnapshot modifier, Data.EElementType weaponElementType = Data.EElementType.None,
                                bool isCrit = false)
        {
            if (modifier == null) modifier = new ModifierSnapshot();

            // 1. 聚合攻击 = 基础 × 武器倍率 × (1 + 攻击力加成%) + 固定附加 + 元素附伤
            float atk = baseAttack * weaponMultiplier * (1f + modifier.AttackPct) + modifier.FlatAtk;

            // 元素附伤：策划 §3.3 元素附伤走主公式（仅 weaponElementType 对应的元素值）
            if (weaponElementType != Data.EElementType.None
                && modifier.EleDmgByType != null
                && modifier.EleDmgByType.TryGetValue(weaponElementType, out float eleFlat) && eleFlat > 0f)
            {
                atk += eleFlat;
            }

            // 2. 防御减伤（策划案 §3.3 公式）
            //    ⚠️ 修正说明（原实现有两处方向性错误）：
            //      a) 策划案给的 "敌防 / (敌防 + 攻击×穿透)" 是【减伤比例】，不是【伤害系数】。
            //         原来直接把它乘到伤害上 → 敌防越高伤害越高、穿透越高伤害越低，完全反了。
            //         正确关系：伤害系数 = 1 - 减伤比例 = 攻击×穿透 / (敌防 + 攻击×穿透)
            //      b) 原 Mathf.Min(defFactor, 0.5f) 把伤害系数压到 ≤0.5，注释却写"减伤不超过 50%"。
            //         "减伤不超过 50%" 等价于 "伤害系数 ≥ 50%"，应改用 Mathf.Max。
            //    不变量：穿透↑ → 伤害↑；敌防↑ → 伤害↓；最终减伤恒 ≤ 50%。
            float pierce = Mathf.Max(0f, modifier.Pierce);
            // 穿透保底：策划案未定义 pierce=0 的行为，若取 0 会让伤害归零（不合理），
            // 这里给一个最小穿透值，保证"完全没有穿透"时仍能打出伤害
            pierce = Mathf.Max(pierce, MinPierce);
            float mitigation = targetDefense + atk * pierce > 0f
                ? targetDefense / (targetDefense + atk * pierce)   // 减伤比例 ∈ [0,1)
                : 0f;
            float defFactor = 1f - mitigation;                     // 伤害系数
            defFactor = Mathf.Max(defFactor, 0.5f);                // 减伤不超过 50%

            float dmg = atk * defFactor;

            // 3. 暴击（150% 起，上限 300%）
            if (isCrit)
            {
                float critMult = Mathf.Min(modifier.CritMult, 3.0f);  // 上限 300%
                dmg *= critMult;
            }

            // 4. 难度修正
            dmg *= modifier.DifficultyFactor;

            // 5. 随机浮动 ±5%
            float jitter = Random.Range(0.95f, 1.05f);
            dmg *= jitter;

            return Mathf.Max(0f, dmg);
        }
    }
}