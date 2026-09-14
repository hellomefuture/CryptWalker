using CryptWalker.HotUpdate.Entity.Common;
using UnityEngine;

namespace CryptWalker.HotUpdate.Entity.Player
{
    /// <summary>
    /// 玩家状态机（纯代码 Play 动画版）。
    /// 状态：Idle ↔ Walk ↔ Attack，动画直接 Animator.Play("{动作}{方向}")，
    /// 不依赖 Animator 参数/transition —— 连招/技能/闪避 = 新增状态类，天然可扩展。
    /// </summary>
    public class PlayerFSM
    {
        private readonly Fsm<PlayerController> _fsm = new Fsm<PlayerController>();
        private readonly PlayerController _owner;

        public readonly IdleState   Idle;
        public readonly WalkState   Walk;
        public readonly AttackState Attack;

        public PlayerFSM(PlayerController owner)
        {
            _owner = owner;
            Idle   = new IdleState(owner);
            Walk   = new WalkState(owner);
            Attack = new AttackState(owner);
        }

        public void Start()           => _fsm.ChangeState(Idle);
        public void Tick()            => _fsm.Tick();
        public bool IsAttacking       => _fsm.Current is AttackState;
        public void ChangeToIdle()    => _fsm.ChangeState(Idle);
        public void ChangeToWalk()    => _fsm.ChangeState(Walk);
        public void ChangeToAttack()  => _fsm.ChangeState(Attack);
    }

    // ==================== 3 个状态 ====================

    /// <summary>待机：播 Idle{方向}，有输入 → Walk，左键 → Attack</summary>
    public class IdleState : FsmState<PlayerController>
    {
        public IdleState(PlayerController owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.PlayAction("Idle");
        }
        public override void OnUpdate()
        {
            if (Owner.MoveInput.sqrMagnitude > 0.01f) { Owner.Fsm.ChangeToWalk(); return; }
            if (Input.GetMouseButtonDown(0)) Owner.Fsm.ChangeToAttack();
        }
    }

    /// <summary>移动：播 Walk{方向}，转向重播，左键 → Attack</summary>
    public class WalkState : FsmState<PlayerController>
    {
        private int _lastFacing;
        public WalkState(PlayerController owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.UpdateFacing();
            _lastFacing = Owner.FacingDir;
            Owner.PlayAction("Walk");
        }
        public override void OnUpdate()
        {
            if (Owner.MoveInput.sqrMagnitude < 0.01f) { Owner.Fsm.ChangeToIdle(); return; }

            Owner.UpdateFacing();
            if (Owner.FacingDir != _lastFacing)      // 转向 → 重播对应方向 Walk
            {
                _lastFacing = Owner.FacingDir;
                Owner.PlayAction("Walk");
            }
            Owner.Move();
            if (Input.GetMouseButtonDown(0)) Owner.Fsm.ChangeToAttack();
        }
    }

    /// <summary>攻击：播 Attack{方向}（定身），硬直结束后按输入回 Walk/Idle</summary>
    public class AttackState : FsmState<PlayerController>
    {
        private float _end;  //硬直结束时间
        private float _hitTime = -1;  //命中时机； -1表示已经打过
        public AttackState(PlayerController owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.PlayAction("Attack");   // 攻击方向 = 进入时的 Facing
            _hitTime = Time.time + Owner.AttackHitDelay; // 命中延后到动画中段
            _end = Time.time + Owner.AttackLockDuration;
        }
        public override void OnUpdate()
        {
            if (_hitTime >= 0 && Time.time >= _hitTime)
            {
                Owner.PerformAttack();
                _hitTime = -1 ; // 标记已打，防止每帧重复打
            } 

            if (Time.time >= _end)
            { 
                if (Owner.MoveInput.sqrMagnitude > 0.01f ) Owner.Fsm.ChangeToWalk();
                 else Owner.Fsm.ChangeToIdle();
            }
        } 
        
    }
}
