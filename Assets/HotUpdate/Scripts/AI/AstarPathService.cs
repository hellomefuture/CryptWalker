using System.Collections.Generic;
using System.Threading.Tasks;
using Pathfinding;
using UnityEngine;

namespace CryptWalker.HotUpdate.AI
{
    /// <summary>
    /// A* Pathfinding Project（4.2.17 免费版）桥接层 —— 热更侧唯一接触插件的入口。
    ///
    /// ===== 为什么要有这一层 =====
    ///   1) 插件是 **AOT 程序集**（`Assets/AstarPathfindingProject` 自带 asmdef，
    ///      由 IL2CPP 编进包），热更层改不了它。调用点集中在这一个文件，
    ///      将来换版本 / 换插件只动这里。
    ///   2) HybridCLR 的 AOT 裁剪靠 `link.xml`（由 `Generate/All` 扫描热更程序集自动生成）。
    ///      调用点越集中，生成的 preserve 列表越干净、越不容易漏。
    ///
    /// ===== 分工（方案 B：只借插件的"算路径"，移动仍自写）=====
    ///   插件负责：建图 → 扫描 → A* 搜索
    ///   自写负责：沿路径移动、卡墙检测、侧向脱困（`EnemyBase` 里那套一行没动）
    ///
    /// ===== 两个必须记住的约束 =====
    ///   · **nodeSize 必须是 1**：要和 `NavGrid` 的"1 格 = 1 世界单位"对齐。
    ///     两套系统都要用格子坐标（LOS 判定、目标格吸附），对不上就会错格。
    ///   · **图必须在所有墙都画完之后才扫**：门洞是"先铺满墙、再把那几格挖掉"挖出来的，
    ///     画到一半去扫会把门洞当墙 → 房间之间不通。
    /// </summary>
    public static class AstarPathService
    {
        /// <summary>一格 = 1 世界单位（必须与 NavGrid 一致）</summary>
        private const float NodeSize = 1f;

        /// <summary>
        /// "贴墙"的判定半径（格）。1 = 自己或四邻接里有墙就算贴墙。
        /// 判定结果不是把格子变不可走，而是【加惩罚】—— 见 <see cref="WallHugPenalty"/>。
        /// </summary>
        private const int WallClearance = 1;

        /// <summary>
        /// 贴墙格的惩罚值。插件文档（GraphNode.cs:70）：1000 = 多走 1 世界单位的代价，
        /// 也就是【= 多绕一格的代价】。
        ///
        /// 为什么用惩罚而不是把贴墙格设为不可走：
        ///   房间内部空间宽裕 → A* 会为了省这 1000 而走中间，不擦墙 ✓
        ///   走廊只有 1 格可走（3×3 的走廊里上下两行是墙）→ 绕不开 → 照走 ✓ 连通性不受影响 ✓
        ///   （早先直接设为不可走，结果 420 个贴墙格被排除，其中包含【全部走廊】→ 房间之间不连通 ✗）
        /// </summary>
        private const uint WallHugPenalty = 1000;

        /// <summary>地图四周多留几格，避免贴边的点找不到最近节点</summary>
        private const int BoundsPadding = 4;

        /// <summary>
        /// 墙所在的层。
        /// 地牢生成时墙 Tilemap 没有指定 layer → 落在 **Default(0)**；
        /// 地板没有 Collider，玩家/敌人在 Player(8)/Enemy(9) 层，所以只留 Default 不会误判。
        /// 将来给墙单独建一层，把这里改成对应掩码即可。
        /// </summary>
        private static readonly LayerMask WallMask = 1 << 0;

        // —— 诊断：寻路在主线程上到底阻塞了多久 ——
        private static double s_blockedMs;
        private static int s_pathCount;
        private static float s_nextPathReport;

        private static AstarPath _astar;
        private static GridGraph _graph;
        private static bool _justCreated;   // 本帧刚 AddComponent → 要让它跑完 Start 才能配图
        private static bool _scanned;       // 图扫过一遍才算真能用（没扫就寻路会一路报错）

