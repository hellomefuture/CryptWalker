using System.Collections;
using CryptWalker.HotUpdate.Combat;
using CryptWalker.HotUpdate.Core;
using CryptWalker.HotUpdate.Data;      // EWeaponType（攻击消耗魔法按武器类型分档）
using CryptWalker.Main.Bridge;         // IHotTunable / PlayerTuning（AOT 代理注入配置）
using CryptWalker.Main.Data;           // PlayerConfig（AOT 的玩家数值配置）
using CryptWalker.HotUpdate.Entity.Enemy;
using UnityEngine;

namespace CryptWalker.HotUpdate.Entity.Player
{
    /// <summary>
    /// 玩家控制器（纯 FSM + 代码 Play 动画）。
    /// 不再使用 Animator 参数（FaceDir/IsMoving/Attack），动画由 FSM 状态直接
    /// Animator.Play("{动作}{方向}") 驱动 —— 连招/技能天然支持（加状态类即可）。
    /// 职责：读输入 + 驱动 PlayerFSM + 提供状态调用接口（PlayAction/朝向/移动）。
    /// </summary>
    public class PlayerController : MonoBehaviour, IHotTunable
    {
        [Header("角色")]
        [Tooltip("角色名（动画 clip 前缀，如 Swordsman_IdleSouth；加主角时改这个）")]
        [SerializeField] private string characterName = "Swordsman";

        [Header("移动")]
        [SerializeField] private float moveSpeed = 3f;
        [Tooltip("奔跑速度（Shift 加速，播 Run 动画）")]
        [SerializeField] private float runSpeed = 5f;

        [Header("战斗")]
        [Tooltip("攻击硬直时长（秒）。攻击期间不响应移动。")]
        [SerializeField] private float attackLockDuration = 0.5f;

        [Tooltip("攻击判定距离")]
        [SerializeField] private float attackRange = 0.9f;

        [Tooltip("攻击判定宽度")]
        [ SerializeField ] private float attackWidth = 0.6f ; // 刀锋宽度

        [Tooltip("每次攻击的伤害")]
        [SerializeField] private float attackDamage = 25f;

        [Tooltip("命中延迟（秒）：动画起手到刀锋挥出的时间")]
        [SerializeField] private float attackHitDelay = 0.2f; //延迟时间

        [Header("生命")]
        [SerializeField] private float maxHp = 100f;

        // ==================== 局内资源（HUD 三条条：红=HP / 绿=体力 / 蓝=魔法）====================
        // 数值全部集中在这一块，手感不对只改这里。
        //
        // 设计口径（与设计文档不一致的地方，以这里为准，文档待同步）：
        //   体力：奔跑 / 攻击 / 空格闪避 消耗，停手后延迟回复
        //   魔法：非近战武器（远程/法术）攻击时消耗，持续回复
        //   ⚠️ 设计文档写的是「空格闪避（含无敌帧）+ 冷却制」（有 dodge_cd_down 词条）。
        //      现在改成体力制：闪避不再走冷却，改扣体力；
        //      那条 dodge_cd_down 词条将来应改成「闪避体力消耗降低」。

        [Header("体力（绿条）")]
        [Tooltip("体力上限")]
        [SerializeField] private float maxStamina = 100f;
        [Tooltip("消耗后多久开始回复（秒）")]
        [SerializeField] private float staminaRegenDelay = 0.8f;
        [Tooltip("体力回复速度（点/秒）")]
        [SerializeField] private float staminaRegenPerSec = 20f;
        [Tooltip("奔跑时每秒消耗体力（Shift 奔跑不是免费的）")]
        [SerializeField] private float runStaminaPerSec = 8f;
        [Tooltip("体力见底后要回到这个比例才能再次奔跑（不做的话会在 0 附近一格一格抖）")]
        [SerializeField] private float staminaRunRecoverPct = 0.2f;

        [Header("魔法（蓝条）")]
        [Tooltip("魔法上限")]
        [SerializeField] private float maxMana = 100f;
        [Tooltip("魔法回复速度（点/秒）")]
        [SerializeField] private float manaRegenPerSec = 5f;
        [Tooltip("远程武器每次攻击消耗魔法")]
        [SerializeField] private float rangedManaCost = 8f;
        [Tooltip("法术武器每次攻击消耗魔法")]
        [SerializeField] private float spellManaCost = 15f;

        [Header("攻击消耗")]
        [Tooltip("每次攻击消耗体力（近战/远程/法术都一样，不按武器分档）")]
        [SerializeField] private float attackStaminaCost = 10f;

        [Header("闪避（空格，策划案 §3.1）")]
        [Tooltip("闪避瞬移距离（世界单位）")]
        [SerializeField] private float dodgeDistance = 3.5f;
        [Tooltip("闪避消耗体力")]
        [SerializeField] private float dodgeStaminaCost = 25f;
        [Tooltip("闪避无敌时长（秒）")]
        [SerializeField] private float dodgeInvincibleDuration = 0.25f;
        [Tooltip("闪避动作时长（秒），期间不接受其他操作")]
        [SerializeField] private float dodgeDuration = 0.15f;

        [Header("武器系统（策划案 §3.1 接入）")]
        [Tooltip("当前装备的武器（运行时由武器代理产出的组件填上 ✓ 不再依赖 prefab 序列化 ✗）")]
        [SerializeField] private WeaponBase currentWeapon;

        // —— 武器背包（2026-10 接入选武器面板 ✓）——
        // 从 player.prefab 上的多个 WeaponProxy 收集而来 ✓ 按 WeaponProxy.Slot 排序 ✓
        // 索引 = 选武器面板的槽位（0 → Button1 ✓）
        private WeaponBase[] _weapons;
        [Tooltip("弹道/特效发出点（玩家手部/枪口）。为空则用 transform")]
        [SerializeField] private Transform firePoint;

