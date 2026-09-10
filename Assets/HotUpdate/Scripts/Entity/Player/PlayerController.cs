using UnityEngine;

namespace CryptWalker.HotUpdate.Entity.Player
{
    /// <summary>
    /// 4 方向玩家控制 —— 驱动 player.controller。
    ///
    /// ═══ 与 player.controller 的协议（必须严格一致）═══
    ///   FaceDir (Int):   1 = South（朝下 S）
    ///                    2 = Left  （朝左 A）
    ///                    3 = Right （朝右 D）
    ///                    4 = North （朝上 W）
    ///   IsMoving (Bool): true = 正在走路 / false = 待机或攻击中停下
    ///   Attack (Trigger): 一次性触发
    ///
    /// ═══ 移动 ═══
    ///   WASD 输入保留 8 向（斜向也走），动画按 4 向离散（y 优先）。
    ///   _lastDir 缓存上次的 4 向朝向，松手后保留，确保 Animator 切回正确 Idle。
    ///
    /// ═══ 攻击 ═══
    ///   鼠标左键：触发 Attack + 锁定 0.5s 硬直（不移动、不改朝向）。
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [Header("移动")]
        [Tooltip("移动速度（单位/秒）")]
        [SerializeField] private float moveSpeed = 3f;

        [Header("动画参数名（与 player.controller 一致）")]
        [SerializeField] private string faceDirParam = "FaceDir";
        [SerializeField] private string movingParam   = "IsMoving";
        [SerializeField] private string attackParam   = "Attack";

        [Header("战斗")]
        [Tooltip("攻击硬直时长（秒）")]
        [SerializeField] private float attackLockDuration = 0.5f;

        // 朝向协议常量
        private const int DIR_South = 1;
        private const int DIR_Left  = 2;
        private const int DIR_Right = 3;
        private const int DIR_North = 4;

        private Animator _anim;
        // _lastDir: 缓存上次的 4 向朝向（即使松手也保持），Animator 据此切回 IdleX
        private int _lastDir = DIR_South;
        private float _attackLockUntil = 0f;

        public Vector2 MoveInput { get; private set; }
        public bool IsMoving => MoveInput.sqrMagnitude > 0.01f;
        public int FacingDir => _lastDir;
        public bool IsAttacking => Time.time < _attackLockUntil;

        private void Awake()
        {
            if (_anim == null) _anim = GetComponent<Animator>();
        }

        private void Update()
        {
            if (_anim == null) return;

            // 1) 读输入
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            MoveInput = new Vector2(h, v);

            bool isAttacking = IsAttacking;
            bool hasInput = MoveInput.sqrMagnitude > 0.01f;

            // 2) 朝向：仅在非攻击期 + 有输入时计算并缓存
            //    松手后 _lastDir 保持，Animator 用它切到对应 IdleX
            if (!isAttacking && hasInput)
            {
                _lastDir = MoveToDirIndex(MoveInput);
            }

            // 3) 写 Animator：每帧都写 FaceDir + IsMoving
            //    原因：Animator 每帧末统一求值，每帧写能保证方向跟得上
            _anim.SetInteger(faceDirParam, _lastDir);
            _anim.SetBool(movingParam, !isAttacking && hasInput);

            // 4) 移动：攻击期不响应
            if (!isAttacking && hasInput)
            {
                Vector3 delta = new Vector3(MoveInput.x, MoveInput.y, 0f).normalized
                                * (moveSpeed * Time.deltaTime);
                transform.position += delta;
            }

            // 5) 攻击
            if (Input.GetMouseButtonDown(0))
            {
                _anim.SetTrigger(attackParam);
                _attackLockUntil = Time.time + attackLockDuration;
            }
        }

        /// <summary>8 向输入 → 4 向离散（y 优先，斜向取竖直）</summary>
        private int MoveToDirIndex(Vector2 dir)
        {
            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x))
                return dir.y > 0 ? DIR_North : DIR_South;
            return dir.x > 0 ? DIR_Right : DIR_Left;
        }
    }
}