        /// <summary>可用 = 对象在 + 网格在 + 已经扫描成功</summary>
        public static bool IsReady => _astar != null && _graph != null && _astar.data != null && _scanned;

        /// <summary>建图（只做一次）。返回是否可用。</summary>
        private static bool EnsureGraph()
        {
            // 注意这里不能判 IsReady —— 那只在扫描成功后为真，
            // 否则每次扫描前都会当成"还没建图"重跑一遍配置。判"对象已建"即可。
            if (_astar != null && _graph != null) return true;

            // 场景里已经有 AstarPath 就用它（想手动可视化调参的话，自己摆一个就行）；
            // 没有就运行时建一个 —— 这样不用手改场景和 prefab。
            if (AstarPath.active != null)
            {
                _astar = AstarPath.active;
            }
            else
            {
                var go = new GameObject("AstarPath (runtime)");
                _astar = go.AddComponent<AstarPath>();
                _justCreated = true;
            }

            if (_astar == null || _astar.data == null)
            {
                Core.Log.Error("[Astar] AstarPath 创建失败或 data 为空，寻路不可用");
                return false;
            }

            // 关掉插件的路径日志。
            // 默认是 PathLog.Normal（AstarPath.cs:175）→ 【每算一条路径】就打多行
            // "Path Completed ..."：实测一局刷了 915 行，而且是在寻路线程上打的（栈回溯也跟着刷）。
            // 只保留出错时的日志 —— 真出问题才看得到，平时 Console 是干净的。
            _astar.logPathResults = PathLog.OnlyErrors;

            // 复用已有的 GridGraph；没有才新建。
            // （不能无条件 AddGraph —— 那会再挂一张图，两张网格同时参与搜索）
            foreach (var g in _astar.data.graphs)
            {
                if (g is GridGraph gg) { _graph = gg; break; }
            }
            if (_graph == null)
                _graph = _astar.data.AddGraph(typeof(GridGraph)) as GridGraph;

            if (_graph == null)
            {
                Core.Log.Error("[Astar] GridGraph 创建失败");
                return false;
            }

            ConfigureGraph(_graph);
            Core.Log.Info("[Astar] GridGraph 已就绪（2D / 8 方向 / 碰撞掩码=Default）");
            return true;
        }

        private static void ConfigureGraph(GridGraph gg)
        {
            // ⚠️⚠️ 这一行必须最先设，漏了它整个寻路都是错的。
            //
            // GridGraph 默认铺在【XZ 平面】（它是为 3D 横放设计的，见 GridGenerator.cs:704
            // "nodes are laid out in the XZ plane"）→ 节点存的是 (x, 0, z)，
            // 也就是说 **每个节点的 world.y 恒等于 0**。
            //
            // 后果：用地板坐标去查可走性时，8541 个节点全都落在同一行上 ✗
            //       → 可走性彻底错乱 → A* 不知道墙在哪 → 敌人直接撞墙卡住。
            //       （实测症状：校正后"可走节点 5183/8541"，既不等于 8541 也不等于权威值 1587）
            //
            // 设成 2D 后图落在 XY 平面，节点坐标才能和 NavGrid 的 (x, y) 一一对应。
            gg.is2D = true;

            gg.nodeSize = NodeSize;
            gg.neighbours = NumNeighbours.Eight;   // 8 方向，与原 NavGrid 一致
            gg.cutCorners = false;                 // 不允许斜穿墙角（地牢里会"钻角"）

            gg.collision.use2D = true;             // 走 Physics2D
            gg.collision.mask = WallMask;
            gg.collision.diameter = 1f;

            // ⚠️ 关键：关掉插件自己的碰撞扫描。
            //
            // 因为可走性【以 NavGrid 为准】（见 ApplyWalkabilityFromNavGrid）——
            // NavGrid 是从地板 Tilemap 反查出来的，比"有没有 2D 碰撞体"准确得多
            // （碰撞体判不出"既没地板也没墙"的空地）。
            // 既然扫描结果马上会被整体覆盖，那 8541 个节点各做一次物理查询就纯属浪费：
            // 实测开着是 282 ms，关掉后只是分配节点数据，快一个数量级。
            //
            // 2D 没有"地面高度"可测，heightCheck 也必须关（否则所有节点判成"没探到地面"）。
            gg.collision.collisionCheck = false;
            gg.collision.heightCheck = false;
        }