        [Header("独立武器显示（元气骑士式：武器绕握把转，跟随鼠标）")]
        [Tooltip("武器挂点（player.prefab 上的子节点，位置=握把）。留空则运行时自动找名为 WeaponPivot 的子节点")]
        [SerializeField] private Transform weaponPivot;
        [Tooltip("武器贴图的 SpriteRenderer（WeaponPivot 的子节点）。留空则自动找")]
        [SerializeField] private SpriteRenderer weaponRenderer;
        [Tooltip("是否用鼠标瞄准（360°）。关掉 / 取不到相机时退化为 4 向朝向")]
        [SerializeField] private bool aimWithMouse = true;

        [Header("武器切换（策划案 §3.1：0.3s 切换硬直）")]
        [SerializeField] private float switchStiffDuration = 0.3f;
        private float _switchStiffRemain;

        // 方向协议：1=South 2=Left 3=Right 4=North（与敌人/素材一致）
        private const int DIR_South = 1;
        private const int DIR_Left  = 2;
        private const int DIR_Right = 3;
        private const int DIR_North = 4;

        /// <summary>
        /// 「无武器」动画片段的后缀（如 Swordsman_WalkSouth_NoWeapon）。
        /// 美术的 lvl1/Parts/ 是分层素材，去掉 sword/sword_back 两层就能合出无剑版，
        /// 由 PlayerUnarmedAnimationBuilder 生成。
        /// </summary>
        public const string NoWeaponSuffix = "_NoWeapon";

        private SpriteRenderer spriteRenderer;

        // 方向值 → 名称后缀（拼 clip 名用）："Walk" + "South" = "WalkSouth"
        private static readonly string[] DirName = { "", "South", "Left", "Right", "North" };

        public Animator Anim { get; private set; }
        public PlayerFSM Fsm { get; private set; }
        public Vector2 MoveInput { get; private set; }
        public int FacingDir { get; private set; } = DIR_South;

        private Rigidbody2D _rb;   // 物理移动：transform.position += 不触发碰撞，必须走刚体速度

        // —— 体力 / 魔法运行时状态 ——
        private float _staminaIdleTimer;      // 距上次消耗体力过了多久（用于回复延迟）
        private bool _runLocked;              // 体力见底后锁住奔跑，恢复到 staminaRunRecoverPct 解锁
        private float _invincibleUntil;       // 无敌帧截止时间（闪避用）
        private float _raisedStaminaPct = -1f; // 上次广播出去的体力占比（避免重复广播）
        private float _raisedManaPct = -1f;
        private float _nextResWarnTime;       // 资源不足提示的节流（AGENTS.md §3.11：不能刷日志）

        // 闪避探路复用缓冲：每次闪避 new 一个会产 GC，池化在字段里
        private readonly RaycastHit2D[] _dodgeHits = new RaycastHit2D[8];
        private ContactFilter2D _dodgeFilter;

        // —— 独立武器 / 瞄准 ——
        private Vector2 _aimDir = Vector2.down;   // 当前瞄准方向（360°，单位向量）
        private float _swingTimeLeft;             // 本次挥砍剩余时间
        private float _swingDuration;             // 本次挥砍总时长
        private float _swingArc;                  // 本次挥砍弧线角度（度）
        private bool _mirrorWeapon;               // 当前是否镜像（带死区，避免竖直方向抖动）
        private bool _warnedNoCamera;
        private int _weaponDiagFrames;            // 武器诊断：只打前 3 帧（AGENTS §3.11 不刷日志）

        public float AttackLockDuration => attackLockDuration;
        public float AttackHitDelay => attackHitDelay;
        public bool IsMoving => MoveInput.sqrMagnitude > 0.01f;
        public bool IsAttacking => Fsm != null && Fsm.IsAttacking;
        public float MaxHp => maxHp;
        public float CurrentHp => Hp;
        public float Hp { get; private set; }
        public bool IsDead => Hp <= 0f;

        // —— 体力 / 魔法（HUD 绿条 / 蓝条）——
        public float MaxStamina => maxStamina;
        public float Stamina { get; private set; }
        public float StaminaPct => maxStamina > 0f ? Stamina / maxStamina : 0f;
        public float MaxMana => maxMana;
        public float Mana { get; private set; }
        public float ManaPct => maxMana > 0f ? Mana / maxMana : 0f;
        public float DodgeStaminaCost => dodgeStaminaCost;
        public float DodgeDuration => dodgeDuration;

        /// <summary>
        /// 当前瞄准方向（360°，单位向量）。攻击判定与武器朝向都用它。
        /// 不再用 4 向 FacingToVector —— 那是"角色朝向"，而瞄准是"武器朝哪"，
        /// 元气骑士式的做法是两者分开：人是 4 向动画，武器自由转。
        /// </summary>
        public Vector2 AimDir => _aimDir;

        /// <summary>闪避无敌帧（策划案 §3.1「空格闪避（含无敌帧）」）</summary>
        public bool IsInvincible => Time.time < _invincibleUntil;

        /// <summary>
        /// 奔跑：Shift + 有移动输入 + 还有体力 + 没被锁。
        /// 加 _runLocked 是因为体力归零瞬间会「跑一下停一下」高频抖动。
        /// </summary>
        public bool IsRunning
            => !_runLocked
            && Input.GetKey(KeyCode.LeftShift)
            && MoveInput.sqrMagnitude > 0.01f
            && Stamina > 0f;

