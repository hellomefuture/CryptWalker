using CryptWalker.HotUpdate.Data;   // GameConfigBase（同 EnemyConfig ✓）
using UnityEngine;

namespace CryptWalker.Main.Data
{
    /// <summary>
    /// 玩家数值配置（**AOT 侧** ScriptableObject）。理由同 EnemyConfig ✗：
    /// 被 prefab 上的代理组件引用 ✗ → 必须是 AOT 类型 ✓ 否则真机包会 missing script ✓
    ///
    /// ===== 字段名与 PlayerController 的序列化字段【一一对应】=====
    /// 迁移工具按字段名逐个搬值 ✓ 改名会导致漏搬 ✗
    ///
    /// ===== 有 4 个字段**故意留在了代理组件上** ✗（不在本 SO）=====
    ///   firePoint / weaponPivot / weaponRenderer   → 都是 **prefab 内部的对象引用** ✗
    ///   currentWeapon                              → 热更组件引用 ✗
    /// 原因：SO 里放"prefab 内部对象的引用"不可靠 ✗（SO 是资产 ✓ 而引用指向某个 prefab 的实例内节点 ✗）
    /// → 它们由 PlayerProxy 直接序列化在 prefab 上 ✓（那才是它们该待的地方 ✓），
    ///   运行时通过 PlayerTuning 一并注入 ✓
    /// </summary>
    [CreateAssetMenu(fileName = "Player_xxx", menuName = "CryptWalker/Data/PlayerConfig")]
    public class PlayerConfig : GameConfigBase
    {
        [Header("角色")]
        [Tooltip("角色名（动画 clip 前缀，如 Swordsman_IdleSouth）")]
        public string characterName = "Swordsman";

        [Header("移动")]
        public float moveSpeed = 3f;
        [Tooltip("奔跑速度（Shift 加速，播 Run 动画）")]
        public float runSpeed = 5f;

        [Header("战斗")]
        [Tooltip("攻击硬直时长（秒）。攻击期间不响应移动。")]
        public float attackLockDuration = 0.5f;
        [Tooltip("攻击判定距离")]
        public float attackRange = 0.9f;
        [Tooltip("攻击判定宽度")]
        public float attackWidth = 0.6f;
        [Tooltip("每次攻击的伤害")]
        public float attackDamage = 25f;
        [Tooltip("命中延迟（秒）：动画起手到刀锋挥出的时间")]
        public float attackHitDelay = 0.2f;

        [Header("生命")]
        public float maxHp = 100f;

        [Header("体力（绿条）")]
        public float maxStamina = 100f;
        [Tooltip("消耗后多久开始回复（秒）")]
        public float staminaRegenDelay = 0.8f;
        [Tooltip("体力回复速度（点/秒）")]
        public float staminaRegenPerSec = 20f;
        [Tooltip("奔跑时每秒消耗体力")]
        public float runStaminaPerSec = 8f;
        [Tooltip("体力见底后要回到这个比例才能再次奔跑（不做的话会在 0 附近一格一格抖）")]
        public float staminaRunRecoverPct = 0.2f;

        [Header("魔法（蓝条）")]
        public float maxMana = 100f;
        [Tooltip("魔法回复速度（点/秒）")]
        public float manaRegenPerSec = 5f;
        [Tooltip("远程武器每次攻击消耗魔法")]
        public float rangedManaCost = 8f;
        [Tooltip("法术武器每次攻击消耗魔法")]
        public float spellManaCost = 15f;

        [Header("攻击消耗")]
        [Tooltip("每次攻击消耗体力（近战/远程/法术都一样）")]
        public float attackStaminaCost = 10f;

        [Header("闪避（空格，策划案 §3.1）")]
        [Tooltip("闪避瞬移距离（世界单位）")]
        public float dodgeDistance = 3.5f;
        [Tooltip("闪避消耗体力")]
        public float dodgeStaminaCost = 25f;
        [Tooltip("闪避无敌时长（秒）")]
        public float dodgeInvincibleDuration = 0.25f;
        [Tooltip("闪避动作时长（秒），期间不接受其他操作")]
        public float dodgeDuration = 0.15f;

        [Header("武器显示（元气骑士式：武器绕握把转，跟随鼠标）")]
        [Tooltip("是否用鼠标瞄准（360°）。关掉 / 取不到相机时退化为 4 向朝向")]
        public bool aimWithMouse = true;

        [Header("武器切换（策划案 §3.1：0.3s 切换硬直）")]
        public float switchStiffDuration = 0.3f;
    }
}
