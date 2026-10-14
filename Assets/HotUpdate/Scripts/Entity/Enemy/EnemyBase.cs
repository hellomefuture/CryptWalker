using System.Collections.Generic;
using System.Text;
using CryptWalker.HotUpdate.Entity.Player;
using CryptWalker.Main.Bridge;         // IHotTunable（AOT 代理注入配置）
using CryptWalker.Main.Data;           // EnemyConfig（AOT 的敌人数值配置）
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
    public class EnemyBase : MonoBehaviour, IHotTunable
    {
        [Header("配置（对应策划案 5.2 敌人属性）")]
        [SerializeField] private string enemyName = "Orc1";   // 必须 = clip 前缀（Orc1/Vampires1...）
        [SerializeField] private float maxHp = 100f;
        [SerializeField] private float moveSpeed = 2f;
        [SerializeField] private float sightRange = 30f;       // 发现玩家距离（30 = 跨房间都能识别，测试期方便）
        [SerializeField] private float attackRange = 1.5f;    // 近战攻击范围
        [SerializeField] private float attackInterval = 1.2f; // 攻击间隔
        [SerializeField] private float attackDamage = 20f;    // 每次攻击伤害
        [SerializeField] private float attackHitDelay = 0.3f; // 命中延迟（动画挥击帧）
        [SerializeField] private float defense = 10f;         // 防御（策划案 §3.3 减伤公式）
        [SerializeField] private bool isBoss = false;         // Boss 标记（策划案 §3.2 剧毒爆燃衰  减）

        [Header("掉落（策划案 §二 局内循环：击杀获取金币）")]
        [Tooltip("击杀掉落金币数。Boss 会自动按 BOSS_COIN_MULTIPLIER 放大")]
        [SerializeField] private int coinDrop = 2;
        /// <summary>Boss 金币倍率（策划案：精英/Boss 奖励更高）</summary>
        private const int BOSS_COIN_MULTIPLIER = 5;

        [Header("击退")]
        [Tooltip("击退速度的衰减率下限（单位/秒²）。调大 → 击退更干脆；实际衰减率还会被 duration 顶上去，保证在 duration 内衰减完")]
        [SerializeField] private float knockbackDecay = 5f;

        [Header("A* 寻路（策划案 5.3 敌人 AI）")]
        [Tooltip("重算路径的最小间隔（秒）—— 节流，避免每帧都跑一次 A*")]
        [SerializeField] private float repathInterval = 0.35f;
        [Tooltip("目标移动超过这个距离（格）就立刻重算路径，不等节流")]
        [SerializeField] private float repathOnTargetMove = 2f;
        [Tooltip("到达某个 waypoint 的判定距离（格）")]
        [SerializeField] private float waypointReachDist = 0.3f;
        [Tooltip("卡住判定的观察窗口（秒）：窗口内实际走的路程不足期望的 40% 才算卡住（与帧率无关）")]
        [SerializeField] private float stuckCheckWindow = 0.5f;

        [Tooltip("【排查用】勾选后每秒打印一次寻路状态 + 可走格 ASCII 图。定位完问题请关掉。" +
                 "（日志级别是 Info，Release 包里会被编译期自动剔除，不用担心带进正式包）")]
        [SerializeField] private bool debugPath = false;

        [Header("元素反应状态")]
        private float _blindExpireTime;    // 致盲过期时间戳（策划 §3.2 蒸汽爆炸）
        private float _stunExpireTime;     // 硬直过期时间戳（策划 §3.2 过载爆发打断施法）

        [Header("目标（测试时在 Inspector 拖玩家，正式由索敌系统注入）")]
        [SerializeField] private Transform _target;

        private SpriteRenderer spriteRenderer;
        private Rigidbody2D _rb;   // 物理移动：transform.position += 不触发碰撞，必须走刚体速度

        // ==================== A* 寻路状态 ====================
        private AI.NavGrid _navGrid;                       // 可走性网格（由 DungeonGenerator 注入）
        private AI.AStarGrid _astar;                       // 自写 A*，现在只作插件不可用时的退回
        // 路径点用【世界坐标】：A* 插件返回的是 vectorPath（已做视线简化），
        // 直接用它就能吃到插件的平滑结果；若转回格子中心，等于又把路径拉回折线。
        private readonly List<Vector3> _path = new List<Vector3>(128);
        // 退回自写 A* 时的中转缓冲（那个还是格子坐标）
        private readonly List<Vector2Int> _legacyCellPath = new List<Vector2Int>(128);
        private bool _hasPath;
        private int _pathIndex;
        private float _nextRepathTime;
        private Vector3 _lastRepathTargetPos;

        // 卡墙检测 / 脱困（时间窗口版，帧率无关）
        private Vector3 _lastStuckCheckPos;
        private float _stuckWindowEndTime;   // 当前观察窗口的结束时刻
        private float _stuckExpected;        // 本窗口内"本该走的路程"
        private float _stuckTraveled;        // 本窗口内"实际走的路程"
        private float _sidestepUntil;
        private Vector2 _sidestepDir;

        // 击退：独立的"残余击退速度"通道。
        // 为什么不直接 `_rb.velocity += velocity`：FSM 每个状态每帧都会用
        // MoveTowardPoint / StopMove 重设 _rb.velocity，直接加到 _rb.velocity 上
        // 下一帧就被覆盖了 —— 这就是原来击退"接了但完全没效果"的原因。
        private Vector2 _knockbackVelocity;
        private float _knockbackDecayRate = 5f;   // 本次击退实际使用的衰减率（单位/秒²）

        // 诊断（静态闸门：同屏每秒最多打印一个敌人的 dump，避免刷屏）
        private static float s_nextDebugTime;
        private bool _warnedNoNavGrid;

        private static readonly string[] DirName = { "", "South", "Left", "Right", "North" };

        public Animator Anim { get; private set; }
        public EnemyFSM Fsm { get; private set; }
        public Transform Target { get => _target; set => _target = value; }
        public float MaxHp => maxHp;
        public float CurrentHp => Hp;
        public float Hp { get; private set; }
        public int Facing { get; private set; } = 1; // 1=South 2=Left 3=Right 4=North
        public float MoveSpeed => moveSpeed;
        public float AttackInterval => attackInterval;
        public float AttackHitDelay => attackHitDelay;
        public float AttackDamage => attackDamage;
        public float Attack => attackDamage;   // 别名（策划案 §3.3 元素反应公式用 Attack）
        public float Defense => defense;
        public bool IsBoss => isBoss;
        /// <summary>击杀实际掉落金币数（Boss 放大）</summary>
        public int CoinDrop => isBoss ? coinDrop * BOSS_COIN_MULTIPLIER : coinDrop;
        public bool IsDead => Hp <= 0f;
        public bool IsBlinded => Time.time < _blindExpireTime;
        public bool IsStunned => Time.time < _stunExpireTime;

        private void Awake()
        {
            Anim = GetComponent<Animator>();
            Hp = maxHp;
            Fsm = new EnemyFSM(this);
            spriteRenderer = GetComponent<SpriteRenderer>();
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null)
                Debug.LogWarning($"[EnemyBase] {name} 缺少 Rigidbody2D（Dynamic/Gravity=0/FreezeRotation-Z），移动将无法碰撞");

            // 兜底：sightRange 配错为 0 时仍能识别（之前 prefab 字段被改过，可能漏配）
            if (sightRange <= 0.1f) sightRange = 6f;

            // 禁用玩家层与敌人层之间的物理碰撞（玩家可穿过敌人，但攻击判定仍生效）
            // 层号：Player=8, Enemy=9（见 ProjectSettings/TagManager.asset）
            Physics2D.IgnoreLayerCollision(8, 9, true);
        }

        /// <summary>
        /// 注入敌人数值（由 AOT 侧的 EnemyProxy 在运行时调用 ✗）。
        ///
        /// ⚠️【必须重新派生，不能只赋值】✗
        ///   本组件现在是运行时 AddComponent 出来的 ✗ → Unity 会**立刻**执行上面的 Awake ✓
        ///   而 Awake 已经消费过这些字段了 ✗：
        ///       Hp = maxHp                          ← 直接用了 maxHp
        ///       if (sightRange <= 0.1f) sightRange = 6f;   ← 直接用了 sightRange
        ///   所以只赋值不重算 ✗ → Hp 会停在"Awake 那一刻的 maxHp" ✗
        ///   （现在配置恰好等于默认值 ✓ 看不出来 ✗；哪天把某类敌人写成 maxHp=300 就会立刻暴露 ✓）
        ///
        /// 一句话规矩 ✓：**凡 Awake 里"用过"的字段 ✗，注入之后都要重新派生一次 ✓**
        /// </summary>
        public void ApplyTuning(object tuning)
        {
            if (!(tuning is EnemyConfig c)) return;

            enemyName = c.enemyName;
            maxHp = c.maxHp;
            moveSpeed = c.moveSpeed;
            sightRange = c.sightRange;
            attackRange = c.attackRange;
            attackInterval = c.attackInterval;
            attackDamage = c.attackDamage;
            attackHitDelay = c.attackHitDelay;
            defense = c.defense;
            isBoss = c.isBoss;
            coinDrop = c.coinDrop;
            knockbackDecay = c.knockbackDecay;
            repathInterval = c.repathInterval;
            repathOnTargetMove = c.repathOnTargetMove;
            waypointReachDist = c.waypointReachDist;
            stuckCheckWindow = c.stuckCheckWindow;
            debugPath = c.debugPath;

            // —— 重新派生 Awake 里算过的值 ✗ ——
            Hp = maxHp;
            if (sightRange <= 0.1f) sightRange = 6f;   // 与 Awake 里的兜底保持一致 ✓
        }

        private void Start()
        {
            // 兜底：通过 PlayerController.Instance 全局静态引用获取玩家 transform
            // 不依赖 Instantiate 顺序，且避免 FindObjectOfType 扫描开销
            if (Target == null)
            {
                var pc = CryptWalker.HotUpdate.Entity.Player.PlayerController.Instance;
                if (pc != null) Target = pc.transform;
                else
                {
                    // 静态引用也没设（玩家还未 Awake），延迟到第一次 Update 前查找
                    var player = Object.FindObjectOfType<CryptWalker.HotUpdate.Entity.Player.PlayerController>();
                    if (player != null) Target = player.transform;
                }
            }
            Fsm.Start();
        }
        private void Update()
        {
            // 先衰减击退，再让 FSM 决定转向 —— 顺序反了的话本帧击退会少衰减一次
            DecayKnockback();
            Fsm.Tick();
        }

        /// <summary>
        /// 击退速度衰减：每帧按 _knockbackDecayRate 把残余击退速度收敛到 0。
        /// 用 MoveTowards 而不是直接相减，保证不会过冲到反方向。
        /// </summary>
        private void DecayKnockback()
        {
            if (_knockbackVelocity.sqrMagnitude <= 0.0001f)
            {
                _knockbackVelocity = Vector2.zero;
                return;
            }

            _knockbackVelocity = Vector2.MoveTowards(_knockbackVelocity, Vector2.zero,
                                                     _knockbackDecayRate * Time.deltaTime);
        }

        private void LateUpdate()
        {
            // 修复：强制设 SortingLayer = Character（不依赖 Inspector prefab 配置）
            // 策划 §9 + SortingLayer 分层：敌人在 Character 层，与玩家同层
            // 同层内只用 Y 排序：y 大（屏幕上方）→ -y*100 小 → 排序靠前 → 画在前面
            spriteRenderer.sortingLayerName = "Character";
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
        public float DistanceToTarget()
        {
            if (Target == null) return float.MaxValue;
            return Vector2.Distance(transform.position, Target.position);
        }

        /// <summary>朝目标移动（Chase 用）：设刚体速度，由物理引擎处理碰撞</summary>
        public void MoveTowardTarget()
        {
            if (Target == null) return;
            MoveTowardPoint(Target.position, 1f);
        }

        /// <summary>朝任意点移动（Patrol 用）：speedScale 为速度倍率（巡逻 0.5 = 半速）</summary>
        public void MoveTowardPoint(Vector3 point, float speedScale)
        {
            if (_rb == null) return;

            // 脱困优先：卡墙后由 CheckStuck 触发的短暂侧向移动，覆盖正常寻路方向。
            // （不这么做的话，下一帧 MoveTowardPoint 又会把速度设回"顶墙方向"，永远出不来）
            if (Time.time < _sidestepUntil)
            {
                ApplyVelocity(_sidestepDir * (moveSpeed * speedScale));
                return;
            }

            Vector2 to = point - transform.position;
            if (to.sqrMagnitude <= 0.01f)
            {
                ApplyVelocity(Vector2.zero);
                return;
            }
            ApplyVelocity(to.normalized * (moveSpeed * speedScale));
        }

        /// <summary>
        /// 统一的速度出口：把"寻路转向速度"和"残余击退速度"合成后写进刚体。
        /// 所有移动都必须走这里，否则击退通道会被绕过（击退就会失效）。
        /// </summary>
        private void ApplyVelocity(Vector2 steering)
        {
            if (_rb == null) return;
            _rb.velocity = steering + _knockbackVelocity;
        }

        /// <summary>
        /// 停止移动（清转向速度）：Idle/攻击/受击/死亡 进入时调用，防止速度残留滑行。
        /// 注意：故意**保留残余击退速度** —— 受击/定身期间敌人本来就该还能被击退滑走，
        /// 如果这里把速度清成 0，过载爆发的击退会在进入 Hit 状态的那一瞬间就被抹掉。
        /// </summary>
        public void StopMove()
        {
            if (_rb != null) _rb.velocity = _knockbackVelocity;
        }

        // ==================== A* 寻路 ====================

        /// <summary>是否已注入可走性网格（DungeonGenerator 生成关卡时调用 SetNavGrid 注入）</summary>
        public bool HasNavGrid => _navGrid != null;

        /// <summary>注入可走性网格。重新生成关卡时换新网格会清掉旧路径。</summary>
        public void SetNavGrid(AI.NavGrid nav)
        {
            _navGrid = nav;
            _hasPath = false;
            _path.Clear();
            _pathIndex = 0;
        }

        /// <summary>敌人当前所在格坐标</summary>
        public Vector2Int CurrentCell => AI.NavGrid.WorldToCell(transform.position);

        /// <summary>
        /// 朝目标寻路移动（Chase 用）。三级策略，从便宜到贵：
        ///   1) 与目标之间没有墙 → <b>直线冲</b>（既省性能，也没有"一格一格拐"的锯齿感）
        ///   2) 有墙 → 走 A* 路径的 waypoint
        ///   3) 没注入网格 → 退化为旧的直线移动（保证不注入也能跑，方便单独测试）
        /// </summary>
        public void MoveAlongPath()
        {
            if (_rb == null || Target == null) return;

            if (debugPath && Time.time >= s_nextDebugTime)
            {
                s_nextDebugTime = Time.time + 1f;
                DumpPathState();
            }

            // —— 没网格：退化直线（兼容旧行为）——
            if (_navGrid == null)
            {
                // 必须"喊出来"：这个降级以前是完全静默的，导致"没注入 NavGrid"这种严重问题
                // 表现成"敌人只是走得笨"，排查时完全看不出来。只喊一次，不刷屏。
                if (!_warnedNoNavGrid)
                {
                    _warnedNoNavGrid = true;
                    Core.Log.Warn($"[Enemy] {name} 未注入 NavGrid → A* 寻路未启用，已退化为直线移动。"
                                + "请确认 DungeonGenerator 生成关卡时成功调用了 SetNavGrid"
                                + "（若 Console 同时有「prefab 上找不到 EnemyBase 组件」，则是 HybridCLR 双程序集问题）。");
                }
                MoveTowardTarget();
                return;
            }

            // —— 1) 有视线：直冲 ——
            Vector2Int targetCell = AI.NavGrid.WorldToCell(Target.position);
            if (AI.AStarGrid.HasLineOfSight(_navGrid, CurrentCell, targetCell))
            {
                _hasPath = false;          // 直线阶段不需要路径，等被墙挡住再重算
                MoveTowardPoint(Target.position, 1f);
                CheckStuck();
                return;
            }

            // —— 2) 需要 A* 路径 ——
            // 统一节流：任何重算都要么过了 repathInterval，要么是"目标明显移动"。
            // 为什么不写成 `if (!_hasPath || 时间到)`：那样一旦寻路失败（目标不可达 / 超节点上限），
            // _hasPath 会一直是 false → 每帧都重跑一次 A*，直接拖垮帧率。
            // Repath() 无论成败都会推进 _nextRepathTime，所以这里天然限流。
            if (Time.time >= _nextRepathTime || TargetMovedEnough()) Repath();

            if (!_hasPath)
            {
                // 本次没拿到路径（等待下次节流窗口）：先直线靠近，别站着不动
                MoveTowardTarget();
                CheckStuck();
                return;
            }

            // 跳过已经走到的 waypoint（可能一次跨过多个）
            while (_pathIndex < _path.Count)
            {
                Vector2 wp = _path[_pathIndex];   // 已是世界坐标
                if ((wp - (Vector2)transform.position).sqrMagnitude > waypointReachDist * waypointReachDist) break;
                _pathIndex++;
            }

            if (_pathIndex >= _path.Count)
            {
                // 路径走完但还没贴到玩家（可能玩家又移动了）→ 下一帧重算
                _hasPath = false;
                MoveTowardTarget();
                CheckStuck();
                return;
            }

            MoveTowardPoint(_path[_pathIndex], 1f);   // 已是世界坐标
            CheckStuck();
        }

        /// <summary>
        /// 【排查用】把寻路的关键状态 + 可走格 ASCII 图打到 Console。一眼能看出三件事：
        ///   1) NavGrid 是否注入、敌人格 / 玩家格是否可走
        ///   2) "有视线就直冲"的快速路径是否在生效（LOS=True 时压根不该出现走格子）
        ///   3) 可走格区是否和地板对得上（是整片，还是碎的 / 错位的）
        /// </summary>
        private void DumpPathState()
        {
            Vector2Int cur = CurrentCell;
            Vector2Int goal = AI.NavGrid.WorldToCell(Target.position);
            bool los = AI.AStarGrid.HasLineOfSight(_navGrid, cur, goal);

            var sb = new StringBuilder(4096);
            sb.Append($"[PathDebug] {name}  dist={DistanceToTarget():F1}  attackRange={attackRange:F1}\n");
            sb.Append($"  敌人格={cur} 可走={_navGrid.IsWalkable(cur)}    玩家格={goal} 可走={_navGrid.IsWalkable(goal)}\n");
            sb.Append($"  视线LOS={los}   ← True 表示会直冲玩家，那种情况下不该出现「按格子走」\n");
            sb.Append($"  hasPath={_hasPath}  pathCount={_path.Count}  pathIndex={_pathIndex}  velocity={_rb.velocity:F2}\n");

            if (_path.Count > 0)
            {
                sb.Append("  路径:");
                int show = Mathf.Min(_path.Count, 12);
                for (int i = 0; i < show; i++)
                    sb.Append($" {_path[i]}{(i == _pathIndex ? "*" : "")}");
                if (_path.Count > show) sb.Append($" ...(共 {_path.Count} 点)");
                sb.Append("    （* = 当前跟踪的 waypoint）\n");

                // 路径现在来自 A* 插件的 vectorPath（世界坐标 + 视线简化），
                // "对角步数"是格子坐标才有的概念，改看路点数与总长：
                // 路点数比原来的格子路径明显更少 = 插件的简化生效了。
                float totalLen = 0f;
                for (int i = 1; i < _path.Count; i++) totalLen += Vector3.Distance(_path[i - 1], _path[i]);
                sb.Append($"  路点数={_path.Count}（格子路径会明显更多）  路径总长={totalLen:F1}\n");
            }

            sb.Append(_navGrid.DumpRegion(cur, 24, 10, cur, goal));
            Core.Log.Info(sb.ToString());
        }

        /// <summary>
        /// 目标是否已经移动得足够远，值得提前重算路径（不必等节流窗口）。
        /// 因为 Repath() 会同步刷新 _lastRepathTargetPos，这个条件在重算后立刻变 false，
        /// 所以最多每"移动 repathOnTargetMove 格"触发一次，不会退化成每帧寻路。
        /// </summary>
        private bool TargetMovedEnough()
        {
            if (Target == null) return false;
            return (Target.position - _lastRepathTargetPos).sqrMagnitude
                   >= repathOnTargetMove * repathOnTargetMove;
        }

        /// <summary>
        /// 跑一次寻路，结果写入可复用的 _path。无论成败都会推进节流时间戳。
        ///
        /// 路径来源（方案 B）：
        ///   1) 首选 **A* Pathfinding Project** 的 ABPath —— 只借它"算路径"，
        ///      沿路径怎么走仍然由本类的 MoveAlongPath 负责（手感留在热更层，可热更）。
        ///   2) 插件不可用（没建图 / 没扫完 / 刚换场景）→ 退回自写的格子 A*，
        ///      表现会退化但敌人不会当场停摆。
        /// </summary>
        private void Repath()
        {
            _nextRepathTime = Time.time + repathInterval;
            _lastRepathTargetPos = Target != null ? Target.position : transform.position;

            _path.Clear();
            _pathIndex = 0;
            _hasPath = false;

            if (_navGrid == null || Target == null) return;

            // —— 1) 首选：A* 插件 ——
            // 起终点都给【世界坐标实位】，不用吸附格子：插件自己会把不可走的点
            // 吸附到最近的可走节点，比原来的 CellToWorld/WorldToCell 往返更准。
            if (AI.AstarPathService.TryFindPath(transform.position, Target.position, _path))
            {
                _hasPath = _path.Count > 1;
                // 第 0 个点是敌人自己所在位置，从下一个开始跟
                _pathIndex = _hasPath ? 1 : 0;
                return;
            }

            // —— 2) 退回：自写的格子 A* ——
            Vector2Int start = CurrentCell;
            Vector2Int goal = AI.NavGrid.WorldToCell(Target.position);

            // 目标格可能不可走：玩家贴着墙站、或跨在门框/两个格子的交界上。
            // 这种时候硬寻路会直接失败 → 把终点吸附到附近最近的可走格。
            if (!_navGrid.IsWalkable(goal) && !TryFindNearestWalkable(goal, 3, out goal)) return;

            if (_astar == null) _astar = new AI.AStarGrid();
            _legacyCellPath.Clear();
            if (!_astar.FindPath(_navGrid, start, goal, _legacyCellPath)) return;

            // 格子坐标 → 世界坐标（CellToWorld 含格子中心偏移）
            for (int i = 0; i < _legacyCellPath.Count; i++)
                _path.Add(AI.NavGrid.CellToWorld(_legacyCellPath[i]));

            _hasPath = _path.Count > 1;
            _pathIndex = _hasPath ? 1 : 0;
        }

        /// <summary>在以 center 为中心、radius 格的方形范围内，由内向外找第一个可走格</summary>
        private bool TryFindNearestWalkable(Vector2Int center, int radius, out Vector2Int result)
        {
            result = center;
            if (_navGrid == null) return false;

            for (int r = 0; r <= radius; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;   // 只看第 r 圈
                        var c = new Vector2Int(center.x + dx, center.y + dy);
                        if (!_navGrid.IsWalkable(c)) continue;
                        result = c;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 卡墙检测（网格寻路的必要兜底）：清路径 + 立刻重算 + 侧向脱困一小段时间。
        ///
        /// ⚠️ 这里曾经有个严重 bug，务必看懂再改：
        ///   原实现用"每帧位移 &gt; 0.01"判断，但 <b>每帧位移 = 速度 / 帧率</b>。
        ///   在 moveSpeed=2 时，这等价于"只有帧率低于 200fps 才不算卡住"。
        ///   而本工程 QualitySettings.vSyncCount = 0（垂直同步关闭），编辑器里场景又简单，
        ///   帧率轻易超过 200 → 每帧位移 &lt; 0.01 → <b>每 0.5 秒都误判一次"卡住"</b> →
        ///   触发侧向脱困，而"垂直于运动方向"永远是<b>轴对齐</b>的 →
        ///   表现成"敌人只会上下左右走、不走斜线，而且明明周围没有墙"。
        ///   （这个 bug 在手机上永远不会出现 —— 30~60fps 时每帧位移远大于阈值。）
        ///
        /// 现在改成两个与帧率无关的量：
        ///   1) 每帧累加"实际走过的路程"——用逐帧位移长度求和，而不是起止点直线距离，
        ///      这样敌人绕着玩家来回摆也不会被误判成卡住
        ///   2) 只在确实有速度时累加"本该走的路程"，避免"有意停下"被误判
        ///   每 stuckCheckWindow 秒结算一次：实际路程不足期望的 40% 才算卡住。
        /// </summary>
        private void CheckStuck()
        {
            if (_rb == null) return;

            // —— 逐帧累加（Update 每帧调一次，与帧率无关）——
            Vector2 step = (Vector2)(transform.position - _lastStuckCheckPos);
            _lastStuckCheckPos = transform.position;
            _stuckTraveled += step.magnitude;

            // 只在"确实想动"时累加期望路程
            if (_rb.velocity.sqrMagnitude > 0.0001f)
                _stuckExpected += moveSpeed * Time.deltaTime;

            if (Time.time < _stuckWindowEndTime) return;

            // —— 窗口结算 ——
            float expected = _stuckExpected;
            float traveled = _stuckTraveled;
            _stuckWindowEndTime = Time.time + stuckCheckWindow;
            _stuckExpected = 0f;
            _stuckTraveled = 0f;

            if (expected < 0.1f) return;                 // 窗口内几乎没想动 → 不判定
            if (traveled >= expected * 0.4f) return;     // 走了四成以上 → 正常

            // —— 确认卡住 ——
            _hasPath = false;
            _nextRepathTime = 0f;    // 下一帧立刻重算
            _path.Clear();
            _pathIndex = 0;

            BeginSidestep();
        }

        /// <summary>
        /// 侧向脱困：垂直于当前移动方向推一小段时间，避免在墙角反复顶。
        /// 注意：这个方向必然是轴对齐的（垂直于一维主导的运动方向），
        /// 所以它**只能在真的被墙卡住时触发** —— 否则会让敌人看起来"只会走上下左右"。
        /// </summary>
        private void BeginSidestep()
        {
            Vector2 baseDir = _rb != null && _rb.velocity.sqrMagnitude > 0.0001f
                ? _rb.velocity.normalized
                : Vector2.right;

            // 取垂直方向，随机选一侧
            Vector2 perp = new Vector2(-baseDir.y, baseDir.x);
            if (Random.value < 0.5f) perp = -perp;

            _sidestepDir = perp;
            _sidestepUntil = Time.time + 0.25f;

            // 诊断日志用 Info 而不是 Warn（AGENTS.md §11.2）：
            //   · Info 挂了 [Conditional("DEBUG")] → Release 包里连字符串拼接一起被编译期剔除
            //   · Warn 不会被剔除，而且 Unity 对每条 Warning 都会抓一次托管栈回溯（StackTraceUtility）
            //     —— 那是真实的 CPU + GC 代价
            // 这条日志一局里可能打上百次（s_nextDebugTime 是 static，全局每秒一次），所以必须走 Info。
            if (debugPath)
                Core.Log.Info($"[PathDebug] {name} 判定卡住 → 侧向脱困 dir=({_sidestepDir.x:F2},{_sidestepDir.y:F2})"
                            + "；若周围明明没有墙却频繁出现，说明卡住判定过于敏感");
        }

        /// <summary>
        /// 在 origin 周围 radius 格内随机取一个<b>可走格中心</b>，供巡逻选点。
        /// 为什么不直接用 Random.insideUnitCircle：那样随机出来的点经常落在墙里，
        /// 敌人会一路顶着墙走到超时才换点。
        /// </summary>
        public bool TryGetRandomPointNear(Vector3 origin, float radius, out Vector3 point)
        {
            point = origin;
            if (_navGrid == null)
            {
                point = origin + (Vector3)(Random.insideUnitCircle * radius);
                return true;
            }

            Vector2Int oc = AI.NavGrid.WorldToCell(origin);
            int r = Mathf.Max(1, Mathf.RoundToInt(radius));

            // 最多试 12 次，避免在密集墙体附近死循环
            for (int i = 0; i < 12; i++)
            {
                var c = new Vector2Int(oc.x + Random.Range(-r, r + 1), oc.y + Random.Range(-r, r + 1));
                if (!_navGrid.IsWalkable(c)) continue;
                point = AI.NavGrid.CellToWorld(c);
                return true;
            }
            return false;
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
            // 修复：先查 IsDead 防止重复伤害（死亡动画期间敌人还在被打，HP 会变负数）
            if (IsDead) return;
            Hp -= damage;
            if (IsDead) OnDied();
            else Fsm.ChangeToHit();
        }

        /// <summary>
        /// 受击（带攻击方信息，用于元素附着策划案 §3.2 + 受击方向反馈）。
        /// 修复：同样查 IsDead 防止重复伤害 + 元素印记重复附着。
        /// </summary>
        public void TakeDamage(float damage, CryptWalker.HotUpdate.Combat.WeaponBase attacker)
        {
            if (IsDead) return;
            // 元素印记附着（策划案 §3.2：每次攻击附元素 → 给敌人挂印记）
            if (attacker != null && attacker.ElementType != Data.EElementType.None)
            {
                var marks = GetComponent<CryptWalker.HotUpdate.Combat.EnemyElementMarks>();
                if (marks != null)
                {
                    bool added = marks.AddMark(attacker.ElementType);
                    if (!added) Core.Log.Info($"[EnemyBase] {name} 第 3 元素 {attacker.ElementType} 拒绝（无法与剩余组成有效反应）");
                }
            }
            Hp -= damage;
            if (IsDead) OnDied();
            else Fsm.ChangeToHit();
        }

        /// <summary>
        /// 统一的死亡出口 —— 两个 TakeDamage 重载都必须走这里，
        /// 否则"死亡时该做的事"会漏掉一处（原来就是两处各写一遍，结果两边都漏了广播）。
        ///
        /// 修复背景：GameEvent.EnemyDied 在枚举里定义了，但全工程没有一处 Raise，
        /// 所以击杀一直是"什么都不发生"（不掉金币、不计击杀、回血护符不触发）。
        /// </summary>
        private void OnDied()
        {
            Fsm.ChangeToDeath();

            // 击杀回调（策划 §4.2 回血护符）。EnemyBase 的注释一直写着"由 EnemyBase.Death 通知玩家"，
            // 但从来没调过 —— 顺手补上，否则那个遗物永远不会触发。
            CryptWalker.HotUpdate.Entity.Player.PlayerController.Instance?.OnEnemyKilled();

            // 击杀广播（payload: 敌人实例）→ RunWallet 按 CoinDrop 加金币 → CoinChanged → HUD 金币数字
            Core.Log.Info($"[Enemy] {name} 被击杀，掉落金币 {CoinDrop}");
            Core.EventBus.Raise(Core.GameEvent.EnemyDied, this);
        }

        /// <summary>
        /// 应用击退（策划案 §3.1 击退方向：跟随攻击来袭反方向；策划案 §9 命中反馈方向）。
        ///
        /// 修复说明（原来的实现是坏的）：
        ///   原来写的是 `_rb.velocity += velocity`，但 FSM 每个状态每帧都会用
        ///   MoveTowardPoint / StopMove 重设 _rb.velocity，所以击退在下一帧就被覆盖了 ——
        ///   ElementReactionTrigger 里"过载爆发击退"实际几乎看不到效果。
        ///   现在改为写入独立的 _knockbackVelocity 通道，由 MoveTowardPoint / StopMove
        ///   在统一出口里叠加，并按 knockbackDecay 平滑衰减。
        ///
        /// duration 的用法：
        ///   反推所需衰减率 = 初速 / duration，再和 knockbackDecay 取较大者作为下限。
        ///   这样既能保证"在 duration 内平滑衰减完"，又留着 knockbackDecay 这个
        ///   策划可调的手感旋钮（调大 → 击退更干脆、距离更短）。
        /// </summary>
        public void ApplyKnockback(Vector2 velocity, float duration)
        {
            if (_rb == null) return;

            _knockbackVelocity += velocity;

            float speed = _knockbackVelocity.magnitude;
            if (speed <= 0.0001f) return;

            duration = Mathf.Max(duration, 0.05f);          // 防止除 0
            float neededRate = speed / duration;            // 在 duration 内衰减完所需的最小衰减率
            _knockbackDecayRate = Mathf.Max(knockbackDecay, neededRate);
        }

        /// <summary>
        /// 致盲（策划 §3.2 蒸汽爆炸："致盲 2s 攻击 50% Miss"）。
        /// 致盲期间敌人的攻击 50% 概率不命中（EnemyFSM.PerformAttack 里 Roll 一次）。
        /// </summary>
        public void ApplyBlind(float duration)
        {
            _blindExpireTime = Mathf.Max(_blindExpireTime, Time.time + duration);
            Core.Log.Info($"[EnemyBase] {name} 致盲 {duration}s 至 {_blindExpireTime:F1}");
        }

        /// <summary>
        /// 硬直/打断施法（策划 §3.2 过载爆发："打断施法"）。
        /// 硬直期间敌人 FSM 不能从 Hit/Attack 状态转换（被打断后强制进入 Hit 状态）。
        /// 当前简化：直接转换到 Hit 状态（策划 §5.3 Hit 硬直 0.2s + duration 参数）。
        /// </summary>
        public void ApplyStun(float duration)
        {
            if (!IsDead)
            {
                Fsm.ChangeToHit();
                _stunExpireTime = Mathf.Max(_stunExpireTime, Time.time + duration);
                Core.Log.Info($"[EnemyBase] {name} 硬直 {duration}s");
            }
        }
    }
}
