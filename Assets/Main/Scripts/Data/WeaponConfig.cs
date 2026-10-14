using System;
using System.Collections.Generic;
using UnityEngine;

// ⚠️【本文件在 AOT 程序集里，但命名空间仍是 HotUpdate.Data】✗
//
// 为什么不把命名空间一起改掉 ✗：
//   1) 真机包里资产（如 Weapon_Sword.asset ✓）会引用本类 ✗
//      → Unity 解析序列化引用靠"打包时的脚本表" ✗，热更程序集不在其中 ✗
//      → 所以【类型本身必须编译进 AOT】✓（这是本文件被搬过来的唯一原因 ✓）
//   2) 但它的命名空间被**大量全限定名**引用着 ✗
//      （DamageCalculator / ElementMarks / DataService … 等约 10 个文件 ✓）
//      改名会把这些引用一次性打断 ✗ —— 收益（好看 ✓）远小于风险（漏改就编不过 ✗）
//   → 结论：**文件归属（程序集）才是关键 ✓，命名空间只影响书写 ✓**，保持不动最稳 ✓
//
// 想改的时候怎么做 ✗：先全工程搜 `CryptWalker.HotUpdate.Data.`（全限定用法 ✗），
//   确认都改成 using 之后再动命名空间 ✓。
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

        // ==================== 视觉（元气骑士式：武器独立显示、绕握把旋转）====================
        // 设计口径（美术清单 §4.2）：
        //   · 贴图就一张（单图连续旋转）；想做 8 方向离散只换图、不改代码
        //   · Pivot 必须打在【握把】—— 旋转中心就是它，所以贴图不用额外调中心点
        //   · 贴图按 PPU=16 画；长度 ≈ 1.5 格（和下面 range 对齐，"看着够得着"才等于"判定够得着"）

        [Tooltip("【UI 图标】选武器面板（UI_Win_Character/ChooseWeapon 下的 Img_Weapon*）显示的那张图。\n" +
                 "用 UI_Weapon 图集里的原始图（PPU=100）—— 不要用下面那张手持图 ✗")]
        public Sprite icon;

        [Tooltip("手持武器贴图（单张，从 UI_Weapon 图集【导出】出来的独立文件）。Pivot 要打在握把上。\n" +
                 "⚠️ 与 icon 的区别：\n" +
                 "  · icon      = 图集原图，PPU=100 → 给 UI 用（尺寸按 UI 规范画）\n" +
                 "  · weaponSprite = 导出文件，PPU=16 → 给世界里的 SpriteRenderer 用\n" +
                 "同一张图不能同时满足两边：图集的 PPU 由 UI 决定，而手持武器必须 PPU=16 才和角色像素密度一致。\n" +
                 "所以是两个字段，别混用 —— 混用的表现是「武器在 UI 里巨大」或「拿在手里小得看不见」✓")]
        public Sprite weaponSprite;

        [Tooltip("握把相对玩家原点的偏移（世界单位）。决定武器挂在身体哪个位置")]
        public Vector2 gripOffset = new Vector2(0.25f, -0.1f);

        [Tooltip("图标里【刀尖的朝向】（相对 +X，逆时针为正，单位度）：朝右=0 / 朝上=90 / 斜向右上=45 / 斜向左上=135")]
        public float spriteAngleOffset;
    }
}