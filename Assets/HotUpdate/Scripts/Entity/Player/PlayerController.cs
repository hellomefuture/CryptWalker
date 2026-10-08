using System.Collections;
using CryptWalker.HotUpdate.Combat;
using CryptWalker.HotUpdate.Core;
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
    public class PlayerController : MonoBehaviour
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

        [Header("武器系统（策划案 §3.1 接入）")]
        [Tooltip("当前装备的武器（策划案 §3.1 双武器切换，本阶段先支持单武器；策划 §3.1 第二档加 Inventory[2]）")]
        [SerializeField] private WeaponBase currentWeapon;
        [Tooltip("弹道/特效发出点（玩家手部/枪口）。为空则用 transform")]
        [SerializeField] private Transform firePoint;

        [Header("武器切换（策划案 §3.1：0.3s 切换硬直）")]
        [SerializeField] private float switchStiffDuration = 0.3f;
        private float _switchStiffRemain;

        // 方向协议：1=South 2=Left 3=Right 4=North（与敌人/素材一致）
        private const int DIR_South = 1;
        private const int DIR_Left  = 2;
        private const int DIR_Right = 3;
        private const int DIR_North = 4;

        private SpriteRenderer spriteRenderer;

        // 方向值 → 名称后缀（拼 clip 名用）："Walk" + "South" = "WalkSouth"
        private static readonly string[] DirName = { "", "South", "Left", "Right", "North" };

        public Animator Anim { get; private set; }
        public PlayerFSM Fsm { get; private set; }
        public Vector2 MoveInput { get; private set; }
        public int FacingDir { get; private set; } = DIR_South;

        private Rigidbody2D _rb;   // 物理移动：transform.position += 不触发碰撞，必须走刚体速度

        public float AttackLockDuration => attackLockDuration;
        public float AttackHitDelay => attackHitDelay;
        public bool IsMoving => MoveInput.sqrMagnitude > 0.01f;
        public bool IsAttacking => Fsm != null && Fsm.IsAttacking;
        public float MaxHp => maxHp;
        public float CurrentHp => Hp;
        public float Hp { get; private set; }
        public bool IsDead => Hp <= 0f;
        public bool IsRunning => Input.GetKey(KeyCode.LeftShift) && MoveInput.sqrMagnitude > 0.01f;
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
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start() => Fsm.Start();

        private void Update()
        {
            ReadInput();
            Fsm.Tick();

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

        /// <summary>按当前朝向播动画：Play("Walk") = "Swordsman_WalkSouth"</summary>
        public void PlayAction(string action)
        {
            if (Anim == null) return;
            Anim.Play($"{characterName}_{action}{DirName[FacingDir]}");
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
            Vector2 dir = FacingToVector(FacingDir);

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

        /// <summary>特殊技能（右键，策划案 §3.1）</summary>
        public void PerformSpecialSkill()
        {
            if (currentWeapon != null)
                currentWeapon.DoSpecialSkill(this, FacingToVector(FacingDir));
        }

        /// <summary>
        /// 切换武器（策划案 §3.1 双武器，Q/E）。
        /// 当前实现：单武器接口，预留双武器槽位（Inventory[2]）。
        /// </summary>
        public void SwitchWeapon(WeaponBase newWeapon)
        {
            if (newWeapon == currentWeapon) return;
            currentWeapon?.OnUnequip();
            currentWeapon = newWeapon;
            currentWeapon?.OnEquip();
            _switchStiffRemain = switchStiffDuration;
            Core.Log.Info($"[Player] 切换武器 → {(newWeapon != null ? newWeapon.Config?.weaponType.ToString() : "null")}");
        }

        public bool InSwitchStiff => _switchStiffRemain > 0f;

        /// <summary>闪避（策划案 §3.1 空格）—— 占位接口，留待后续实现</summary>
        public void Dodge()
        {
            Core.Log.Info("[Player] Dodge 占位（策划案后续实现：无敌帧 + 短距位移）");
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

        /// <summary>玩家受击：扣血 + 闪红 + 进 Hit 硬直（播 Hurt）；死亡进 Death</summary>
        public void TakeDamage(float damage)
        {
            if (IsDead) return;
            Hp -= damage;
            StartCoroutine(FlashRed());

            // 修复：受击回调（策划 §4.2 狂战护符：hp_below_50pct 时 atk+30%）
            // 由 RelicManager 内部判断 hp 比例并累计攻击加成
            var relicMgr = GetComponent<CryptWalker.HotUpdate.BuffSystem.RelicManager>();
            if (relicMgr != null)
            {
                float hpPct = MaxHp > 0f ? Hp / MaxHp : 1f;
                relicMgr.OnTakeDamage(hpPct);
            }

            if (IsDead) Fsm.ChangeToDeath();
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
