using UnityEngine;

namespace CryptWalker.HotUpdate.Character
{
    /// <summary>
    /// 4 方向玩家控制 —— 驱动你自己配的 player.controller（单 Int 参数 WalkStates）。
    ///
    /// ═══ 与 player.controller 的协议（必须一致）═══
    ///   WalkStates:  0 = Idle
    ///                1 = WalkSouth（朝下 S）
    ///                2 = WalkLeft （朝左 A / West）
    ///                3 = WalkRight（朝右 D / East）
    ///                4 = WalkNorth（朝上 W）
    ///
    /// ═══ 输入 ═══
    ///   WASD（Legacy Input），4 方向离散（同时按多键取分量大的轴）。
    ///
    /// ═══ 你已经知道的 Animator 缺口（本脚本已兜底）═══
    ///   你的 Walk 状态之间没有 cross-walk transition，转向时动画不会切。
    ///   兜底方案：方向变化时先把 WalkStates 置 0（插一帧 Idle），下一帧再置新方向。
    ///   更干净的解法（纯 Animator）：在各 Walk 状态补上到其它方向的 transition，见类底注释。
    ///
    /// ═══ 场景挂载 ═══
    ///   GameObject 上需要：SpriteRenderer + Animator(runtimeAnimatorController=player) + 本脚本
    ///   相机：加一个简单的跟随脚本，或手动给 Camera 挂一个 LateUpdate 跟随。
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [Header("移动")]
        [Tooltip("移动速度（单位/秒）")]
        [SerializeField] private float moveSpeed = 3f;

        [Header("动画参数名（与 player.controller 一致）")]
        [SerializeField] private string stateParam = "WalkStates";

        // Animator 节点编码 —— 与你的协议严格对应
        private const int ST_Idle    = 0;
        private const int ST_South   = 1;
        private const int ST_Left    = 2;
        private const int ST_Right   = 3;
        private const int ST_North   = 4;

        private Animator _anim;
        private int _currentState = ST_Idle;   // 当前已下发的状态
        private int _pendingState = ST_Idle;   // 转向时排队的下一个状态（下一帧生效）

        // 公共只读属性（供 HUD/其他系统读）
        public Vector2 MoveInput { get; private set; }
        public bool IsMoving => MoveInput.sqrMagnitude > 0.01f;
        public int FacingState => _currentState;   // 0=Idle, 1..4=方向

        private void Awake()
        {
            if (_anim == null) _anim = GetComponent<Animator>();
        }

        private void Update()
        {
            // 1) 若上一帧排队了转向，先提交
            if (_pendingState != ST_Idle)
            {
                SetState(_pendingState);
                _pendingState = ST_Idle;
                return;   // 本帧不再读写输入，保证 Idle → 新方向跨帧稳定
            }

            // 2) 读输入并做 4 方向离散
            ReadInputRaw();
            DiscretizeTo4();

            // 3) 下发状态
            UpdateState();

            // 4) 移动
            Move();
        }

        // ---------- 输入读取 ----------
        private void ReadInputRaw()
        {
            float h = Input.GetAxisRaw("Horizontal"); // A=-1, D=+1
            float v = Input.GetAxisRaw("Vertical");   // S=-1, W=+1
            MoveInput = new Vector2(h, v);
        }

        /// <summary>4 方向离散：同时按两键取分量大的轴，避免斜向。</summary>
        private void DiscretizeTo4()
        {
            if (Mathf.Abs(MoveInput.x) > Mathf.Abs(MoveInput.y))
                MoveInput = new Vector2(Mathf.Sign(MoveInput.x), 0f);
            else if (Mathf.Abs(MoveInput.y) > 0.01f)
                MoveInput = new Vector2(0f, Mathf.Sign(MoveInput.y));
            else
                MoveInput = Vector2.zero;
        }

        // ---------- 状态机 ----------
        /// <summary>由输入决定目标状态，处理「转向需先过 Idle」的兜底。</summary>
        private void UpdateState()
        {
            int target = MoveToStateIndex(MoveInput);

            // 目标与当前相同：不动
            if (target == _currentState) return;

            // 若正在走路且要换方向：你的 Animator 没有 WalkX→WalkY，
            // 先置 0 回 Idle，下一帧再提交新方向。
            if (_currentState != ST_Idle && target != ST_Idle)
            {
                SetState(ST_Idle);
                _pendingState = target;   // 下一帧 Update 顶部提交
                return;
            }

            // 普通切换（Idle↔Walk，或第一次进入某个方向）
            SetState(target);
        }

        private int MoveToStateIndex(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.01f) return ST_Idle;
            if (dir.y > 0)      return ST_North;   // W
            if (dir.y < 0)      return ST_South;   // S
            if (dir.x > 0)      return ST_Right;   // D/East
            return ST_Left;                        // A/West
        }

        private void SetState(int state)
        {
            _currentState = state;
            if (_anim != null) _anim.SetInteger(stateParam, state);
        }

        // ---------- 移动 ----------
        private void Move()
        {
            if (!IsMoving) return;
            Vector3 delta = new Vector3(MoveInput.x, MoveInput.y, 0f) * (moveSpeed * Time.deltaTime);
            transform.position += delta;
        }
    }
}