        /// <summary>
        /// 按地牢的实际范围配置图并扫描。**分帧**：每推进一步让出一帧，
        /// 大图不会把主线程卡成一顿（和 DungeonGenerator 的分帧是同一套思路）。
        /// </summary>
        public static async Task<bool> ScanAsync(Bounds worldBounds, NavGrid navGrid)
        {
            if (!EnsureGraph()) return false;

            // 刚 AddComponent 出来的 AstarPath 还要跑完它自己的 Start 才稳定，先让一帧
            if (_justCreated)
            {
                _justCreated = false;
                await Task.Yield();
            }

            int width = Mathf.CeilToInt(worldBounds.size.x / NodeSize) + BoundsPadding * 2;
            int depth = Mathf.CeilToInt(worldBounds.size.y / NodeSize) + BoundsPadding * 2;

            _graph.SetDimensions(width, depth, NodeSize);
            _graph.center = new Vector3(worldBounds.center.x, worldBounds.center.y, 0f);

            Core.Log.Info($"[Astar] 开始扫描 {width}×{depth} 格（{width * depth} 节点）" +
                          $"  center=({_graph.center.x:F1},{_graph.center.y:F1})");

            // ⚠️ 免费版限制（见 AstarPath.cs:1694）：
            //     ScanAsync 的迭代必须【在同一帧内】跑完 —— 只要某个 MoveNext 之间跨了帧，
            //     Time.frameCount 一变就抛 "Async scanning can only be done in the pro version"。
            //     所以这里【不能】用"每步 yield 一帧"的分帧写法；
            //     直接调同步的 Scan() —— 它内部就是"不 yield 地遍历 ScanAsync"，正是免费版允许的用法。
            //     代价：主线程会卡一下（下面会打出实际毫秒数，真太久再想办法拆）。
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _astar.Scan();
            }
            catch (System.Exception e)
            {
                // 扫描失败【绝不能】拖垮地牢生成：敌人会退回自写的 A*，游戏照常能玩
                Core.Log.Error($"[Astar] 扫描失败，寻路退回自写 A*：{e.Message}");
                _scanned = false;
                return false;
            }
            sw.Stop();

            // 关键一步：用 NavGrid（从地板 Tilemap 反查出来的权威可走性）校正插件的节点。
            // 插件只判断"节点上有没有 2D 碰撞体"，所以房间之间【既没地板也没墙】的空地
            // 会被判成可走 ✗ —— 敌人能从门溜到房间外面绕路，绕过走廊。
            ApplyWalkabilityFromNavGrid(navGrid);

            int walkable = 0, total = 0;
            // 用 Func 重载（返回 true = 继续遍历），避免依赖 Action 重载是否存在
            _graph.GetNodes(n => { total++; if (n.Walkable) walkable++; return true; });

            _scanned = true;
            // 注：对齐自检（与 NavGrid 权威值比对）与贴墙惩罚的统计都在
            //     ApplyWalkabilityFromNavGrid 里输出。这里的可走数应当与 NavGrid 权威值相等。
            Core.Log.Info($"[Astar] 扫描完成（{sw.ElapsedMilliseconds} ms，主线程一次做完），" +
                          $"可走节点 {walkable}/{total}");
            return true;
        }

