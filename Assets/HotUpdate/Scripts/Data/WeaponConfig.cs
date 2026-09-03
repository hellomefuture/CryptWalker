using System;
using System.Collections.Generic;
using UnityEngine;

namespace CryptWalker.HotUpdate.Data
{
    // —— 共享枚举与可序列化条目（映射策划案细化三/四/五）——

    public enum EWeaponType { Melee, Ranged, Spell }
    public enum EElementType { None, Fire, Ice, Thunder, Poison }

    /// <summary>武器词条条目（映射策划案的 trait）</summary>
    [Serializable]
    public class TraitEntry
    {
        public string traitID;
        public float value;
    }

    /// <summary>
    /// 武器配置（ScriptableObject 版本）。
    /// 与策划案细化三的 JSON 结构一一对应，作为 SO 数据驱动的示例。
    /// </summary>
    [CreateAssetMenu(fileName = "Weapon_xxx", menuName = "CryptWalker/Data/WeaponConfig")]
    public class WeaponConfig : GameConfigBase
    {
        public EWeaponType weaponType;
        public EElementType elementType;

        [Header("数值")]
        public float baseAttack;
        public float attackSpeed;
        public float range;
        public int elementDamage;

        [Header("特殊技能")]
        public float specialSkillCD;
        public string specialSkillType;

        [Header("词条(最多4条)")]
        public List<TraitEntry> traits = new List<TraitEntry>();
    }
}