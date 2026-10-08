using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CryptWalker.HotUpdate.AI
{
    /// <summary>
    /// 可走性网格 —— 敌人 A* 寻路的底层数据结构（对应策划案 5.3 敌人 AI）。
    ///
    /// 为什么只需要【一张全局网格】（不用按房间分别寻路）：
    ///   DungeonGenerator 里所有房间/走廊的世界坐标都是【整数】——
    ///     cellSpanX = roomWidth + corridorWidth = 21 + 3 = 24（int）
    ///     room.WorldPos = (gridPos - center) * cellSpan
    ///   所以 "世界坐标向下取整" 就是全局唯一的格子坐标，天然可以统一索引。
    ///
    /// 坐标约定（全工程唯一权威，务必保持一致）：
    ///   world → cell ：FloorToInt(world.x), FloorToInt(world.y)
    ///   cell  → world：格子【中心】= (cell.x + 0.5, cell.y + 0.5)
    ///   （Unity Grid 默认 cellAnchor = (0.5, 0.5)，Tilemap 的格子中心正好落在 x + 0.5 上）
    ///
    /// 可走性判定（由 DungeonGenerator 从最终 Tilemap 反查，是权威结果）：
    ///   可走 = Floor 层有 tile 且 Wall 层没有 tile
    ///   （房间最外一圈铺满墙 → 不可走；"挖门洞"把墙 SetTile(null) 之后 → 恢复可走）
    /// </summary>
    public sealed class NavGrid
    {
        // 密集数组：索引 = (y - _minY) * _width + (x - _minX)
        // 用密集数组而不是 HashSet：A* 里按整数索引寻址，零哈希开销、对 CPU 缓存友好
        private readonly bool[] _cells;
        private readonly int _minX;
        private readonly int _minY;
        private readonly int _width;
        private readonly int _height;

        /// <summary>可走格总数（诊断用）</summary>
        public int WalkableCount { get; }

        public int MinX => _minX;
        public int MinY => _minY;
        public int Width => _width;
        public int Height => _height;

        private NavGrid(bool[] cells, int minX, int minY, int width, int height, int walkableCount)
        {
            _cells = cells;
            _minX = minX;
            _minY = minY;
            _width = width;
            _height = height;
            WalkableCount = walkableCount;
        }

        // ==================== 坐标换算 ====================

        /// <summary>世界坐标 → 格坐标（向下取整）</summary>
        public static Vector2Int WorldToCell(Vector3 world)
            => new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

        /// <summary>格坐标 → 世界坐标（格子中心）</summary>
        public static Vector3 CellToWorld(Vector2Int cell)
            => new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);

        // ==================== 查询 ====================

        public bool InBounds(int x, int y)
        {
            int ix = x - _minX;
            int iy = y - _minY;
            return ix >= 0 && iy >= 0 && ix < _width && iy < _height;
        }

        /// <summary>界外一律视为不可走（省掉调用方的边界判断）</summary>
        public bool IsWalkable(int x, int y)
        {
            int ix = x - _minX;
            int iy = y - _minY;
            if (ix < 0 || iy < 0 || ix >= _width || iy >= _height) return false;
            return _cells[iy * _width + ix];
        }

        public bool IsWalkable(Vector2Int cell) => IsWalkable(cell.x, cell.y);

        // ==================== 构建 ====================

        /// <summary>用一组可走格构建密集数组（重复格自动去重：房间与走廊边角可能重复写入）</summary>
        public static NavGrid Build(List<Vector2Int> walkableCells)
        {
            if (walkableCells == null || walkableCells.Count == 0) return null;

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < walkableCells.Count; i++)
            {
                var c = walkableCells[i];
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.y > maxY) maxY = c.y;
            }

            int w = maxX - minX + 1;
            int h = maxY - minY + 1;
            var cells = new bool[w * h];

            int count = 0;
            for (int i = 0; i < walkableCells.Count; i++)
            {
                var c = walkableCells[i];
                int idx = (c.y - minY) * w + (c.x - minX);
                if (cells[idx]) continue;
                cells[idx] = true;
                count++;
            }

            return new NavGrid(cells, minX, minY, w, h, count);
        }

        // ==================== 诊断 ====================

        /// <summary>
        /// 把网格的一块区域画成 ASCII，用于排查"可走格是否和地板对得上"。
        ///
        /// 这是排查寻路问题最有效的手段：一眼就能看出可走区是**一整片**还是**碎的/错位的**。
        ///   '.' = 可走    '#' = 不可走（墙 / 界外）
        ///   'E' = 敌人所在格   'P' = 目标（玩家）所在格
        ///
        /// y 由大到小打印，和 Unity 世界坐标一致，方便直接对照场景里的上下关系。
        /// </summary>
        public string DumpRegion(Vector2Int center, int halfWidth, int halfHeight,
                                Vector2Int enemyCell, Vector2Int targetCell)
        {
            halfWidth = Mathf.Clamp(halfWidth, 4, 60);
            halfHeight = Mathf.Clamp(halfHeight, 2, 24);

            var sb = new StringBuilder((halfWidth * 2 + 8) * (halfHeight * 2 + 4));
            sb.Append($"NavGrid dump  中心=({center.x},{center.y})  x[{center.x - halfWidth},{center.x + halfWidth}] y[{center.y - halfHeight},{center.y + halfHeight}]  总可走格={WalkableCount}\n");

            // 列号标尺（每 10 列打一个数，方便定位）
            sb.Append("      ");
            for (int x = center.x - halfWidth; x <= center.x + halfWidth; x++)
                sb.Append((x % 10 == 0) ? ((x / 10) % 10).ToString()[0] : ' ');
            sb.Append('\n');

            for (int y = center.y + halfHeight; y >= center.y - halfHeight; y--)
            {
                sb.Append($"{y,5} ");
                for (int x = center.x - halfWidth; x <= center.x + halfWidth; x++)
                {
                    if (x == enemyCell.x && y == enemyCell.y) sb.Append('E');
                    else if (x == targetCell.x && y == targetCell.y) sb.Append('P');
                    else sb.Append(IsWalkable(x, y) ? '.' : '#');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 从 from 出发用 4 邻接 BFS 能走到的格子数。
        /// 用途：验证"房间 + 走廊"是否真的连通 —— 若 reachable &lt; WalkableCount，
        /// 说明某些房间是孤岛（门洞/走廊几何有缺口），A* 必然找不到路。
        /// 只在生成关卡时调用一次，允许分配临时数组。
        /// </summary>
        public int CountReachable(Vector2Int from)
        {
            if (!IsWalkable(from)) return 0;

            int total = _width * _height;
            var visited = new bool[total];
            var queue = new int[total];
            int head = 0, tail = 0;

            int startIdx = (from.y - _minY) * _width + (from.x - _minX);
            visited[startIdx] = true;
            queue[tail++] = startIdx;

            int reached = 0;
            while (head < tail)
            {
                int idx = queue[head++];
                reached++;

                int x = idx % _width + _minX;
                int y = idx / _width + _minY;

                // 4 邻接：用 8 邻接会把"对角贴墙的缝隙"误判成连通
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (!IsWalkable(nx, ny)) continue;

                    int nidx = (ny - _minY) * _width + (nx - _minX);
                    if (visited[nidx]) continue;
                    visited[nidx] = true;
                    queue[tail++] = nidx;
                }
            }
            return reached;
        }
    }
}
