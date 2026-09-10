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
        public PatrolState(EnemyBase owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.PlayAction("Walk");
            PickNewPoint();
        }
        public override void OnUpdate()
        {
            if (Owner.TargetInSight()) { Owner.Fsm.ChangeToChase(); return; }

            Vector3 to = _target - Owner.transform.position;
            if (to.sqrMagnitude > 0.25f)
                Owner.transform.position += to.normalized * (Owner.MoveSpeed * 0.5f * Time.deltaTime);
            else
                PickNewPoint();
        }
        private void PickNewPoint() => _target = Owner.transform.position + (Vector3)(Random.insideUnitCircle * 3f);
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
            Owner.MoveTowardTarget();
        }
    }

    /// <summary>攻击：播 Attack（不移动），攻击间隔后回到追击/巡逻</summary>
    public class AttackState : FsmState<EnemyBase>
    {
        private float _attackEnd;
        private int _lastFacing;
        public AttackState(EnemyBase owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.LookAtTarget();
            _lastFacing = Owner.Facing;
            Owner.PlayAction("Attack");
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
            Owner.PlayAction("Hurt");
            _hitEnd = Time.time + 0.2f;
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