        /// <summary>
        /// 同步算一条路径，结果写入 <paramref name="result"/>（世界坐标路点，含起点）。
        ///
        /// 为什么用同步而不是回调：
        ///   `EnemyBase.Repath()` 原本就是"算完立刻能用"的语义，同步能保持它一行不改；
        ///   而且调用点本身有重算节流（repathInterval），代价可控。
        ///
        /// 为什么不传 callback：路径计算**不会回调到热更代码**，
        ///   跨程序集（AOT→热更委托）和跨线程的坑一次都不碰。
        ///
        /// 为什么不 Claim/Release：按插件文档（Path.Release 注释），
        ///   不 claim 的路径**不会被池化**，直接交给 GC —— 安全，只是少省一点分配。
        ///   将来想做到零 GC，照 `Seeker.OnPathComplete` 的写法加 `Claim(this)` + `Release(this)` 即可。
        /// </summary>
        /// <summary>
        /// 用 NavGrid 的可走性覆盖插件节点。
        ///
        /// 为什么必须做：插件只判断"节点位置上有没有 2D 碰撞体" ✗
        ///   → 房间之间【既没地板也没墙】的空地会被判成可走
        ///   → 敌人能从门溜出去、贴着房间外围绕到另一个门进去，绕过走廊
        /// NavGrid 是从地板 Tilemap 反查出来的，才是"人能站的地方"的权威答案。
        ///
        /// 用 AddWorkItem + FlushWorkItems 同步执行 —— 这是插件自己的图更新机制
        /// （GraphUpdateObject 内部走的就是它），不依赖任何私有/未公开 API。
        /// </summary>
        private static void ApplyWalkabilityFromNavGrid(NavGrid navGrid)
        {
            if (navGrid == null || navGrid.WalkableCount == 0)
            {
                Core.Log.Warn("[Astar] NavGrid 为空，跳过可走性校正（敌人可能走到房间外）");
                return;
            }

            int changed = 0, inRange = 0, alignedTrue = 0, hugWallTrue = 0;

            _astar.AddWorkItem(() =>
            {
                _graph.GetNodes(n =>
                {
                    Vector2Int c = NavGrid.WorldToCell((Vector3)n.position);

                    // 自己判范围，不依赖 NavGrid.IsWalkable 对越界的处理：
                    // 图的包围盒比地板大（四周还留了几格 padding），那些格必须判为不可走。
                    bool inside = c.x >= navGrid.MinX && c.x < navGrid.MinX + navGrid.Width
                               && c.y >= navGrid.MinY && c.y < navGrid.MinY + navGrid.Height;

                    bool aligned = inside && navGrid.IsWalkable(c);
                    if (inside) inRange++;
                    if (aligned) alignedTrue++;

                    // 贴墙格（周围 WallClearance 格内有墙）→ 保留【可走】，但加惩罚。
                    //
                    // 为什么必须处理（实测症状："卡一小会，然后自己脱困"）：
                    //   路径是沿【格中心】走的，而敌人碰撞体宽约 1 格 →
                    //   站在紧贴墙的那一格时，碰撞体边缘正好【擦着墙面】→
                    //   物理引擎持续把它往外推 → 卡住 → 靠 EnemyBase 的侧向脱困救回来。
                    //
                    // 为什么用惩罚而不是设为不可走：走廊只有 1 格是地板（3×3 里上下两行是墙），
                    // 直接排除会把【整条走廊】堵掉 → 房间之间不连通 ✗
                    // 加惩罚则是"能绕就绕、绕不开照走"，两边都保住 ✓
                    bool hugWall = aligned && !HasClearance(navGrid, c);
                    if (hugWall) hugWallTrue++;

                    if (n.Walkable != aligned) changed++;
                    n.Walkable = aligned;
                    n.Penalty = hugWall ? WallHugPenalty : 0u;
                    return true;
                });
            });
            _astar.FlushWorkItems();

            // 自检：对齐结果必须与 NavGrid 权威值【完全相等】——
            // 这是"图和地板严丝合缝"的证明（早先就是它抓出了 GridGraph 的 XZ 朝向问题）。
            string verdict = alignedTrue == navGrid.WalkableCount ? "✓ 一致" : "✗ 不一致！";
            Core.Log.Info($"[Astar] 校正自检：范围内节点 {inRange}；与地板对齐 {alignedTrue}" +
                          $"（NavGrid 权威值 {navGrid.WalkableCount} {verdict}）；改动 {changed} 个");
            Core.Log.Info($"[Astar] 贴墙惩罚：{hugWallTrue} 格 +{WallHugPenalty}（≈多绕一格的代价）" +
                          $" —— 房间内能绕就绕（不擦墙），走廊绕不开照走（连通性不受影响）");
        }

