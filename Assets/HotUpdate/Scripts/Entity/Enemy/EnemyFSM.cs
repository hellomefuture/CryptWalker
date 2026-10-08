using CryptWalker.HotUpdate.Entity.Common;
using UnityEngine;

namespace CryptWalker.HotUpdate.Entity.Enemy
{
    /// <summary>
    /// 敌人状态机（对应策划案 5.3 FSM 表）。
    /// 状态：Idle → Patrol → Chase → Attack，Hit/Death 可随时打断（优先级 Death > Hit > Attack > Chase > Patrol > Idle）。
    /// 实现：复用 Entity.Common.Fsm&lt;EnemyBase&gt;，6 个状态类放本文件（缩略版）。
    /// 每个状态 OnEnter 播对应动画（Animator.Play，需要 controller 里存在同名状态）。
    /// </summary>
    public class EnemyFSM
    {
        private readonly Fsm<EnemyBase> _fsm = new Fsm<EnemyBase>();
        private readonly EnemyBase _owner;

        // 状态实例（复用，避免每帧 new）
        public readonly IdleState    Idle;
        public readonly PatrolState  Patrol;
        public readonly ChaseState   Chase;
        public readonly AttackState  Attack;
        public readonly HitState     Hit;
        public readonly DeathState   Death;

        public EnemyFSM(EnemyBase owner)
        {
            _owner = owner;
            Idle   = new IdleState(owner);
            Patrol = new PatrolState(owner);
            Chase  = new ChaseState(owner);
            Attack = new AttackState(owner);
            Hit    = new HitState(owner);
            Death  = new DeathState(owner);
        }

        public void Start()           => _fsm.ChangeState(Idle);
        public void Tick()            => _fsm.Tick();
        public void ChangeToChase()   => _fsm.ChangeState(Chase);
        public void ChangeToPatrol()  => _fsm.ChangeState(Patrol);
        public void ChangeToAttack()  => _fsm.ChangeState(Attack);
        public void ChangeToHit()     => _fsm.ChangeState(Hit);    // 外部（受击）可随时打断
        public void ChangeToDeath()   => _fsm.ChangeState(Death);  // 最高优先级
    }

    // ==================== 6 个状态 ====================

    /// <summary>待机：播 Idle，2s 后巡逻；发现玩家立即追击</summary>
    public class IdleState : FsmState<EnemyBase>
    {
        private float _idleUntil;
        public IdleState(EnemyBase owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.StopMove();      // 待机清速度
            Owner.PlayAction("Idle");
            _idleUntil = Time.time + 2f;
        }
        public override void OnUpdate()
        {
            if (Owner.TargetInSight()) { Owner.Fsm.ChangeToChase(); return; }
            if (Time.time >= _idleUntil) Owner.Fsm.ChangeToPatrol();
        }
    }

    /// <summary>巡逻：播 Walk，绕当前位置半径 3 格随机走动；发现玩家追击</summary>
    public class PatrolState : FsmState<EnemyBase>
    {
        private Vector3 _target;
        private int _lastFacing;
        public PatrolState(EnemyBase owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.PlayAction("Walk");
            _lastFacing = Owner.Facing;
            PickNewPoint();
        }
        public override void OnUpdate()
        {
            if (Owner.TargetInSight()) { Owner.Fsm.ChangeToChase(); return; }

            Owner.LookAtPoint(_target);
            if(Owner.Facing != _lastFacing)
            {
                _lastFacing = Owner.Facing;
                Owner.PlayAction("Walk");
            }

            // 移动走刚体速度（碰墙会被挡）；巡逻半速
            if ((_target - Owner.transform.position).sqrMagnitude > 0.25f)
                Owner.MoveTowardPoint(_target, 0.5f);
            else
                PickNewPoint();
        }
        /// <summary>
        /// 选一个巡逻点：修复前用 Random.insideUnitCircle，随机点经常落在墙里，
        /// 敌人会一路顶着墙走到 2 秒超时才换点。现在从可走格里随机挑。
        /// </summary>
        private void PickNewPoint()
        {
            if (!Owner.TryGetRandomPointNear(Owner.transform.position, 3f, out var p))
                p = Owner.transform.position;   // 周围找不到可走格 → 原地待着（比撞墙好）
            _target = p;
        }
    }

