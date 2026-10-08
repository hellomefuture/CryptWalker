using CryptWalker.HotUpdate.Entity.Common;
using UnityEngine;

namespace CryptWalker.HotUpdate.Entity.Player
{
    /// <summary>
    /// 玩家状态机（纯代码 Play 动画版）。
    /// 状态：Idle ↔ Walk/Run ↔ Attack，Hit 可打断进入，Death 终态（后续接结算）。
    /// 动画直接 Animator.Play("{角色}_{动作}{方向}") —— 连招/技能 = 新增状态类，天然可扩展。
    /// </summary>
    public class PlayerFSM
    {
        private readonly Fsm<PlayerController> _fsm = new Fsm<PlayerController>();
        private readonly PlayerController _owner;

        public readonly IdleState   Idle;
        public readonly WalkState   Walk;
        public readonly RunState    Run;
        public readonly AttackState Attack;
        public readonly HitState    Hit;
        public readonly DeathState  Death;

        public PlayerFSM(PlayerController owner)
        {
            _owner = owner;
            Idle   = new IdleState(owner);
            Walk   = new WalkState(owner);
            Run    = new RunState(owner);
            Attack = new AttackState(owner);
            Hit    = new HitState(owner);
            Death  = new DeathState(owner);
        }

        public void Start()          => _fsm.ChangeState(Idle);
        public void Tick()           => _fsm.Tick();
        public bool IsAttacking      => _fsm.Current is AttackState;
        public void ChangeToIdle()   => _fsm.ChangeState(Idle);
        public void ChangeToWalk()   => _fsm.ChangeState(Walk);
        public void ChangeToRun()    => _fsm.ChangeState(Run);
        public void ChangeToAttack() => _fsm.ChangeState(Attack);
        public void ChangeToHit()    => _fsm.ChangeState(Hit);   // 受击打断（外部调用）
        public void ChangeToDeath()  => _fsm.ChangeState(Death); // 终态
    }

    // ==================== 6 个状态 ====================

    /// <summary>待机：播 Idle，有输入 → Walk/Run，左键 → Attack</summary>
    public class IdleState : FsmState<PlayerController>
    {
        public IdleState(PlayerController owner) : base(owner) { }
        public override void OnEnter()
        {
            // 修复：进 Idle 必须 StopMove 清速度（之前漏掉，从 Walk/Run 切回时速度残留 → 玩家滑行）
            Owner.StopMove();
            Owner.PlayAction("Idle");
        }
        public override void OnUpdate()
        {
            if (Owner.MoveInput.sqrMagnitude > 0.01f)
            {
                if (Owner.IsRunning) Owner.Fsm.ChangeToRun();
                else Owner.Fsm.ChangeToWalk();
                return;
            }
            if (Input.GetMouseButtonDown(0)) Owner.Fsm.ChangeToAttack();
        }
    }

    /// <summary>走路：播 Walk，转向重播，Shift → Run，左键 → Attack</summary>
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
            if (Owner.IsRunning) { Owner.Fsm.ChangeToRun(); return; }

            Owner.UpdateFacing();
            if (Owner.FacingDir != _lastFacing)
            {
                _lastFacing = Owner.FacingDir;
                Owner.PlayAction("Walk");
            }
            Owner.Move();
            if (Input.GetMouseButtonDown(0)) Owner.Fsm.ChangeToAttack();
        }
    }

    /// <summary>奔跑：播 Run（Shift+方向），松开 Shift → Walk，左键 → Attack</summary>
    public class RunState : FsmState<PlayerController>
    {
        private int _lastFacing;
        public RunState(PlayerController owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.UpdateFacing();
            _lastFacing = Owner.FacingDir;
            Owner.PlayAction("Run");
        }
        public override void OnUpdate()
        {
            if (Owner.MoveInput.sqrMagnitude < 0.01f) { Owner.Fsm.ChangeToIdle(); return; }
            if (!Owner.IsRunning) { Owner.Fsm.ChangeToWalk(); return; }

            Owner.UpdateFacing();
            if (Owner.FacingDir != _lastFacing)
            {
                _lastFacing = Owner.FacingDir;
                Owner.PlayAction("Run");
            }
            Owner.Move();
            if (Input.GetMouseButtonDown(0)) Owner.Fsm.ChangeToAttack();
        }
    }

    /// <summary>攻击：播 Attack（完全定身），挥击帧命中，硬直结束后按输入回 Walk/Run/Idle</summary>
    public class AttackState : FsmState<PlayerController>
    {
        private float _end;
        private float _hitTime = -1;
        public AttackState(PlayerController owner) : base(owner) { }
        public override void OnEnter()
        {
            // 修复：从 Walk/Run 进入 Attack 时清速度（之前漏调，进入攻击后滑动继续滑动）
            Owner.StopMove();
            Owner.PlayAction("Attack");
            _hitTime = Time.time + Owner.AttackHitDelay;   // 命中延后到动画中段
            _end = Time.time + Owner.AttackLockDuration;
        }
        public override void OnUpdate()
        {
            if (_hitTime >= 0 && Time.time >= _hitTime)
            {
                Owner.PerformAttack();
                _hitTime = -1;   // 只打一次
            }
            if (Time.time >= _end)
            {
                if (Owner.MoveInput.sqrMagnitude > 0.01f)
                {
                    if (Owner.IsRunning) Owner.Fsm.ChangeToRun();
                    else Owner.Fsm.ChangeToWalk();
                }
                else Owner.Fsm.ChangeToIdle();
            }
        }
    }

    /// <summary>受击硬直：播 Hurt（免疫移动），硬直后回 Idle/Walk/Run</summary>
    public class HitState : FsmState<PlayerController>
    {
        private float _hitEnd;
        public HitState(PlayerController owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.PlayAction("Hurt");
            _hitEnd = Time.time + 0.3f;   // 硬直对齐 Hurt 动画时长
        }
        public override void OnUpdate()
        {
            if (Time.time >= _hitEnd)
            {
                if (Owner.MoveInput.sqrMagnitude > 0.01f)
                {
                    if (Owner.IsRunning) Owner.Fsm.ChangeToRun();
                    else Owner.Fsm.ChangeToWalk();
                }
                else Owner.Fsm.ChangeToIdle();
            }
        }
    }

    /// <summary>死亡：播 Death，终态（后续接结算/禁用输入）</summary>
    public class DeathState : FsmState<PlayerController>
    {
        public DeathState(PlayerController owner) : base(owner) { }
        public override void OnEnter()
        {
            Owner.PlayAction("Death");
            Debug.Log("[Player] 死亡动画播放（后续接结算流程）");
        }
    }
}
