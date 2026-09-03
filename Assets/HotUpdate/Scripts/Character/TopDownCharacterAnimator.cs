using UnityEngine;

namespace CryptWalker.Character
{
    /// <summary>
    /// 监听 TopDownCharacterController 的状态,把 SpriteRenderer 的 sprite
    /// 切成对应 (动作, 方向, 帧) 的精灵。
    ///
    /// 用纯代码切 sprite 的原因:
    ///   1) 8 方向 × 8 动作 × 4 帧 = 256 个 AnimationClip,用 Animator Controller 搭很繁琐
    ///   2) 代码方案只需要维护 Sprite 数组,逻辑更清晰
    ///   3) 适合毕设展示"程序化动画"思路
    ///
    /// sprite 数组按以下约定填(在 Inspector 里手动配):
    ///   sprites[action][dir][frame]
    ///   action: 0=Idle, 1=Walk, 2=Run, 3=Attack, 4=Hurt, 5=Death, 6=Walk_Attack, 7=Run_Attack
    ///   dir:    0..7 (Facing8)
    ///   frame:  0..3
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(TopDownCharacterController))]
    public class TopDownCharacterAnimator : MonoBehaviour
    {
        public enum Action : int
        {
            Idle        = 0,
            Walk        = 1,
            Run         = 2,
            Attack      = 3,
            Hurt        = 4,
            Death       = 5,
            WalkAttack  = 6,
            RunAttack   = 7,
        }

        [System.Serializable]
        public struct ActionRow
        {
            public Action action;
            [Tooltip("8 方向 × 4 帧 = 32 个 sprite")]
            public Sprite[] sprites; // 长度 32
        }

        [SerializeField] private float walkFps = 8f;
        [SerializeField] private float runFps  = 12f;
        [SerializeField] private float attackFps = 14f;
        [SerializeField] private float hurtFps = 8f;
        [SerializeField] private float idleFps = 6f;

        [SerializeField] private ActionRow[] actionRows;

        private SpriteRenderer _sr;
        private TopDownCharacterController _ctrl;

        // 运行时状态
        private Action _currentAction = Action.Idle;
        private float _frameTimer;
        private int _frameIndex;
        private bool _attackPlaying;
        private float _attackDuration;

        private void Awake()
        {
            _sr   = GetComponent<SpriteRenderer>();
            _ctrl = GetComponent<TopDownCharacterController>();
        }

        private void Start()
        {
            // 初始帧
            SetFrame(Action.Idle, (int)_ctrl.Facing, 0);
        }

        private void Update()
        {
            var next = ResolveAction();
            if (next != _currentAction)
            {
                _currentAction = next;
                _frameIndex = 0;
                _frameTimer = 0f;
                if (next == Action.Attack || next == Action.WalkAttack || next == Action.RunAttack)
                {
                    _attackPlaying = true;
                    _attackDuration = GetFps(next) > 0 ? 4f / GetFps(next) : 0.3f;
                }
                else
                {
                    _attackPlaying = false;
                }
            }

            if (_attackPlaying)
            {
                _attackDuration -= Time.deltaTime;
                if (_attackDuration <= 0f) _attackPlaying = false;
            }

            float fps = GetFps(_currentAction);
            _frameTimer += Time.deltaTime;
            int frameCount = GetFrameCount(_currentAction);
            if (frameCount > 0 && fps > 0f)
            {
                float interval = 1f / fps;
                while (_frameTimer >= interval)
                {
                    _frameTimer -= interval;
                    _frameIndex++;
                    if (_currentAction == Action.Death)
                    {
                        _frameIndex = Mathf.Min(_frameIndex, frameCount - 1);
                    }
                    else
                    {
                        _frameIndex %= frameCount;
                    }
                }
            }

            // 死亡后冻结,其他循环
            SetFrame(_currentAction, (int)_ctrl.Facing, _frameIndex);

            // 死亡时角色静止(可选:这里只冻帧, 移动由 Controller 自己控)
        }

        private Action ResolveAction()
        {
            if (_ctrl.AttackTriggered || _attackPlaying)
            {
                if (_ctrl.IsMoving) return _ctrl.IsRunning ? Action.RunAttack : Action.WalkAttack;
                return Action.Attack;
            }
            if (_ctrl.IsMoving) return _ctrl.IsRunning ? Action.Run : Action.Walk;
            return Action.Idle;
        }

        private float GetFps(Action a)
        {
            switch (a)
            {
                case Action.Idle: return idleFps;
                case Action.Walk: return walkFps;
                case Action.Run:  return runFps;
                case Action.Attack: case Action.WalkAttack: case Action.RunAttack: return attackFps;
                case Action.Hurt: return hurtFps;
                default: return 6f;
            }
        }

        private int GetFrameCount(Action a)
        {
            if (TryFindRow(a, out var row))
                return row.sprites != null ? Mathf.Min(row.sprites.Length, 32) : 0;
            return 0;
        }

        private bool TryFindRow(Action a, out ActionRow row)
        {
            row = default;
            if (actionRows == null) return false;
            for (int i = 0; i < actionRows.Length; i++)
            {
                if (actionRows[i].action == a)
                {
                    row = actionRows[i];
                    return true;
                }
            }
            return false;
        }

        private void SetFrame(Action a, int dir, int frame)
        {
            if (!TryFindRow(a, out var row)) return;
            if (row.sprites == null || row.sprites.Length == 0) return;
            dir = Mathf.Clamp(dir, 0, 7);
            int idx = dir * 4 + Mathf.Clamp(frame, 0, 3);
            if (idx >= row.sprites.Length) return;
            _sr.sprite = row.sprites[idx];
        }
    }
}
