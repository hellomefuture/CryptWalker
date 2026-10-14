using System.Collections.Generic;
using System.Threading.Tasks;
using CryptWalker.Main.Data;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CryptWalker.HotUpdate.GameFlow.Dungeon
{
    /// <summary>
    /// 程序化关卡生成器（方案 B：Grid-based 完全程序化，参考元气骑士）。
    ///
    /// 算法骨架（答辩可讲）：
    ///   1. 在 DungeonRoot 下挂 Grid 组件（CellSize=1×1），所有 Tilemap 共享同一 Grid
    ///   2. 创建 GRID_SIZE×GRID_SIZE 二维网格（默认 5×5）
    ///   3. Start 房放在中心 cell
    ///   4. 随机游走 N 步，每步从已放置房间随机选一个 + 随机选一个方向，
    ///      目标 cell 有效（边界内、未占用）则放置 Combat 房，建立邻居关系
    ///   5. 末端房（只有 1 个邻居）扩展一个邻居 cell 放置 End 房
    ///   6. 程序化绘制所有房间（floor 填充 + 边缘 wall + Exit 处挖 3 格宽门洞）
    ///   7. 程序化绘制所有走廊（每对相邻 cell 之间画 corridorWidth 长的直线 + 两边墙）
    ///   8. Combat 房中心刷敌人
    ///   9. Start 房中心刷玩家 + Cinemachine 跟随
    ///
    /// 固定种子 → 同一关卡布局可复现（策划案要求）。
    /// Grid-based 布点 → 房间不会重叠，相邻 cell 必相邻 → 走廊必连通。
    /// </summary>
    public static class DungeonGenerator
    {
        /// <summary>
        /// 主入口：生成整个关卡，返回 Start 房数据。
        ///
        /// ⚠️ 【分帧生成】整个地牢（11 间房 + 走廊 + NavGrid + 敌人）一口气做完会卡住主线程几百毫秒。
        /// 所以每画完一间房 / 一段走廊 / 一房的敌人就让出一帧（Task.Yield），把开销摊到几十帧。
        ///
        /// 让出期间玩家可能按 ESC 退回主菜单（GameState.OnExit → DestroyDungeonRoot），
        /// 所以每次让出后都必须确认根节点还活着 —— 否则会往已销毁的父节点下继续创建物体，
        /// 那些物体会掉到场景根、永远没人回收（和之前修过的"每局泄漏一整套地牢"是同一类 bug）。
        /// </summary>
        public static async Task<RoomData> GenerateAsync(Transform parent, LevelConfig config,
            System.Action<float> onProgress = null)
        {
            if (config == null)
            {
                Debug.LogError("[DungeonGenerator] LevelConfig 为空，无法生成关卡");
                return null;
            }
            if (config.roomTileset == null || config.roomTileset.Floor == null || config.roomTileset.Wall == null)
            {
                Debug.LogError("[DungeonGenerator] LevelConfig.roomTileset 缺少 Floor / Wall 资源，无法生成关卡");
                return null;
            }

            // 进度回调（给加载界面用）。没传就什么都不做。
            // 各阶段大致权重：布点 0.15 → 画房间 0.15~0.45 → 走廊 0.6 → NavGrid 0.7 → A* 扫图 0.9 → 刷怪 0.98
            void Report(float p) => onProgress?.Invoke(Mathf.Clamp01(p));

            var rnd = new System.Random(config.seed);
            int gridSize = config.gridSize;
            var tileFloor = config.roomTileset.Floor;
            var tileWall  = config.roomTileset.Wall;

            // 1. DungeonRoot（含 Grid 组件，所有 Tilemap 共享）—— 复用 parent 或创建子物体
            var dungeonRoot = EnsureDungeonRoot(parent);

            // 2. 网格（gridSize × gridSize）
            var grid = new GridRoom[gridSize, gridSize];
            var placed = new List<GridRoom>();

            // 3. Start 房放在中心
            int cx = gridSize / 2;
            int cy = gridSize / 2;
            var start = CreateRoom(config, RoomType.Start, new Vector2Int(cx, cy), dungeonRoot.transform);
            grid[cx, cy] = start;
            placed.Add(start);

            // 4. 随机游走放置 Combat 房
            int combatTarget = Mathf.Max(1, config.roomCount - 2);  // 减去 Start 和 End
            int attempts = 0;
            int maxAttempts = combatTarget * 50;
            while (placed.Count < 1 + combatTarget && attempts < maxAttempts)
            {
                attempts++;
                // 随机选一个已放置房作为扩展起点
                var current = placed[rnd.Next(placed.Count)];
                // 随机选一个方向（4 选 1）
                var dir = PickRandomDirection(rnd);
                var nextGrid = current.GridPos + GridRoom.Offset(dir);

                // 边界检查
                if (nextGrid.x < 0 || nextGrid.x >= gridSize) continue;
                if (nextGrid.y < 0 || nextGrid.y >= gridSize) continue;
                // 已占用检查
                if (grid[nextGrid.x, nextGrid.y] != null) continue;

                // 放置 Combat 房 + 建立双向连接
                var next = CreateRoom(config, RoomType.Combat, nextGrid, dungeonRoot.transform);
                grid[nextGrid.x, nextGrid.y] = next;
                ConnectRooms(current, next, dir);
                placed.Add(next);
            }

            // 5. End 房：从末端房扩展
            if (placed.Count >= 2)
            {
                GridRoom leaf = null;
                // 优先选末端房（只有 1 个邻居），保证 End 房在关卡边界
                foreach (var r in placed)
                {
                    if (r.Neighbors.Count == 1) { leaf = r; break; }
                }
                if (leaf == null) leaf = placed[placed.Count - 1];

                // 从 leaf 找一个可用方向放置 End
                var dirs = new[] { Direction.North, Direction.South, Direction.East, Direction.West };
                // 排序：先尝试远离其他房的方向
                foreach (var dir in Shuffled(dirs, rnd))
                {
                    var nextGrid = leaf.GridPos + GridRoom.Offset(dir);
                    if (nextGrid.x < 0 || nextGrid.x >= gridSize) continue;
                    if (nextGrid.y < 0 || nextGrid.y >= gridSize) continue;
                    if (grid[nextGrid.x, nextGrid.y] != null) continue;

                    var end = CreateRoom(config, RoomType.End, nextGrid, dungeonRoot.transform);
                    grid[nextGrid.x, nextGrid.y] = end;
                    ConnectRooms(leaf, end, dir);
                    placed.Add(end);
                    break;
                }
            }

            // 6. 绘制所有房间（每画完一间让出一帧 —— SetTilesBlock + RefreshAllTiles + collider 重建都在这里）
            //    这一段是总耗时的大头，所以逐间报进度，加载条才不会卡住不动。
            int drawnRooms = 0;
            foreach (var r in placed)
            {
                DrawRoom(r, tileFloor, tileWall);
                Report(0.15f + 0.30f * (++drawnRooms) / Mathf.Max(1, placed.Count));
                await Task.Yield();
                if (Aborted(dungeonRoot)) return null;
            }

            // 7. 绘制所有走廊（去重：每对相邻 cell 只画一次）
            var drawnCorridors = new HashSet<(Vector2Int, Vector2Int)>();
            int corridorCount = 0;
            foreach (var r in placed)
            {
                foreach (var exit in r.Exits)
                {
                    var fromCell = r.GridPos;
                    var toCell = fromCell + GridRoom.Offset(exit);
                    var key = MakeCorridorKey(fromCell, toCell);
                    if (!drawnCorridors.Add(key)) continue;
                    CreateCorridor(r, exit, dungeonRoot.transform,
                        tileFloor, tileWall, config.corridorWidth,
                        config.roomWidth, config.roomHeight);
                    corridorCount++;

                    await Task.Yield();
                    if (Aborted(dungeonRoot)) return null;
                }
            }

            // 7.5 构建可走性网格（供敌人 A* 寻路）——必须在【所有房间 + 走廊都画完】之后，
            //     因为门洞是"先把边缘铺满墙、再把门洞那几格 SetTile(null) 挖掉"挖出来的，
            //     画到一半去扫会得到错误的墙分布。
            Report(0.60f);   // 房间 + 走廊画完

            await Task.Yield();
            if (Aborted(dungeonRoot)) return null;
            var navGrid = BuildNavGrid(dungeonRoot);   // 全图扫描，单独占一帧
            Report(0.70f);

            // 7.6 建 A* 插件（A* Pathfinding Project）的图并扫描。
            //     时机要求：必须【所有墙 + 走廊都画完】之后 —— 门洞是"先铺满墙、再把那几格挖掉"
            //     挖出来的，画到一半去扫会把门洞当墙，房间之间就不通了。
            //     这里扫描时玩家和敌人都还没刷，所以碰撞掩码只留 Default 层就很干净。
            //     与 NavGrid 并存分工：NavGrid 管"有视线就直冲"和连通性自检，
            //     插件管真正的寻路（EnemyBase.Repath 里调）。
            await AI.AstarPathService.ScanAsync(ComputeWorldBounds(placed, config), navGrid);
            if (Aborted(dungeonRoot)) return null;
            // A* 扫图是主线程一次做几十~几百毫秒的活（实测 ~230ms），
            // 把它放在 0.7→0.9 之间，加载条正好"卡在 70% 一小会"，不会看起来像死机。
            Report(0.90f);

            // 8. 先刷玩家（Start 房中心），再刷敌人（敌人需要 Target 引用）
            await Task.Yield();
            if (Aborted(dungeonRoot)) return null;
            var playerTransform = SpawnPlayer(start, parent, config);

            // 9. 刷敌人（Combat 房中心），并注入 Target = player.transform（每房一帧）
            foreach (var r in placed)
            {
                if (r.Type != RoomType.Combat) continue;
                SpawnEnemies(r, parent, config, playerTransform, navGrid);
                await Task.Yield();
                if (Aborted(dungeonRoot)) return null;
            }

            Report(0.98f);   // 怪刷完，剩下的是自检

            // 兜底：场景里所有 EnemyBase 兜底注入 Target / NavGrid（防止个别敌人漏注入）
            // FindObjectsOfType 是全场景扫描，单独占一帧
            await Task.Yield();
            if (Aborted(dungeonRoot)) return null;
            if (playerTransform != null)
            {
                var allEnemies = Object.FindObjectsOfType<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>();
                foreach (var e in allEnemies)
                {
                    if (e.Target == null) e.Target = playerTransform;
                    if (!e.HasNavGrid) e.SetNavGrid(navGrid);
                }
                Debug.Log($"[DungeonGenerator] 兜底注入：扫描到 {allEnemies.Length} 个 EnemyBase，玩家={playerTransform.name}");
            }

            // 连通性自检：从 Start 房中心出发能走到的格子数应等于全部可走格。
            // 不相等 = 有房间成了孤岛（门洞/走廊几何有缺口），A* 必然找不到路 → 这里直接报出来。
            // （BFS 扫全图，也让它单独占一帧）
            await Task.Yield();
            if (Aborted(dungeonRoot)) return null;
            if (navGrid != null && playerTransform != null)
            {
                var startCell = CryptWalker.HotUpdate.AI.NavGrid.WorldToCell(playerTransform.position);
                int reachable = navGrid.CountReachable(startCell);
                if (reachable != navGrid.WalkableCount)
                {
                    Debug.LogWarning($"[DungeonGenerator] 地图不连通！从玩家 {startCell} 只能走到 {reachable}/{navGrid.WalkableCount} 格，" +
                                     "说明有房间/走廊没接上，敌人 A* 会找不到路。请检查门洞与走廊的绘制范围。");
                }
            }

            Debug.Log($"[DungeonGenerator] 关卡生成完成：{placed.Count} 间房 + {corridorCount} 段走廊（grid={gridSize}×{gridSize}, seed={config.seed}）");
            Report(1f);
            return new RoomData { Type = RoomType.Start, Room = start };
        }

        /// <summary>
        /// 分帧生成中每次让出一帧后调用：地牢根节点是不是已经没了（玩家中途退出战斗）。
        /// Unity 的假 null 语义正好适用 —— 被 Destroy 的对象用 == null 判定为 true。
        /// </summary>
        private static bool Aborted(GameObject dungeonRoot) => dungeonRoot == null;

        /// <summary>
        /// 所有房间（含走廊外扩）的世界包围盒 —— 给 A* 的 GridGraph 定尺寸和中心用。
        /// 房间格子是画在本地 (0,0)~(w,h) 上的（见 DrawRoom），所以世界范围就是
        /// WorldPos ~ WorldPos + (Width, Height)；走廊落在两间房之间，外扩一个走廊宽就盖住了。
        /// </summary>
        private static Bounds ComputeWorldBounds(List<GridRoom> placed, LevelConfig config)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, 0f);
            var max = new Vector3(float.MinValue, float.MinValue, 0f);
            foreach (var r in placed)
            {
                min = Vector3.Min(min, r.WorldPos);
                max = Vector3.Max(max, r.WorldPos + new Vector3(r.Width, r.Height, 0f));
            }

            float pad = config.corridorWidth;
            min -= new Vector3(pad, pad, 0f);
            max += new Vector3(pad, pad, 0f);
            return new Bounds((min + max) * 0.5f, max - min);
        }

        // ================ 房间创建 ================

        /// <summary>
        /// 确保 DungeonRoot 存在并挂有 Grid 组件：
        ///   - 如果 parent 已有 Grid 组件 → 直接复用
        ///   - 否则创建子物体 "DungeonRoot" 并挂 Grid 组件
        /// </summary>
        private static GameObject EnsureDungeonRoot(Transform parent)
        {
            // parent 为 null 时直接创建场景根物体
            if (parent == null)
            {
                var go = new GameObject("DungeonRoot");
                go.AddComponent<UnityEngine.Grid>().cellSize = new Vector3(1f, 1f, 0f);
                return go;
            }

            // parent 已有 Grid → 复用
            var existing = parent.GetComponent<UnityEngine.Grid>();
            if (existing != null) return parent.gameObject;

            // parent 缺 Grid → 创建子物体
            var child = new GameObject("DungeonRoot");
            child.transform.SetParent(parent, false);
            child.AddComponent<UnityEngine.Grid>().cellSize = new Vector3(1f, 1f, 0f);
            return child;
        }

        /// <summary>创建一个网格房间（含 Root + Floor Tilemap + Wall Tilemap + Wall Collider）</summary>
        private static GridRoom CreateRoom(LevelConfig config, RoomType type, Vector2Int gridPos, Transform parent)
        {
            var room = new GridRoom
            {
                Type = type,
                GridPos = gridPos,
                Width = config.roomWidth,
                Height = config.roomHeight,
            };

            // 世界坐标：左下角 = (gridPos - center) * cellSpan
            int cx = config.gridSize / 2;
            int cy = config.gridSize / 2;
            float cellSpanX = config.roomWidth + config.corridorWidth;
            float cellSpanY = config.roomHeight + config.corridorWidth;
            room.WorldPos = new Vector3(
                (gridPos.x - cx) * cellSpanX,
                (gridPos.y - cy) * cellSpanY,
                0f);

            // Root
            room.Root = new GameObject($"{type}_Grid({gridPos.x},{gridPos.y})");
            room.Root.transform.SetParent(parent, false);
            room.Root.transform.position = room.WorldPos;

            // Floor 子物体
            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(room.Root.transform, false);
            room.Floor = floorGo.AddComponent<Tilemap>();
            var floorRenderer = floorGo.AddComponent<TilemapRenderer>();
            floorRenderer.sortingLayerName = "Map";
            floorRenderer.sortingOrder = 0;

            // Wall 子物体（含 TilemapCollider2D）
            var wallGo = new GameObject("Wall");
            wallGo.transform.SetParent(room.Root.transform, false);
            room.Wall = wallGo.AddComponent<Tilemap>();
            room.WallCollider = wallGo.AddComponent<TilemapCollider2D>();
            var wallRenderer = wallGo.AddComponent<TilemapRenderer>();
            wallRenderer.sortingLayerName = "Map";
            wallRenderer.sortingOrder = 1;

            return room;
        }

        // ================ 房间绘制 ================

        /// <summary>
        /// 程序化绘制单个房间：
        ///   1. 填充 floor（全格子）
        ///   2. 边缘填 wall（顶/底/左/右各 1 行/列）
        ///   3. 在每个 Exit 方向挖 3 格宽门洞（corridorWidth）
        /// </summary>
        private static void DrawRoom(GridRoom room, TileBase floor, TileBase wall)
        {
            int w = room.Width;
            int h = room.Height;
            var bounds = new BoundsInt(0, 0, 0, w, h, 1);

            // 1. 填充 floor
            var floorTiles = new TileBase[w * h];
            for (int i = 0; i < floorTiles.Length; i++) floorTiles[i] = floor;
            room.Floor.SetTilesBlock(bounds, floorTiles);

            // 2. 边缘填 wall
            var wallTiles = new TileBase[w * h];
            // 默认 null（中间区域留空 → 透明，让 floor 透出来）
            for (int i = 0; i < wallTiles.Length; i++) wallTiles[i] = null;
            // 顶边（y = h - 1）和 底边（y = 0）
            for (int x = 0; x < w; x++)
            {
                wallTiles[(h - 1) * w + x] = wall;
                wallTiles[x] = wall;
            }
            // 左边（x = 0）和 右边（x = w - 1）
            for (int y = 0; y < h; y++)
            {
                wallTiles[y * w] = wall;
                wallTiles[y * w + w - 1] = wall;
            }
            room.Wall.SetTilesBlock(bounds, wallTiles);

            // 3. 挖门洞（每个 Exit 方向挖 corridorWidth 格宽，居中）
            int doorHalfWidth = Mathf.Max(1, 3 / 2);  // 3 格宽门洞 = 中心 ± 1
            int cx = w / 2;
            int cy = h / 2;
            foreach (var dir in room.Exits)
            {
                switch (dir)
                {
                    case Direction.North:
                        for (int x = cx - doorHalfWidth; x <= cx + doorHalfWidth; x++)
                            room.Wall.SetTile(new Vector3Int(x, h - 1, 0), null);
                        break;
                    case Direction.South:
                        for (int x = cx - doorHalfWidth; x <= cx + doorHalfWidth; x++)
                            room.Wall.SetTile(new Vector3Int(x, 0, 0), null);
                        break;
                    case Direction.East:
                        for (int y = cy - doorHalfWidth; y <= cy + doorHalfWidth; y++)
                            room.Wall.SetTile(new Vector3Int(w - 1, y, 0), null);
                        break;
                    case Direction.West:
                        for (int y = cy - doorHalfWidth; y <= cy + doorHalfWidth; y++)
                            room.Wall.SetTile(new Vector3Int(0, y, 0), null);
                        break;
                }
            }

            room.Floor.RefreshAllTiles();
            room.Wall.RefreshAllTiles();

            // 强制 collider 重建
            if (room.WallCollider != null)
            {
                room.WallCollider.enabled = false;
                room.WallCollider.enabled = true;
            }
        }

        // ================ 走廊绘制 ================

        /// <summary>
        /// 程序化绘制一段走廊（from 房 → to 房）。
        /// 走廊是 corridorWidth 长的直线 + 两边墙（lenX = corridorWidth，lenY = corridorWidth）。
        /// </summary>
        private static void CreateCorridor(GridRoom from, Direction dir, Transform parent,
            TileBase floor, TileBase wall, int corridorWidth, int roomWidth, int roomHeight)
        {
            bool horizontal = (dir == Direction.East || dir == Direction.West);
            int lenX, lenY;
            Vector3 worldPos;

            // 走廊长度 = 房间间距 = corridorWidth（从 from 墙边到邻居房间墙边的距离）
            int corridorLen = corridorWidth;

            // 居中计算：走廊世界中心必须 = 房间中心 cell 的世界中心 = (cx + 0.5, cy + 0.5)
            //   走廊宽 corridorWidth cell，起始 world 坐标 = 中心 - corridorWidth/2
            //   = (roomDim - corridorWidth) * 0.5f  （用 * 0.5f 强制浮点，避免 int 截断）
            // 修复：之前公式 `centerLine - corridorWidth/2f` 用 int centerLine（roomDim/2）→ 偏 0.5 cell
            if (horizontal)
            {
                lenX = corridorLen;
                lenY = corridorWidth;
                float yOffset = from.WorldPos.y + (roomHeight - corridorWidth) * 0.5f;
                if (dir == Direction.East)
                    worldPos = new Vector3(from.WorldPos.x + roomWidth, yOffset, 0f);
                else  // West
                    worldPos = new Vector3(from.WorldPos.x - corridorLen, yOffset, 0f);
            }
            else  // North / South
            {
                lenX = corridorWidth;
                lenY = corridorLen;
                float xOffset = from.WorldPos.x + (roomWidth - corridorWidth) * 0.5f;
                if (dir == Direction.North)
                    worldPos = new Vector3(xOffset, from.WorldPos.y + roomHeight, 0f);
                else  // South
                    worldPos = new Vector3(xOffset, from.WorldPos.y - corridorLen, 0f);
            }

            // 创建走廊 Root
            var corridorRoot = new GameObject($"Corridor_{from.GridPos.x},{from.GridPos.y}→{dir}");
            corridorRoot.transform.SetParent(parent, false);
            corridorRoot.transform.position = worldPos;

            // Floor
            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(corridorRoot.transform, false);
            var floorMap = floorGo.AddComponent<Tilemap>();
            var floorRenderer = floorGo.AddComponent<TilemapRenderer>();
            floorRenderer.sortingLayerName = "Map";
            floorRenderer.sortingOrder = 0;

            // Wall（含 TilemapCollider2D）
            var wallGo = new GameObject("Wall");
            wallGo.transform.SetParent(corridorRoot.transform, false);
            var wallMap = wallGo.AddComponent<Tilemap>();
            var wallCol = wallGo.AddComponent<TilemapCollider2D>();
            var wallRenderer = wallGo.AddComponent<TilemapRenderer>();
            wallRenderer.sortingLayerName = "Map";
            wallRenderer.sortingOrder = 1;

            // 填充 Floor（全矩形）
            var bounds = new BoundsInt(0, 0, 0, lenX, lenY, 1);
            var floorTiles = new TileBase[lenX * lenY];
            for (int i = 0; i < floorTiles.Length; i++) floorTiles[i] = floor;
            floorMap.SetTilesBlock(bounds, floorTiles);

            // 填充 Wall（走廊的两条长边）
            var wallTiles = new TileBase[lenX * lenY];
            for (int i = 0; i < wallTiles.Length; i++) wallTiles[i] = null;
            if (horizontal)
            {
                // 水平走廊：上下两条边（y = 0 和 y = lenY-1）
                for (int x = 0; x < lenX; x++)
                {
                    wallTiles[x] = wall;
                    wallTiles[(lenY - 1) * lenX + x] = wall;
                }
            }
            else
            {
                // 垂直走廊：左右两条边（x = 0 和 x = lenX-1）
                for (int y = 0; y < lenY; y++)
                {
                    wallTiles[y * lenX] = wall;
                    wallTiles[y * lenX + lenX - 1] = wall;
                }
            }
            wallMap.SetTilesBlock(bounds, wallTiles);

            floorMap.RefreshAllTiles();
            wallMap.RefreshAllTiles();

            // 强制 collider 重建
            wallCol.enabled = false;
            wallCol.enabled = true;
        }

        // ================ 可走性网格（A* 寻路用） ================

        /// <summary>
        /// 从最终 Tilemap 反查可走性，构建全局 NavGrid。
        ///
        /// 为什么"事后扫 Tilemap"而不是"画的时候顺手记"：
        ///   门洞是"先把房间边缘铺满墙，再把门洞那几格 SetTile(null) 挖掉"挖出来的。
        ///   画的过程中记录会漏掉"挖洞"这一步，把门洞误判成墙 → 房间之间寻路不通。
        ///   扫最终状态才是权威结果。
        ///
        /// 为什么遍历 DungeonRoot 下所有 Tilemap 而不是只用 rooms 列表：
        ///   走廊（CreateCorridor）创建的 Tilemap 没有被任何集合持有，
        ///   只扫 rooms 会漏掉全部走廊 → 每个房间都成了孤岛。
        ///   房间和走廊的结构是一致的（Root 下各挂一个 Floor + Wall），统一扫最稳。
        /// </summary>
        private static CryptWalker.HotUpdate.AI.NavGrid BuildNavGrid(GameObject dungeonRoot)
        {
            if (dungeonRoot == null) return null;

            var cells = new List<Vector2Int>(4096);
            var tilemaps = dungeonRoot.GetComponentsInChildren<Tilemap>(true);
            int floorLayerCount = 0;

            foreach (var floorMap in tilemaps)
            {
                if (floorMap.name != "Floor") continue;   // 只以 Floor 层为基准，Wall 层作为它的兄弟节点配对
                floorLayerCount++;

                // 同父节点下找名为 "Wall" 的兄弟 Tilemap
                Tilemap wallMap = null;
                var parent = floorMap.transform.parent;
                if (parent != null)
                {
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        var child = parent.GetChild(i);
                        if (child.name != "Wall") continue;
                        wallMap = child.GetComponent<Tilemap>();
                        break;
                    }
                }

                CollectWalkable(floorMap, wallMap, cells);
            }

            var nav = CryptWalker.HotUpdate.AI.NavGrid.Build(cells);
            Debug.Log($"[DungeonGenerator] NavGrid 构建完成：{floorLayerCount} 个地板层 → 可走格 {nav?.WalkableCount ?? 0}");
            return nav;
        }

        /// <summary>
        /// 收集单个地板层的可走格。
        /// 判定：Floor 有 tile 且 Wall 没有 tile（Wall 盖住 = 墙；被挖掉门洞后恢复可走）。
        /// 坐标换算用 GetCellCenterWorld 而不是 CellToWorld —— 前者返回【格子中心】，
        /// 带上了 Grid 默认 cellAnchor=(0.5,0.5) 的偏移，取整后正好得到全局格坐标。
        /// </summary>
        private static void CollectWalkable(Tilemap floorMap, Tilemap wallMap, List<Vector2Int> outCells)
        {
            if (floorMap == null) return;

            var bounds = floorMap.cellBounds;
            foreach (var pos in bounds.allPositionsWithin)
            {
                if (!floorMap.HasTile(pos)) continue;
                if (wallMap != null && wallMap.HasTile(pos)) continue;

                outCells.Add(CryptWalker.HotUpdate.AI.NavGrid.WorldToCell(floorMap.GetCellCenterWorld(pos)));
            }
        }

        // ================ 实体刷怪 ================

        /// <summary>
        /// 刷敌人并注入 Target（玩家 transform）。
        /// 每个 Combat 房刷 1~2 只敌人，位置在房间中心两侧。
        /// </summary>
        private static void SpawnEnemies(GridRoom room, Transform parent, LevelConfig config, Transform playerTransform,
                                        CryptWalker.HotUpdate.AI.NavGrid navGrid)
        {
            var enemyPrefab = config.orc1Prefab;
            if (enemyPrefab == null)
            {
                Debug.LogWarning("[DungeonGenerator] LevelConfig.orc1Prefab 为空，跳过刷敌人");
                return;
            }
            // 在房间中心刷 1~2 只敌人
            int count = Mathf.Min(2, 1 + (int)(room.GridPos.x + room.GridPos.y) % 2);
            Debug.Log($"[DungeonGenerator] SpawnEnemies @Grid({room.GridPos.x},{room.GridPos.y}) count={count} player={(playerTransform != null ? playerTransform.name : "null")}");
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = new Vector3(
                    (i == 0 ? -2f : 2f),
                    (i == 0 ? 0f : -2f),
                    0f);
                var enemyGo = Object.Instantiate(enemyPrefab, room.CenterWorld + offset, Quaternion.identity, parent);

                // 注入玩家 Target（敌人 FSM 追击/攻击的依据）
                var enemy = enemyGo.GetComponent<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>();
                if (enemy == null)
                {
                    Debug.LogWarning($"[DungeonGenerator] {enemyPrefab.name} prefab 上找不到 EnemyBase 组件！");
                    continue;
                }
                if (playerTransform == null)
                {
                    Debug.LogWarning($"[DungeonGenerator] playerTransform 为 null，敌人 {enemyGo.name} 拿不到 Target！");
                    continue;
                }
                enemy.Target = playerTransform;
                enemy.SetNavGrid(navGrid);   // 注入可走性网格 → 敌人 Chase 走 A* 而不是直线撞墙
                Debug.Log($"[DungeonGenerator] 敌人 {enemyGo.name} Target/NavGrid 已注入 → {playerTransform.name}");
            }
        }

        /// <summary>
        /// 刷玩家到 Start 房中心，并设置 Cinemachine 跟随。
        /// 返回玩家 transform（供刷敌人时注入 Target）。
        /// </summary>
        private static Transform SpawnPlayer(GridRoom startRoom, Transform parent, LevelConfig config)
        {
            if (config.playerPrefab == null)
            {
                Debug.LogWarning("[DungeonGenerator] LevelConfig.playerPrefab 为空，跳过刷玩家");
                return null;
            }
            var player = Object.Instantiate(config.playerPrefab, startRoom.CenterWorld, Quaternion.identity, parent);

            // Cinemachine 跟随：反射访问 Cinemachine 2.x 的私有字段 m_Follow / m_LookAt
            var vcType = System.Type.GetType("Cinemachine.CinemachineVirtualCameraBase, Cinemachine");
            var vc = vcType != null ? Object.FindObjectOfType(vcType) : null;
            if (vc != null)
            {
                var followField = vc.GetType().GetField("m_Follow",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var lookAtField = vc.GetType().GetField("m_LookAt",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                followField?.SetValue(vc, player.transform);
                lookAtField?.SetValue(vc, player.transform);
            }

            return player.transform;
        }

        // ================ 工具方法 ================

        private static void ConnectRooms(GridRoom a, GridRoom b, Direction dirFromAtoB)
        {
            a.Exits.Add(dirFromAtoB);
            a.Neighbors.Add(b);
            b.Exits.Add(GridRoom.Opposite(dirFromAtoB));
            b.Neighbors.Add(a);
        }

        private static Direction PickRandomDirection(System.Random rnd)
        {
            var dirs = new[] { Direction.North, Direction.South, Direction.East, Direction.West };
            return dirs[rnd.Next(dirs.Length)];
        }

        private static Direction[] Shuffled(Direction[] src, System.Random rnd)
        {
            var list = new List<Direction>(src);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
            return list.ToArray();
        }

        /// <summary>走廊去重 Key：把 (a, b) 按字典序归一化，(from, to) 与 (to, from) 返回相同 Key</summary>
        private static (Vector2Int, Vector2Int) MakeCorridorKey(Vector2Int a, Vector2Int b)
        {
            // 先比 x，再比 y，小的在前
            bool aFirst = a.x < b.x || (a.x == b.x && a.y <= b.y);
            return aFirst ? (a, b) : (b, a);
        }
    }
}