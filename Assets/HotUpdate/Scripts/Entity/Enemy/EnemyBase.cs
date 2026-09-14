using CryptWalker.HotUpdate.Entity.Player;
using UnityEngine;

namespace CryptWalker.HotUpdate.Entity.Enemy
{
    /// <summary>
    /// 敌人基类（对应策划案 5.2 / 5.3）。
    /// 职责：
    ///   1. 持有数据：生命/移速/视野/攻击范围/冷却（Inspector 可调）
    ///   2. 驱动 EnemyFSM（Awake 建 FSM，Update 每帧 Tick）
    ///   3. 提供动画播放、朝向计算、目标判定、受伤接口（给 FSM 状态调用）
    ///
    /// 方向协议与玩家一致：Facing 1=South 2=Left 3=Right 4=North
    /// 动画命名：{enemyName}_{动作}{方向}，如 Orc1_AttackSouth
    /// </summary>
    public class EnemyBase : MonoBehaviour
    {
        [Header("配置（对应策划案 5.2 敌人属性）")]
        [SerializeField] private string enemyName = "Orc1";   // 必须 = clip 前缀（Orc1/Vampires1...）
        [SerializeField] private float maxHp = 100f;
        [SerializeField] private float moveSpeed = 2f;
        [SerializeField] private float sightRange = 6f;       // 发现玩家距离（策划案：视野 6 格）
        [SerializeField] private float attackRange = 1.5f;    // 近战攻击范围
        [SerializeField] private float attackInterval = 1.2f; // 攻击间隔
        [SerializeField] private float attackDamage = 20f;    // 每次攻击伤害
        [SerializeField] private float attackHitDelay = 0.3f; // 命中延迟（动画挥击帧）

        [Header("目标（测试时在 Inspector 拖玩家，正式由索敌系统注入）")]
        [SerializeField] private Transform _target;

        private SpriteRenderer spriteRenderer;

        private static readonly string[] DirName = { "", "South", "Left", "Right", "North" };

        public Animator Anim { get; private set; }
        public EnemyFSM Fsm { get; private set; }
        public Transform Target { get => _target; set => _target = value; }
        public float Hp { get; private set; }
        public int Facing { get; private set; } = 1; // 1=South 2=Left 3=Right 4=North
        public float MoveSpeed => moveSpeed;
        public float AttackInterval => attackInterval;
        public float AttackHitDelay => attackHitDelay;
        public float AttackDamage => attackDamage;
        public bool IsDead => Hp <= 0f;

        private void Awake()
        {
            Anim = GetComponent<Animator>();
            Hp = maxHp;
            Fsm = new EnemyFSM(this);
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void Start() => Fsm.Start();
        private void Update() => Fsm.Tick();

        private void LateUpdate()
        {
            spriteRenderer.sortingOrder = Mathf.RoundToInt(-transform.position.y * 100);
        }

        // ==================== FSM 状态调用的接口 ====================

        /// <summary>播动画：拼 {enemyName}_{action}{方向} 名字，交给 Animator.Play</summary>
        public void PlayAction(string action)
        {
            if (Anim == null) return;
            if (Anim.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"[EnemyBase] {name} 的 Animator 没有挂 controller，无法播 {action}。请在 Inspector 把 {enemyName}.controller 拖到 Animator 的 Controller 槽");
                return;
            }
            Anim.Play($"{enemyName}_{action}{DirName[Facing]}");
        }

        /// <summary>面朝点（4 向离散，y 优先，与玩家协议一致）</summary>
        public void LookAtPoint(Vector2 point)
        {
            Vector2 d = point - (Vector2)transform.position;
            if(Mathf.Abs(d.y) > Mathf.Abs(d.x))
                Facing = d.y > 0 ? 4 : 1;   // North / South
            else
                Facing = d.x > 0 ? 3 : 2;   //Right / Left
        }
        /// <summary>面朝目标（4 向离散，y 优先，与玩家协议一致）</summary>
        public void LookAtTarget()
        {
            if (Target == null) return;
                LookAtPoint(Target.position);
        }

        public bool TargetInSight() => Target != null && DistanceToTarget() <= sightRange;
        public bool TargetInAttackRange() => Target != null && DistanceToTarget() <= attackRange;
        public float DistanceToTarget() => Target == null ? float.MaxValue : Vector2.Distance(transform.position, Target.position);

        /// <summary>朝目标移动（Chase 用）</summary>
        public void MoveTowardTarget()
        {
            if (Target == null) return;
            Vector3 dir = (Target.position - transform.position).normalized;
            transform.position += dir * (moveSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 敌人攻击命中：攻击动画挥击帧时调用。
        /// 判定 = 距离比较（近战）——目标在攻击范围内就扣血（后续可换 OverlapBox/弹道）。
        /// </summary>
        public void PerformAttack()
        {
            if (Target == null) return;
            if (DistanceToTarget() > attackRange * 1.2f) return;  // 略放宽，容忍动画前摇位移

            var player = Target.GetComponent<PlayerController>();
            if (player != null) player.TakeDamage(attackDamage);
        }

        /// <summary>受击：死亡优先，否则进 Hit 硬直（转换优先级：Death > Hit）</summary>
        public void TakeDamage(float damage)
        {
            if (IsDead) return;
            Hp -= damage;
            if (IsDead) Fsm.ChangeToDeath();
            else Fsm.ChangeToHit();
        }
    }
}
