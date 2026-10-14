using CryptWalker.HotUpdate.Data;   // GameConfigBase —— 它虽已搬进 AOT，但命名空间仍保留为 HotUpdate.Data（见 WeaponConfig.cs 顶部注释 ✓）
using UnityEngine;

namespace CryptWalker.Main.Data
{
    /// <summary>
    /// 敌人数值配置（**AOT 侧** ScriptableObject）。
    ///
    /// ===== 为什么它必须在 AOT 侧 =====
    /// 它会被 **prefab 上的代理组件**（EnemyProxy ✓ AOT）引用 ✗。
    /// 而 Unity 解析序列化引用靠的是**打包时那份脚本表** ✗ —— 热更程序集不在里面 ✗
    /// → 任何"被资产引用"的类型都必须是 AOT 的 ✓
    ///   （这也是真机包出现 39 条 `missing script` 的根因 ✗）
    ///
    /// ===== 字段名必须与 EnemyBase 里的序列化字段【同名】=====
    /// 迁移工具（CryptWalker/迁移/把 prefab 上的热更组件搬到 AOT 代理 ✗）
    /// 按【字段名】逐个把 prefab 上的旧值搬进本 SO ✓
    /// → 改字段名会导致迁移时漏搬 ✗（漏搬 = 静默变回代码默认值 ✗ 很难发现 ✓）
    ///
    /// 另有 1 个字段**故意不在这里** ✗：`_target`（玩家 transform ✗）
    /// —— 它是**运行时注入**的（DungeonGenerator 刷怪时设 ✓），不该被序列化到配置里 ✓
    /// </summary>
    [CreateAssetMenu(fileName = "Enemy_xxx", menuName = "CryptWalker/Data/EnemyConfig")]
    public class EnemyConfig : GameConfigBase
    {
        [Header("配置（对应策划案 5.2 敌人属性）")]
        [Tooltip("必须 = 动画 clip 前缀（Orc1 / Vampires1 ...）")]
        public string enemyName = "Orc1";

        public float maxHp = 100f;
        public float moveSpeed = 2f;
        [Tooltip("发现玩家距离（30 = 跨房间都能识别，测试期方便）")]
        public float sightRange = 30f;
        [Tooltip("近战攻击范围")]
        public float attackRange = 1.5f;
        [Tooltip("攻击间隔")]
        public float attackInterval = 1.2f;
        [Tooltip("每次攻击伤害")]
        public float attackDamage = 20f;
        [Tooltip("命中延迟（动画挥击帧）")]
        public float attackHitDelay = 0.3f;
        [Tooltip("防御（策划案 §3.3 减伤公式）")]
        public float defense = 10f;
        [Tooltip("Boss 标记（策划案 §3.2 剧毒爆燃衰减）")]
        public bool isBoss = false;

        [Header("掉落（策划案 §二 局内循环：击杀获取金币）")]
        [Tooltip("击杀掉落金币数。Boss 会自动按 BOSS_COIN_MULTIPLIER 放大")]
        public int coinDrop = 2;

        [Header("击退")]
        [Tooltip("击退速度的衰减率下限（单位/秒²）。调大 → 击退更干脆")]
        public float knockbackDecay = 5f;

        [Header("A* 寻路（策划案 5.3 敌人 AI）")]
        [Tooltip("重算路径的最小间隔（秒）—— 节流，避免每帧都跑一次 A*")]
        public float repathInterval = 0.35f;
        [Tooltip("目标移动超过这个距离（格）就立刻重算路径，不等节流")]
        public float repathOnTargetMove = 2f;
        [Tooltip("到达某个 waypoint 的判定距离（格）")]
        public float waypointReachDist = 0.3f;
        [Tooltip("卡住判定的观察窗口（秒）：窗口内实际走的路程不足期望的 40% 才算卡住")]
        public float stuckCheckWindow = 0.5f;

        [Tooltip("【排查用】勾选后每秒打印一次寻路状态 + 可走格 ASCII 图。定位完问题请关掉。")]
        public bool debugPath = false;
    }
}