    /// <summary>追击：播 Run，面朝目标并靠近；进攻击范围转 Attack，丢目标转 Patrol</summary>
    public class ChaseState : FsmState<EnemyBase>
    {
        private int _lastFacing;
        public ChaseState(EnemyBase owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.LookAtTarget();
            _lastFacing = Owner.Facing;
            Owner.PlayAction("Run");
        }
        public override void OnUpdate()
        {
            if (!Owner.TargetInSight()) { Owner.Fsm.ChangeToPatrol(); return; }

            Owner.LookAtTarget();
            if (Owner.Facing != _lastFacing)      // 转向时重播动画（换方向 clip）
            {
                _lastFacing = Owner.Facing;
                Owner.PlayAction("Run");
            }
            if (Owner.TargetInAttackRange()) { Owner.Fsm.ChangeToAttack(); return; }
            // 修复：改为 A* 寻路跟随（原先 MoveTowardTarget 是纯直线，撞墙就被卡住）
            // EnemyBase.MoveAlongPath 内部三级策略：有视线直冲 → 有墙走 A* 路径 → 无网格退化直线
            Owner.MoveAlongPath();
        }
    }

    /// <summary>攻击：播 Attack（不移动），挥击帧命中，攻击间隔后回追击/巡逻</summary>
    public class AttackState : FsmState<EnemyBase>
    {
        private float _attackEnd;
        private float _hitTime = -1;   // 命中时机；-1 = 已命中
        private int _lastFacing;
        public AttackState(EnemyBase owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.StopMove();      // 攻击定身（清速度）
            Owner.LookAtTarget();
            _lastFacing = Owner.Facing;
            Owner.PlayAction("Attack");
            _hitTime = Time.time + Owner.AttackHitDelay;   // 延迟到动画挥击帧
            _attackEnd = Time.time + Owner.AttackInterval;
        }
        public override void OnUpdate()
        {
            Owner.LookAtTarget();
            if (Owner.Facing != _lastFacing)
            {
                _lastFacing = Owner.Facing;
                Owner.PlayAction("Attack");
            }
            // 到挥击帧且还没命中 → 造成伤害（只一次）
            if (_hitTime >= 0 && Time.time >= _hitTime)
            {
                _hitTime = -1;

                // 修复：策划 §3.2 蒸汽爆炸致盲（攻击 50% Miss）
                if (Owner.IsBlinded && Random.value < 0.5f)
                {
                    Core.Log.Info($"[EnemyFSM] {Owner.name} 致盲 miss");
                }
                // 修复：策划 §3.2 过载爆发硬直（打断正在进行的攻击 → 走 Hit）
                else if (Owner.IsStunned)
                {
                    Owner.Fsm.ChangeToHit();
                }
                else
                {
                    Owner.PerformAttack();
                }
            }
            if (Time.time >= _attackEnd)
            {
                if (Owner.TargetInSight()) Owner.Fsm.ChangeToChase();
                else Owner.Fsm.ChangeToPatrol();
            }
        }
    }

    /// <summary>受击硬直：播 Hurt，0.2s 后回追击/巡逻</summary>
    public class HitState : FsmState<EnemyBase>
    {
        private float _hitEnd;
        public HitState(EnemyBase owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.StopMove();      // 受击硬直（清速度）
            Owner.PlayAction("Hurt");
            _hitEnd = Time.time + 0.3f;
        }
        public override void OnUpdate()
        {
            if (Time.time >= _hitEnd)
            {
                if (Owner.TargetInSight()) Owner.Fsm.ChangeToChase();
                else Owner.Fsm.ChangeToPatrol();
            }
        }
    }

    /// <summary>死亡：播 Death，2s 后销毁（占位，后续接掉落/对象池回收）</summary>
    public class DeathState : FsmState<EnemyBase>
    {
        private float _destroyAt;
        public DeathState(EnemyBase owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.StopMove();      // 死亡（清速度）
            Owner.PlayAction("Death");
            _destroyAt = Time.time + 2f;
        }
        public override void OnUpdate()
        {
            if (Time.time >= _destroyAt)
                Object.Destroy(Owner.gameObject);
        }
    }
}
