using UnityEngine;

namespace CryptWalker.Character
{
    /// <summary>
    /// 8 方向枚举(对应 sprite sheet 的列)。
    /// 顺序按 CraftPix Swordsman Top-Down 资源包约定:
    ///   0=South(下) 1=SouthEast 2=East 3=NorthEast
    ///   4=North(上) 5=NorthWest 6=West  7=SouthWest
    /// </summary>
    public enum Facing8
    {
        South     = 0,
        SouthEast = 1,
        East      = 2,
        NorthEast = 3,
        North     = 4,
        NorthWest = 5,
        West      = 6,
        SouthWest = 7,
    }

    /// <summary>
    /// 8 方向俯视角色运行时控制。
    /// 输入 -> 移动 -> 朝向 + 状态 -> Animator 参数。
    ///
    /// 设计要点:
    ///   1) 不直接依赖具体 Animator, 只暴露"当前状态枚举"。
    ///      Animator 端的 State 由 TopDownCharacterAnimator 监听并切换 sprite。
    ///   2) 朝向用 8 方向离散,不用角度,避免浮点误差。
    ///   3) 移动用 CharacterController(碰撞) 或 Transform(无碰撞),
    ///      默认 CharacterController(更通用)。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class TopDownCharacterController : MonoBehaviour
    {
        [Header("Move")]
        [SerializeField] private float walkSpeed = 2.5f;
        [SerializeField] private float runSpeed = 4.5f;

        [Header("Input (Legacy Input)")]
        [SerializeField] private KeyCode runKey = KeyCode.LeftShift;

        // 公共状态(供 Animator 监听)
        public Vector2 MoveInput { get; private set; }   // 原始 -1..1
        public Vector2 MoveDir   { get; private set; }   // 归一化
        public Facing8 Facing    { get; private set; } = Facing8.South;
        public bool IsMoving     => MoveInput.sqrMagnitude > 0.01f;
        public bool IsRunning    { get; private set; }
        public bool AttackTriggered { get; private set; }

        private CharacterController _cc;

        // 攻击状态:按下攻击键后一帧内为 true,触发 Animator 后由 Animator 端清
        public void ConsumeAttack() => AttackTriggered = false;

        private void Awake() => _cc = GetComponent<CharacterController>();

        private void Update()
        {
            ReadInput();
            UpdateFacing();
            UpdateAnimatorParams();
            ApplyMovement();
        }

        private void ReadInput()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            MoveInput = new Vector2(h, v);
            if (MoveInput.sqrMagnitude > 1f) MoveInput = MoveInput.normalized;
            MoveDir = MoveInput.normalized;
            IsRunning = Input.GetKey(runKey) && IsMoving;

            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.J))
            {
                AttackTriggered = true;
            }
        }

        private void UpdateFacing()
        {
            if (!IsMoving) return;
            // atan2 返回弧度:East=0, North=90(在 2D 里 y 上是北)
            // 注意:在 2D top-down 里,世界 Y 向下(z=0 平面),所以 v>0 是 South
            // 我们的方向枚举:0=South, 1=SouthEast, 2=East, ...
            // 角度(degree, 0..360, East=0, 顺时针): atan2(-v, h)
            float deg = Mathf.Atan2(-MoveDir.y, MoveDir.x) * Mathf.Rad2Deg;
            if (deg < 0) deg += 360f;
            // 切到 8 段
            int idx = Mathf.RoundToInt(deg / 45f) % 8;
            Facing = (Facing8)idx;
        }

        private void UpdateAnimatorParams()
        {
            // Animator 参数约定:
            //   Facing (int)   0..7
            //   Speed (float)  0 idle, 1 walk, 2 run
            //   Attack (trigger)
            var a = GetComponent<Animator>();
            if (a == null) return;
            a.SetInteger("Facing", (int)Facing);
            float speed = 0f;
            if (IsMoving) speed = IsRunning ? 2f : 1f;
            a.SetFloat("Speed", speed);
            if (AttackTriggered)
            {
                a.SetTrigger("Attack");
                ConsumeAttack();
            }
        }

        private void ApplyMovement()
        {
            float speed = IsRunning ? runSpeed : walkSpeed;
            Vector3 delta = new Vector3(MoveDir.x, MoveDir.y, 0f) * (speed * Time.deltaTime);
            _cc.Move(delta);
        }
    }
}