        /// <summary>
        /// 以 c 为中心的十字（自己 + 上下左右各 WallClearance 格）是否全是地板。
        /// 不算对角：真正的"钻角"已经由 cutCorners=false 挡掉了；
        /// 而算上对角的话，3 格宽走廊里唯一可走的那格也会被判成贴墙，惩罚就失去区分度了。
        /// </summary>
        private static bool HasClearance(NavGrid navGrid, Vector2Int c)
        {
            for (int dx = -WallClearance; dx <= WallClearance; dx++)
                for (int dy = -WallClearance; dy <= WallClearance; dy++)
                    if (!navGrid.IsWalkable(c.x + dx, c.y + dy)) return false;
            return true;
        }

        /// <summary>
        /// 同步算一条路径，结果写入 <paramref name="result"/>（世界坐标路点，含起点）。
        ///
        /// 为什么用同步而不是回调：
        ///   `EnemyBase.Repath()` 原本就是"算完立刻能用"的语义，同步能保持它一行不改；
        ///   而且调用点本身有重算节流（repathInterval），代价可控。
        ///
        /// 为什么不传 callback：路径计算【不会回调到热更代码】，
        ///   跨程序集（AOT → 热更委托）和跨线程的坑一次都不碰。
        ///
        /// 为什么不 Claim/Release：按插件文档（Path.Release 注释），
        ///   不 claim 的路径不会被池化，直接交给 GC —— 安全，只是少省一点分配。
        ///   将来想零 GC，照 `Seeker.OnPathComplete` 的写法加 `Claim(this)` + `Release(this)` 即可。
        /// </summary>
        public static bool TryFindPath(Vector3 from, Vector3 to, List<Vector3> result)
        {
            result.Clear();
            if (!IsReady) return false;

            // 用 Stopwatch.GetTimestamp（静态方法）而不是 new Stopwatch()：后者每次会分配对象，
            // 而本方法是被敌人反复调用的热路径。
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool ok = false;
            try
            {
                var p = ABPath.Construct(from, to, null);
                AstarPath.StartPath(p);
                p.BlockUntilCalculated();   // ← 同步等结果：这一句会在主线程上阻塞

                if (!p.error && p.vectorPath != null && p.vectorPath.Count > 0)
                {
                    // vectorPath 是 path 内部的列表（会被池化复用），必须拷出来
                    result.AddRange(p.vectorPath);
                    ok = true;
                }
            }
            catch (System.Exception e)
            {
                // 图没扫完 / 插件未初始化 → 返回 false，由调用方退回到自写的 AStarGrid
                Core.Log.Warn($"[Astar] 寻路异常，本帧放弃（调用方会退回自写寻路）：{e.Message}");
            }
            finally
            {
                s_blockedMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0
                               / System.Diagnostics.Stopwatch.Frequency;
                s_pathCount++;
                ReportPathCostIfDue();
            }
            return ok;
        }

        /// <summary>
        /// 每 2 秒汇报一次"寻路在主线程上总共阻塞了多久"。
        ///
        /// 为什么需要它：TryFindPath 用的是 BlockUntilCalculated（同步等结果）——
        /// 单个请求很快，但敌人一多、同一帧里连等十几次就会明显掉帧。
        /// 这行日志能直接判断"卡"是不是寻路造成的，不用靠猜：
        ///   平均 &lt; 0.5 ms/次  → 不是它，去别处找
        ///   平均 几 ms 以上      → 就是它，改成异步回调版
        /// </summary>
        private static void ReportPathCostIfDue()
        {
            float now = Time.unscaledTime;
            if (s_nextPathReport <= 0f) { s_nextPathReport = now + 2f; return; }
            if (now < s_nextPathReport) return;

            if (s_pathCount > 0)
                Core.Log.Info($"[Astar] 最近 2 秒：寻路 {s_pathCount} 次，主线程等待共 {s_blockedMs:F1} ms" +
                              $"（平均 {s_blockedMs / s_pathCount:F2} ms/次）");

            s_pathCount = 0;
            s_blockedMs = 0;
            s_nextPathReport = now + 2f;
        }
    }
}
