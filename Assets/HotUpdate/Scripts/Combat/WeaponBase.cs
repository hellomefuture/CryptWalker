using CryptWalker.HotUpdate.Data;
using UnityEngine;

namespace CryptWalker.HotUpdate.Combat
{
    /// <summary>
    /// 武器基类（策划案 §3.1）。
    /// 挂在玩家身上的 Component，负责具体武器的攻击/技能逻辑。
    /// 子类：MeleeWeapon / RangedWeapon / SpellWeapon。
    ///
    /// 数据来源：WeaponConfig（ScriptableObject），策划案 §3.1 数据结构一一对应。
    /// 攻击流程：
    ///   PlayerController 触发 → WeaponBase.DoAttack(dir) → 武器类型决定效果
    ///   命中判定 → DamageCalculator.Calc → 应用伤害 + 元素印记
    /// </summary>
    public abstract class WeaponBase : MonoBehaviour
    {
        [SerializeField] protected WeaponConfig config;     // 数据源（策划案 §3.1 JSON 字段）
        [SerializeField] protected Transform firePoint;      // 弹道/特效发出点（玩家手部/枪口）

        public WeaponConfig Config => config;
        public float BaseAttack => config != null ? config.baseAttack : 0f;
        public float WeaponMultiplier => GetWeaponMultiplier();    // 武器倍率（策划案 §3.3）
        public EWeaponType WeaponType => config != null ? config.weaponType : EWeaponType.Melee;
        public EElementType ElementType => config != null ? config.elementType : EElementType.None;
        public float AttackRange => config != null ? config.range : 1.5f;
        public float AttackSpeed => config != null ? config.attackSpeed : 1f;
        public float SpecialSkillCD => config != null ? config.specialSkillCD : 6f;

        /// <summary>
        /// 策划案 §3.3 武器倍率表：
        ///   长剑 1.0 / 巨斧 1.8 / 匕首 0.6 / 长弓 1.3(蓄满 2.0) / 连发铳 0.4×5 / 法杖 1.2
        /// 当前只有 3 把武器（长剑/长弓/法杖），其他预留接口。
        /// </summary>
        protected virtual float GetWeaponMultiplier()
        {
            switch (WeaponType)
            {
                case EWeaponType.Melee: return 1.0f;     // 长剑
                case EWeaponType.Ranged: return 1.3f;    // 长弓（蓄满时 2.0，由 RangedWeapon 动态调整）
                case EWeaponType.Spell: return 1.2f;     // 法杖
                default: return 1.0f;
            }
        }

        /// <summary>
        /// 普通攻击（按策划案：左键）。
        /// 子类实现具体效果：
        ///   - 近战：判定扇形/矩形范围 → 应用伤害 + 元素印记
        ///   - 远程：发射弹道
        ///   - 法术：释放元素弹或法术
        /// </summary>
        /// <param name="attacker">攻击方（玩家），用于聚合 Modifier</param>
        /// <param name="aimDir">攻击方向（玩家面对的方向，世界坐标单位向量）</param>
        public abstract void DoAttack(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker, Vector2 aimDir);

        /// <summary>特殊技能（右键，策划案 §3.1 specialSkillType）</summary>
        public abstract void DoSpecialSkill(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker, Vector2 aimDir);

        /// <summary>武器切换到本武器时调用（重置冷却等）</summary>
        public virtual void OnEquip() { }

        /// <summary>武器切走时调用</summary>
        public virtual void OnUnequip() { }
    }
}