        public WeaponBase CurrentWeapon => currentWeapon;
        public Transform FirePoint => firePoint != null ? firePoint : transform;
        private float CurrentSpeed => IsRunning ? runSpeed : moveSpeed;

        // 静态全局引用（敌人 Start / 其他系统查找玩家用 —— 不依赖 Instantiate 顺序）
        public static PlayerController Instance { get; private set; }

        private void Awake()
        {
            Instance = this;   // 注册全局引用（敌人通过 PlayerController.Instance.Target 获取玩家 transform）
            Anim = GetComponent<Animator>();
            Fsm = new PlayerFSM(this);
            spriteRenderer = GetComponent<SpriteRenderer>();
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null)
                Debug.LogWarning("[Player] 缺少 Rigidbody2D（Dynamic/Gravity=0/FreezeRotation-Z），移动将无法碰撞");
            Hp = maxHp;

            // 局内资源初始满值
            Stamina = maxStamina;
            Mana = maxMana;

            // 闪避探路过滤：不吃 Trigger（拾取物/门触发器不该挡住闪避），层掩码交给 Cast 自己判定
            _dodgeFilter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = false,
            };

            // 武器挂点兜底：prefab 上没拖时按名字找（装配工具会建这两个节点）
            if (weaponPivot == null) weaponPivot = transform.Find(WeaponPivotName);
            if (weaponRenderer == null && weaponPivot != null)
                weaponRenderer = weaponPivot.GetComponentInChildren<SpriteRenderer>(true);
        }

        /// <summary>
        /// 注入玩家配置（由 AOT 侧的 PlayerProxy 在运行时调用 ✗）。
        ///
        /// ⚠️【必须重新派生，不能只赋值】✗
        ///   本组件现在是运行时 AddComponent 出来的 ✗ → Unity 会**立刻**执行 Awake ✓
        ///   而 Awake 已经读过这些字段了 ✗：Hp = maxHp / Stamina = maxStamina / Mana = maxMana
        ///   只赋值不重算 ✗ → 三个条会停在 Awake 那一刻的值 ✗（将来把上限调成 !=100 就会立刻暴露 ✓）
        ///
        /// 武器（currentWeapon ✗）**故意不在这里解析** ✗：
        ///   PlayerProxy 与 WeaponProxy 挂在同一物体上 ✗ 两者 Awake 顺序不保证 ✓
        ///   → 这里 GetComponent 可能还拿不到 ✓ 所以放到 Start 里（见 ResolveWeaponIfNeeded ✓）
        /// </summary>
        public void ApplyTuning(object tuning)
        {
            if (!(tuning is PlayerTuning t) || t.config == null) return;
            PlayerConfig c = t.config;

            characterName = c.characterName;
            moveSpeed = c.moveSpeed;
            runSpeed = c.runSpeed;
            attackLockDuration = c.attackLockDuration;
            attackRange = c.attackRange;
            attackWidth = c.attackWidth;
            attackDamage = c.attackDamage;
            attackHitDelay = c.attackHitDelay;
            maxHp = c.maxHp;
            maxStamina = c.maxStamina;
            staminaRegenDelay = c.staminaRegenDelay;
            staminaRegenPerSec = c.staminaRegenPerSec;
            runStaminaPerSec = c.runStaminaPerSec;
            staminaRunRecoverPct = c.staminaRunRecoverPct;
            maxMana = c.maxMana;
            manaRegenPerSec = c.manaRegenPerSec;
            rangedManaCost = c.rangedManaCost;
            spellManaCost = c.spellManaCost;
            attackStaminaCost = c.attackStaminaCost;
            dodgeDistance = c.dodgeDistance;
            dodgeStaminaCost = c.dodgeStaminaCost;
            dodgeInvincibleDuration = c.dodgeInvincibleDuration;
            dodgeDuration = c.dodgeDuration;
            aimWithMouse = c.aimWithMouse;
            switchStiffDuration = c.switchStiffDuration;

            // —— 对象引用：它们在 prefab 上（代理组件里 ✓），不在 SO 里 ✓ ——
            // 用 if 判空：为空时保留 Awake 里那套"按名字兜底"的结果 ✓（比直接覆盖成 null 安全 ✓）
            if (t.firePoint != null) firePoint = t.firePoint;
            if (t.weaponPivot != null) weaponPivot = t.weaponPivot;
            if (t.weaponRenderer != null) weaponRenderer = t.weaponRenderer;

            // —— 重新派生 Awake 里算过的值 ✗ ——
            Hp = maxHp;
            Stamina = maxStamina;
            Mana = maxMana;

            // ⚠️ 武器**故意不在这里解析** ✗：
            //   此刻"另一个 WeaponProxy"的 Awake 很可能还没跑 ✓ → 这里收集只会拿到一部分 ✗
            //   而收集是一次性的 ✗（见 ResolveWeapons 的幂等守卫 ✓）→ 拿到一半就会固化 ✗
            //   统一放到 Start 里（那时所有 Awake 都结束了 ✓）
        }

        /// <summary>
        /// 收集本物体上**所有**武器（按 WeaponProxy.Slot 排序 ✓），装备槽位 0 ✓。
        ///
        /// 为什么顺序只认 slot ✗：武器组件是各个 WeaponProxy 在 Awake 里 AddComponent 出来的 ✓
        /// 而【多个代理的 Awake 顺序不保证】✓ → 组件注册顺序可能是乱的 ✓
        /// 所以不认组件顺序 ✗ 也不认名字 ✗ 只认你在 Inspector 上填的 slot ✓✓
        ///
        /// 时机 ✗：必须在 Start（所有 Awake 之后 ✓）；且只跑一次 ✓（幂等守卫 ✓）
        /// 只有当前武器是 enabled ✓ —— 否则每把武器的逻辑都会跑 ✗（例如远程武器的蓄力检测 ✓）
        /// </summary>
        private void ResolveWeapons()
        {
            if (_weapons != null && _weapons.Length > 0) return;   // 幂等 ✓

            var proxies = GetComponentsInChildren<WeaponProxy>(true);
            System.Array.Sort(proxies, (a, b) => a.Slot.CompareTo(b.Slot));

            var list = new System.Collections.Generic.List<WeaponBase>(proxies.Length);
            foreach (var p in proxies)
            {
                var w = p.HotComponent as WeaponBase;
                if (w != null) list.Add(w);
                else Core.Log.Warn($"[Player] 槽位 {p.Slot} 的 WeaponProxy 没产出武器组件 ✗（热更类型名填错了？）");
            }
            _weapons = list.ToArray();

            if (_weapons.Length == 0)
            {
                Core.Log.Warn("[Player] 身上没有任何武器 ✓（player.prefab 上还没有 WeaponProxy ✓）—— 攻击会走旧的 fallback 逻辑 ✓");
                currentWeapon = null;
                return;
            }

            CurrentWeaponIndex = 0;
            currentWeapon = _weapons[0];
            for (int i = 0; i < _weapons.Length; i++) _weapons[i].enabled = (i == 0);
            currentWeapon.OnEquip();

            Core.Log.Info($"[Player] 武器就绪：{_weapons.Length} 把 ✓ 当前槽位 0"
                        + $"（{(currentWeapon.Config != null ? currentWeapon.Config.name : "配置为空 ✗")}）");
        }

        /// <summary>武器把数 —— UI 用它决定显示几个槽位 ✓（没有的槽位自动失活 ✓）</summary>
        public int WeaponCount => _weapons != null ? _weapons.Length : 0;

        /// <summary>当前武器槽位 —— UI 用它高亮 ✓</summary>
        public int CurrentWeaponIndex { get; private set; }

        /// <summary>取某槽位的武器配置（UI 画图标用 ✓；没有该槽位返回 null ✓）</summary>
        public WeaponConfig GetWeaponConfig(int slot)
        {
            if (_weapons == null || slot < 0 || slot >= _weapons.Length) return null;
            return _weapons[slot] != null ? _weapons[slot].Config : null;
        }

        /// <summary>
        /// 按槽位切武器（选武器面板的点击入口 ✓）。
        /// 复用已有的 SwitchWeapon(WeaponBase) ✓ —— 它已经处理了 OnUnequip/OnEquip + 切换硬直 ✓
        /// </summary>
        /// <returns>true = 真的换了（调用方据此 Raise GameEvent.WeaponSwitched ✓）</returns>
        public bool SwitchWeaponBySlot(int slot)
        {
            if (_weapons == null || slot < 0 || slot >= _weapons.Length) return false;
            if (slot == CurrentWeaponIndex) return false;   // 点的就是当前这把 → 不算切换 ✓
            if (InSwitchStiff) return false;                // 切换硬直内忽略 ✓（防连点 ✓）
            CurrentWeaponIndex = slot;
            SwitchWeapon(_weapons[slot]);
            return true;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            // 武器解析放这里而不是只在 ApplyTuning ✗：
            //   PlayerProxy / WeaponProxy 同挂一个物体 ✗ 两者 Awake 顺序**不保证** ✓
            //   → PlayerProxy 注入时 WeaponProxy 可能还没创建武器组件 ✓
            //   而 Start 一定晚于【所有】Awake ✓ 这是可靠时机 ✓
            ResolveWeapons();
            Fsm.Start();
        }

        private void Update()
        {
            ReadInput();
            Fsm.Tick();
            TickResources(Time.deltaTime);   // 体力/魔法回复 + 奔跑消耗
            UpdateAim();                     // 鼠标 → 360° 瞄准方向
            UpdateWeaponVisual(Time.deltaTime);  // 武器贴图/握把/朝向/挥砍

            // 修复：武器切换硬直递减（之前 _switchStiffRemain 永不归零，玩家一直处于切换硬直）
            if (_switchStiffRemain > 0f) _switchStiffRemain -= Time.deltaTime;

            // 修复：长弓蓄力自动检测（之前 TickChargeRelease 没人调，蓄满/松键不发射）
            // 用 Transform 接收，每个武器自己 cast 也能更灵活
            if (currentWeapon is CryptWalker.HotUpdate.Combat.RangedWeapon rw)
            {
                Vector2 aim = FacingToVector(FacingDir);
                bool mouseUp = Input.GetMouseButtonUp(1);   // 右键松开发射（策划 §3.1）
                rw.TickChargeRelease(this, aim, mouseUp);
            }
        }
        private void LateUpdate()
        {
            // 排序：强制 SortingLayer = Character（不依赖 Inspector prefab 配置）
            // 同 Character 层内只用 Y 排序：y 大（屏幕上方）→ -y*100 小 → 排序靠前 → 画在前面
            spriteRenderer.sortingLayerName = "Character";
            spriteRenderer.sortingOrder = Mathf.RoundToInt(-transform.position.y * 100);
        }

        private void ReadInput()
        {
            MoveInput = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));
        }

        // ==================== 状态类调用的接口 ====================

        /// <summary>
        /// 按当前朝向播动画：Play("Walk") = "Swordsman_WalkSouth"。
        ///
        /// 只要该动作存在「无武器」版本（美术 Parts 分层素材合出来的
        /// Swordsman_WalkSouth_NoWeapon 等，见 PlayerUnarmedAnimationBuilder），就优先用它。
        ///
        /// 为什么是"有就用"而不是"没装备武器才用"：
        ///   武器改成独立节点显示之后，身体永远不该再出现烘在图里的剑 ——
        ///   否则会同时看到两把剑（一把跟着身体、一把跟着鼠标）。
        /// 用 Animator.HasState 判存在性，所以没做无武器版的动作（目前 Hurt/Death）
        /// 会自动退回原版，以后补了也不用改这里。
        /// </summary>
        public void PlayAction(string action)
        {
            if (Anim == null) return;

            string name = $"{characterName}_{action}{DirName[FacingDir]}";

            string unarmed = name + NoWeaponSuffix;
            if (Anim.HasState(0, Animator.StringToHash(unarmed))) name = unarmed;

            Anim.Play(name);
        }

        /// <summary>更新朝向（只在有输入时改，保持上次朝向）</summary>
        public void UpdateFacing()
        {
            if (MoveInput.sqrMagnitude <= 0.01f) return;
            FacingDir = MoveToDirIndex(MoveInput);
        }

        /// <summary>
        /// 移动（物理版）：设刚体速度，由物理引擎处理与墙的碰撞。
        /// ⚠️ 不能用 transform.position += —— 那是瞬移，Collider 只会"重叠"不会被挡住。
        ///
        /// 修复：之前 `MoveInput.normalized` 在 MoveInput=Vector2.zero 时返回 NaN，
        /// 导致 _rb.velocity = NaN → Rigidbody2D 卡住，玩家原地不动。
        /// 现在防御：sqrMagnitude < 0.01f 时直接清零速度，避免 normalize(NaN)。
        /// </summary>
        public void Move()
        {
            if (_rb == null) return;
            if (MoveInput.sqrMagnitude < 0.01f)
            {
                _rb.velocity = Vector2.zero;
                return;
            }
            Vector2 dir = MoveInput.normalized;
            _rb.velocity = dir * CurrentSpeed;
        }

        /// <summary>停止移动（清速度）：Idle/攻击/受击/死亡 进入时调用，否则速度残留会滑行</summary>
        public void StopMove()
        {
            if (_rb != null) _rb.velocity = Vector2.zero;
        }

        /// <summary>
        /// 攻击判定（策划案 §3.1）。
        /// 优先调 CurrentWeapon.DoAttack（策划案 §3.1 武器系统）；
        /// 没武器时 fallback 到 Inspector 字段（兼容现有 player.prefab 不挂武器的情况）。
        /// 修复：fallback 调 EnemyBase.TakeDamage(float) 没查 IsDead——之前可能重复伤害。
        /// 现在 fallback 查 IsDead。
        /// </summary>
        public void PerformAttack()
        {
            // 360° 鼠标瞄准：判定方向就是武器指的方向，不再是 4 向 FacingToVector。
            // 身体动画仍然是 4 向（素材只有 4 向），但"打哪"由鼠标决定 —— 元气骑士式。
            Vector2 dir = AimDir;

            // 攻击要花体力和魔法（策划：攻击消耗体力；非近战武器额外消耗魔法）。
            // 没武器时按近战算 —— 会走下面那段 fallback 旧逻辑。
            EWeaponType weaponType = currentWeapon != null ? currentWeapon.WeaponType : EWeaponType.Melee;
            float manaCost = weaponType == EWeaponType.Ranged ? rangedManaCost
                           : weaponType == EWeaponType.Spell ? spellManaCost
                           : 0f;

            // 原子判定：两样都够才扣。分开扣会出现"体力扣了、魔法不够、弹道没发出去"的哑火。
            if (!TryConsume(attackStaminaCost, manaCost))
            {
                WarnResourceShort(manaCost > 0f ? "体力/魔法" : "体力");
                return;
            }

            if (currentWeapon != null)
            {
                // 策划案 §3.1：武器自己负责伤害计算 + 元素附伤
                currentWeapon.DoAttack(this, dir);
                return;
            }

            // ---- 无武器 fallback：旧逻辑（策划案 §3.1 兼容层）----
            Vector2 center = (Vector2)transform.position + dir * (attackRange * 0.5f);
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            var hits = Physics2D.OverlapBoxAll(center, new Vector2(attackRange, attackWidth), angle);
            foreach (var hit in hits)
            {
                if (!hit.TryGetComponent<EnemyBase>( out var enemy)) continue;
                if (enemy.IsDead) continue;  // 修复：fallback 也查 IsDead
                enemy.TakeDamage(attackDamage);
            }
        }

        /// <summary>特殊技能（右键，策划案 §3.1）—— 方向同样走 360° 瞄准</summary>
        public void PerformSpecialSkill()
        {
            if (currentWeapon != null)
                currentWeapon.DoSpecialSkill(this, AimDir);
        }

        /// <summary>
        /// 切换武器（策划案 §3.1 双武器，Q/E）。
        /// 当前实现：单武器接口，预留双武器槽位（Inventory[2]）。
        /// </summary>
        public void SwitchWeapon(WeaponBase newWeapon)
        {
            if (newWeapon == currentWeapon) return;
            currentWeapon?.OnUnequip();

            // 旧武器必须关掉 ✗ 新武器要开 ✓：
            //   每把武器组件都是独立的 MonoBehaviour ✓ 留着 enabled 的话它的逻辑会一起跑 ✗
            //   （比如远程武器的蓄力检测 ✓ 会同时算在两把武器上 ✓）
            if (currentWeapon != null) currentWeapon.enabled = false;
            currentWeapon = newWeapon;
            if (currentWeapon != null) currentWeapon.enabled = true;

            currentWeapon?.OnEquip();
            _switchStiffRemain = switchStiffDuration;
            Core.Log.Info($"[Player] 切换武器 → {(newWeapon != null ? newWeapon.Config?.weaponType.ToString() : "null")}");
        }

        public bool InSwitchStiff => _switchStiffRemain > 0f;

        /// <summary>
        /// 空格闪避（策划案 §3.1「空格闪避（含无敌帧）」）。
        /// 由 PlayerFSM.DodgeState 在确认体力足够后调用
        /// （AGENTS.md §3.6-31：技能/连击扩展必须走 FSM 状态链，不要散在 Update 里）。
        ///
        /// 实现要点：
        ///   1) 先开无敌帧 —— 否则瞬移路上吃到伤害就白闪了
        ///   2) 位移用 Rigidbody2D.position，不用 transform.position：
        ///      transform 直接改是"纯瞬移"，会穿墙、也不触发物理
        ///   3) 用 Rigidbody2D.Cast 探出前后能走多远，被地形挡住就停在墙前；
        ///      但【忽略敌人】—— 闪避是防御动作，被贴身怪卡住就完全失去意义
        /// </summary>
        public void Dodge()
        {
            if (IsDead) return;

            _invincibleUntil = Time.time + dodgeInvincibleDuration;
            DodgeMove();
        }

        /// <summary>闪避位移（物理感知的瞬移）</summary>
        private void DodgeMove()
        {
            Vector2 dir = FacingToVector(FacingDir);
            float dist = dodgeDistance;

            if (_rb != null)
            {
                int n = _rb.Cast(dir, _dodgeFilter, _dodgeHits, dist);
                for (int i = 0; i < n; i++)
                {
                    var col = _dodgeHits[i].collider;
                    if (col == null) continue;
                    if (col.GetComponentInParent<EnemyBase>() != null) continue;   // 敌人不挡闪避
                    dist = Mathf.Min(dist, Mathf.Max(0f, _dodgeHits[i].distance - 0.05f));
                }

                _rb.velocity = Vector2.zero;        // 清残留速度，否则瞬移后还会往前滑一段
                _rb.position += dir * dist;
            }
            else
            {
                // 降级：prefab 漏挂刚体时直接改 transform（会穿墙，但至少能动）
                transform.position += (Vector3)(dir * dist);
            }
        }

        // ==================== 独立武器 / 360° 瞄准 ====================

        /// <summary>武器挂点节点名（player.prefab 上的子节点，装配工具按这个名字建）</summary>
        private const string WeaponPivotName = "WeaponPivot";

        /// <summary>
        /// 镜像判定的死区（|aim.x| 小于它就认为是"正上/正下"，沿用上次判定）。
        /// 太小仍会抖，太大会让左右切换变迟钝；0.06 ≈ 3.4° 的容差。
        /// </summary>
        private const float MirrorDeadzone = 0.06f;

        /// <summary>开始一次武器挥砍（近战用）。由 PlayerFSM.AttackState 进入时调用。</summary>
        public void BeginWeaponSwing(float arcDeg, float duration)
        {
            _swingArc = arcDeg;
            _swingDuration = Mathf.Max(0.05f, duration);
            _swingTimeLeft = _swingDuration;
        }

        /// <summary>
        /// 鼠标 → 世界方向（360°）。
        /// 取不到相机（或关掉了 aimWithMouse）就退化为 4 向朝向 —— 保证任何情况下都打得出去。
        /// 注：2D 是正交相机，ScreenToWorldPoint 的 z 不影响 xy 映射，所以直接传鼠标坐标即可。
        /// </summary>
        private void UpdateAim()
        {
            if (!aimWithMouse) { _aimDir = FacingToVector(FacingDir); return; }

            Camera cam = Camera.main;
            if (cam == null)
            {
                if (!_warnedNoCamera)
                {
                    _warnedNoCamera = true;
                    Core.Log.Warn("[Player] 找不到 Camera.main（相机没打 MainCamera 标签？）→ 武器瞄准退化为 4 向朝向");
                }
                _aimDir = FacingToVector(FacingDir);
                return;
            }

            Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
            Vector2 origin = weaponPivot != null ? (Vector2)weaponPivot.position : (Vector2)transform.position;
            Vector2 d = (Vector2)mouseWorld - origin;
            _aimDir = d.sqrMagnitude > 0.0001f ? d.normalized : FacingToVector(FacingDir);
        }

        /// <summary>
        /// 每帧摆武器：握把位置 / 贴图 / 朝向 / 挥砍 / 左侧翻转。
        ///
        /// 翻转为什么要「转 180° + Y 缩放 -1」而不是只转 180°：
        ///   只转 180° 会把贴图上下颠倒（剑刃朝下）。镜像是"转 180° 再沿自身轴翻回来"，
        ///   所以两者必须成对使用。
        /// </summary>
        private void UpdateWeaponVisual(float dt)
        {
            // 一次性诊断（只打前 3 帧，之后闭嘴）：
            // 一次就能分清「挂点没拿到 / 贴图没挂上 / 相机拿不到 / 都正常但角度不对」。
            if (_weaponDiagFrames < 3)
            {
                _weaponDiagFrames++;
                Camera diagCam = Camera.main;
                Core.Log.Info($"[Weapon] 诊断{_weaponDiagFrames}: "
                            + $"pivot={(weaponPivot != null ? weaponPivot.name : "【null】")} "
                            + $"renderer={(weaponRenderer != null ? weaponRenderer.name : "【null】")} "
                            + $"sprite={(currentWeapon != null && currentWeapon.WeaponSprite != null ? currentWeapon.WeaponSprite.name : "【null】")} "
                            + $"camera={(diagCam != null ? diagCam.name : "【null】")} "
                            + $"aim={_aimDir} angle={(weaponPivot != null ? weaponPivot.localEulerAngles.z : -1f):F1}");
            }

            if (weaponPivot == null) return;

            WeaponConfig cfg = currentWeapon != null ? currentWeapon.Config : null;

            // 1) 握把相对身体的偏移（每把武器可以不同）
            weaponPivot.localPosition = cfg != null ? (Vector3)cfg.gripOffset : Vector3.zero;

            // 2) 贴图（换武器时自动换图）
            Sprite want = currentWeapon != null ? currentWeapon.WeaponSprite : null;
            if (weaponRenderer != null)
            {
                if (weaponRenderer.sprite != want) weaponRenderer.sprite = want;
                weaponRenderer.enabled = want != null;

                // 与身体同层，但永远画在身体之上（身体的 order 在 LateUpdate 里按 y 算）
                weaponRenderer.sortingLayerName = "Character";
                weaponRenderer.sortingOrder = Mathf.RoundToInt(-transform.position.y * 100) + 1;
            }

            // 3) 挥砍弧线（从 -arc/2 扫到 +arc/2，相对瞄准方向）
            float swing = 0f;
            if (_swingTimeLeft > 0f)
            {
                _swingTimeLeft -= dt;
                float t = 1f - Mathf.Clamp01(_swingTimeLeft / Mathf.Max(0.0001f, _swingDuration));
                swing = Mathf.Lerp(-_swingArc * 0.5f, _swingArc * 0.5f, t);
            }

            // 4) 朝向：直接算「让刀尖指向瞄准方向」的那个角 φ。
            //
            //    贴图里的刀尖并不朝右（比如斜向右上 45°），所以要减掉这个偏角。
            //    ⚠️ 关键在于：Y 缩放的镜像会把偏角的符号也翻过来 ——
            //       未镜像：刀尖世界角 = φ + spriteOff   →  φ = aim − spriteOff
            //       镜像后：刀尖世界角 = φ − spriteOff   →  φ = aim + spriteOff
            //    所以两个分支的符号是相反的。之前两个分支都用 `aim + spriteOff` 再叠 180，
            //    对"朝右"的贴图正好等价，但对斜着画的贴图，左右两半里有一半会偏 90°。
            //
            // 镜像判定带死区：鼠标在正上/正下时 aim.x 会在 0 附近来回过零，
            // 不加死区会每帧在"正常/镜像"间翻（实测挂点角在 88° 与 274° 之间跳）。
            // 死区内沿用上次判定。（按上面的正确公式，两个分支算出的刀尖世界角是同一个，
            // 所以即使偶尔切换也不会看到角度跳，只会看到贴图镜像本身。）
            if (Mathf.Abs(_aimDir.x) > MirrorDeadzone) _mirrorWeapon = _aimDir.x < 0f;

            float aimDeg = Mathf.Atan2(_aimDir.y, _aimDir.x) * Mathf.Rad2Deg;
            float spriteOff = cfg != null ? cfg.spriteAngleOffset : 0f;
            float phi = (_mirrorWeapon ? aimDeg + spriteOff : aimDeg - spriteOff) + swing;

            weaponPivot.localRotation = Quaternion.Euler(0f, 0f, phi);
            weaponPivot.localScale = new Vector3(1f, _mirrorWeapon ? -1f : 1f, 1f);
        }

        // ==================== 体力 / 魔法 ====================

        /// <summary>
        /// 原子消耗：两样都够才扣，任一不够则整体失败、一点都不扣。
        /// 攻击同时要体力和魔法，所以不能拆成两个独立方法（拆了会出现"扣了体力没打出弹道"）。
        /// </summary>
        public bool TryConsume(float staminaCost, float manaCost = 0f)
        {
            if (Stamina < staminaCost || Mana < manaCost) return false;

            if (staminaCost > 0f)
            {
                Stamina = Mathf.Max(0f, Stamina - staminaCost);
                _staminaIdleTimer = 0f;     // 重新计时，回复要等 staminaRegenDelay
                RaiseStaminaChanged();
            }
            if (manaCost > 0f)
            {
                Mana = Mathf.Max(0f, Mana - manaCost);
                RaiseManaChanged();
            }
            return true;
        }

        /// <summary>体力是否够（只查不扣，供 UI/预判用）</summary>
        public bool HasStamina(float cost) => Stamina >= cost;

        /// <summary>资源不足提示（节流 1 秒 —— AGENTS.md §3.11：禁止高频日志）</summary>
        public void WarnResourceShort(string need)
        {
            if (Time.time < _nextResWarnTime) return;
            _nextResWarnTime = Time.time + 1f;
            Core.Log.Warn($"[Player] {need}不足，操作被拒绝");
        }

        /// <summary>体力/魔法回复 + 奔跑消耗（每帧调用，内部不打日志）</summary>
        private void TickResources(float dt)
        {
            // 奔跑消耗体力：耗尽后锁住奔跑，回到 staminaRunRecoverPct 才解锁
            if (IsRunning)
            {
                Stamina = Mathf.Max(0f, Stamina - runStaminaPerSec * dt);
                _staminaIdleTimer = 0f;
                if (Stamina <= 0f && !_runLocked)
                {
                    _runLocked = true;
                    Core.Log.Info($"[Player] 体力耗尽，奔跑锁定（回到 {staminaRunRecoverPct:P0} 解锁）");
                }
                RaiseStaminaChanged();
            }
            else if (_runLocked && StaminaPct >= staminaRunRecoverPct)
            {
                _runLocked = false;
            }

            _staminaIdleTimer += dt;

            if (_staminaIdleTimer >= staminaRegenDelay && Stamina < maxStamina)
            {
                Stamina = Mathf.Min(maxStamina, Stamina + staminaRegenPerSec * dt);
                RaiseStaminaChanged();
            }

            if (Mana < maxMana)
            {
                Mana = Mathf.Min(maxMana, Mana + manaRegenPerSec * dt);
                RaiseManaChanged();
            }
        }

        // 广播占位：变化小于 0.1% 就不发 —— 否则每帧给 HUD 刷一堆没意义的事件
        private void RaiseStaminaChanged()
        {
            if (Mathf.Abs(StaminaPct - _raisedStaminaPct) < 0.001f) return;
            _raisedStaminaPct = StaminaPct;
            EventBus.Raise(GameEvent.StaminaChanged, StaminaPct);
        }

        private void RaiseManaChanged()
        {
            if (Mathf.Abs(ManaPct - _raisedManaPct) < 0.001f) return;
            _raisedManaPct = ManaPct;
            EventBus.Raise(GameEvent.ManaChanged, ManaPct);
        }

        /// <summary>朝向值 → 单位方向向量（1=下 2=左 3=右 4=上）</summary>
        private static Vector2 FacingToVector(int facing)
        {
            switch (facing)
            {
                case DIR_Left:  return Vector2.left;
                case DIR_Right: return Vector2.right;
                case DIR_North: return Vector2.up;
                default:        return Vector2.down;
            }
        }

        /// <summary>8 向输入 → 4 向朝向（y 优先，与敌人一致）</summary>
        private int MoveToDirIndex(Vector2 dir)
        {
            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x))
                return dir.y > 0 ? DIR_North : DIR_South;
            return dir.x > 0 ? DIR_Right : DIR_Left;
        }

        // ==================== 受击 ====================

        /// <summary>
        /// 玩家受击：扣血 + 闪红 + 进 Hit 硬直（播 Hurt）；死亡进 Death。
        /// 无敌帧（闪避）期间直接免疫。
        /// </summary>
        public void TakeDamage(float damage)
        {
            if (IsDead) return;
            if (IsInvincible) return;   // 闪避无敌帧（策划案 §3.1「空格闪避（含无敌帧）」）

            Hp -= damage;
            StartCoroutine(FlashRed());

            // HUD 红条：广播当前 HP 占比（payload: float 0~1）
            EventBus.Raise(GameEvent.PlayerDamaged, MaxHp > 0f ? Hp / MaxHp : 0f);

            // 修复：受击回调（策划 §4.2 狂战护符：hp_below_50pct 时 atk+30%）
            // 由 RelicManager 内部判断 hp 比例并累计攻击加成
            var relicMgr = GetComponent<CryptWalker.HotUpdate.BuffSystem.RelicManager>();
            if (relicMgr != null)
            {
                float hpPct = MaxHp > 0f ? Hp / MaxHp : 1f;
                relicMgr.OnTakeDamage(hpPct);
            }

            if (IsDead)
            {
                EventBus.Raise(GameEvent.PlayerDied);   // 本局结束（结算/停 HUD 用）
                Fsm.ChangeToDeath();
            }
            else Fsm.ChangeToHit();
        }

        /// <summary>
        /// 玩家回血（策划 §4.3 消耗道具 + 策划 §4.2 回血护符击杀回血）。
        /// 不超过 MaxHp。
        /// </summary>
        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Hp = Mathf.Min(MaxHp, Hp + amount);
            EventBus.Raise(GameEvent.PlayerDamaged, MaxHp > 0f ? Hp / MaxHp : 0f);   // HUD 血条同步
            Core.Log.Info($"[Player] 回血 +{amount:F1} → HP {Hp:F1}/{MaxHp:F1}");
        }

        /// <summary>
        /// 玩家击杀敌人后调用（策划 §4.2 回血护符）。
        /// 由 EnemyBase.Death 时通知玩家。
        /// </summary>
        public void OnEnemyKilled()
        {
            var relicMgr = GetComponent<CryptWalker.HotUpdate.BuffSystem.RelicManager>();
            relicMgr?.OnKill();
        }

        /// <summary>
        /// 玩家暴击时调用（策划 §4.2 刺客匕首）。
        /// 由 RangedWeapon / MeleeWeapon 命中暴击后调用。
        /// </summary>
        public void OnCritTriggered()
        {
            var relicMgr = GetComponent<CryptWalker.HotUpdate.BuffSystem.RelicManager>();
            relicMgr?.OnCrit();
        }

        /// <summary>受击闪红 0.1s：白 → 红 → 白</summary>
        private IEnumerator FlashRed()
        {
            if (spriteRenderer == null) yield break;
            spriteRenderer.color = Color.red;
            yield return new WaitForSeconds(0.1f);
            spriteRenderer.color = Color.white;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 调试可视化：Scene 视图画出攻击判定矩形（仅编辑器，不进发布包）。
        /// 平时黄色，攻击瞬间变绿 —— 调 attackRange/attackWidth 时实时看形状。
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;   // FacingDir 是运行时值，非播放态画了是错的

            Vector2 dir = FacingToVector(FacingDir);
            Vector2 center = (Vector2)transform.position + dir * (attackRange * 0.5f);
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            Gizmos.color = IsAttacking ? Color.green : Color.yellow;
            DrawRect(center, attackRange, attackWidth, angle);
        }

        private static void DrawRect(Vector2 center, float w, float h, float angleDeg)
        {
            // 把绘图坐标系绕 center 旋转 angleDeg，之后画的框跟着转
            Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angleDeg), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(w, h, 0f));
            Gizmos.matrix = Matrix4x4.identity;   // 必须复位，否则污染其他绘制
        }
#endif
    }
}
