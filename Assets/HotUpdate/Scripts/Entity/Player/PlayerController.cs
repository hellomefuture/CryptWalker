using System.Collections;
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

        public float AttackLockDuration => attackLockDuration;
        public float AttackHitDelay => attackHitDelay;
        public bool IsMoving => MoveInput.sqrMagnitude > 0.01f;
        public bool IsAttacking => Fsm != null && Fsm.IsAttacking;
        public float Hp { get; private set; }
        public bool IsDead => Hp <= 0f;
        public bool IsRunning => Input.GetKey(KeyCode.LeftShift) && MoveInput.sqrMagnitude > 0.01f;
        private float CurrentSpeed => IsRunning ? runSpeed : moveSpeed;

        private void Awake()
        {
            Anim = GetComponent<Animator>();
            Fsm = new PlayerFSM(this);
            spriteRenderer = GetComponent<SpriteRenderer>();
            Hp = maxHp;
        }

        private void Start() => Fsm.Start();

        private void Update()
        {
            ReadInput();
            Fsm.Tick();
        }
        private void LateUpdate()
        {
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

        /// <summary>8 向移动（斜向也走，normalized 保证速度一致；Shift 奔跑）</summary>
        public void Move()
        {
            Vector3 delta = new Vector3(MoveInput.x, MoveInput.y, 0f).normalized
                            * (CurrentSpeed * Time.deltaTime);
            transform.position += delta;
        }

        /// <summary>
        /// 攻击判定（策划案 3.1 近战）。
        /// 在面前偏移处放一个判定圆，命中带 EnemyBase 的物体造成伤害；
        /// 朝向过滤保证只打面前（Dot > 0.3 ≈ 面前 72° 扇形）。
        /// </summary>
        public void PerformAttack()
        {
            Vector2 dir = FacingToVector(FacingDir);
            Vector2 center = (Vector2)transform.position + dir * (attackRange * 0.5f);
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            var hits = Physics2D.OverlapBoxAll(center, new Vector2(attackRange, attackWidth), angle);
            foreach (var hit in hits)
            {
                if (!hit.TryGetComponent<EnemyBase>( out var enemy)) continue ; // ⑧ 只要敌人 if (enemy.IsDead) continue ;
                if (enemy.IsDead) continue ;
                enemy.TakeDamage(attackDamage);
            }
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
            if (IsDead) Fsm.ChangeToDeath();
            else Fsm.ChangeToHit();
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
