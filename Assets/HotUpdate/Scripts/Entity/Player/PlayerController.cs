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
        [Header("移动")]
        [SerializeField] private float moveSpeed = 3f;

        [Header("战斗")]
        [Tooltip("攻击硬直时长（秒）。攻击期间不响应移动。")]
        [SerializeField] private float attackLockDuration = 0.5f;

        // 方向协议：1=South 2=Left 3=Right 4=North（与敌人/素材一致）
        private const int DIR_South = 1;
        private const int DIR_Left  = 2;
        private const int DIR_Right = 3;
        private const int DIR_North = 4;

        // 方向值 → 名称后缀（拼 clip 名用）："Walk" + "South" = "WalkSouth"
        private static readonly string[] DirName = { "", "South", "Left", "Right", "North" };

        public Animator Anim { get; private set; }
        public PlayerFSM Fsm { get; private set; }
        public Vector2 MoveInput { get; private set; }
        public int FacingDir { get; private set; } = DIR_South;

        public float AttackLockDuration => attackLockDuration;
        public bool IsMoving => MoveInput.sqrMagnitude > 0.01f;
        public bool IsAttacking => Fsm != null && Fsm.IsAttacking;

        private void Awake()
        {
            Anim = GetComponent<Animator>();
            Fsm = new PlayerFSM(this);
        }

        private void Start() => Fsm.Start();

        private void Update()
        {
            ReadInput();
            Fsm.Tick();
        }

        private void ReadInput()
        {
            MoveInput = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));
        }

        // ==================== 状态类调用的接口 ====================

        /// <summary>按当前朝向播动画：Play("Walk" + "South") = "WalkSouth"</summary>
        public void PlayAction(string action)
        {
            if (Anim == null) return;
            Anim.Play($"{action}{DirName[FacingDir]}");
        }

        /// <summary>更新朝向（只在有输入时改，保持上次朝向）</summary>
        public void UpdateFacing()
        {
            if (MoveInput.sqrMagnitude <= 0.01f) return;
            FacingDir = MoveToDirIndex(MoveInput);
        }

        /// <summary>8 向移动（斜向也走，normalized 保证速度一致）</summary>
        public void Move()
        {
            Vector3 delta = new Vector3(MoveInput.x, MoveInput.y, 0f).normalized
                            * (moveSpeed * Time.deltaTime);
            transform.position += delta;
        }

        /// <summary>8 向输入 → 4 向朝向（y 优先，与敌人一致）</summary>
        private int MoveToDirIndex(Vector2 dir)
        {
            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x))
                return dir.y > 0 ? DIR_North : DIR_South;
            return dir.x > 0 ? DIR_Right : DIR_Left;
        }
    }
}
