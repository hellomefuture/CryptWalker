using System.Collections.Generic;
using UnityEngine;

namespace CryptWalker.HotUpdate.AI
{
    /// <summary>
    /// 网格 A*（8 方向 + 禁止穿角）。
    ///
    /// 针对 Android 的性能设计（AGENTS.md §13.3 移动端 ≥30fps）：
    ///   1) <b>零 GC</b>：所有容器在实例内部复用，网格没变就不重建；重复寻路不产生垃圾
    ///   2) <b>访问戳</b>：用 _searchId 标记"本次搜索访问过"，免去每次清空 visited 数组
    ///   3) <b>二叉最小堆</b>开放集：取最小 f 是 O(log n)，不是 List 每次排序
    ///   4) <b>节点上限</b>：超过 MaxSearchNodes 就放弃本次寻路，防止单帧卡顿
    ///   5) <b>路径平滑</b>：贪心"拉直"，消掉 A* 特有的锯齿折线；且有窗口上限，避免 O(n²) 炸开
    ///   6) <b>禁止穿角</b>：对角移动要求两个正交邻格都可走，否则敌人会从墙的拐角"钻"过去
    ///
    /// 用法：<b>每个寻路者持有一个实例</b>（不要每次调用 new，否则复用缓冲全白费）
    ///   if (_astar.FindPath(navGrid, startCell, goalCell, _pathBuffer)) { ... }
    /// </summary>
    public sealed class AStarGrid
    {
        /// <summary>单次搜索展开的节点上限。超出即放弃（返回 false），保证最坏情况也不会卡帧。</summary>
        public int MaxSearchNodes = 4000;

        /// <summary>路径平滑时向后"拉直"的最大窗口，防止长路径退化成 O(n²)</summary>
        private const int SmoothWindow = 32;

        private const float StraightCost = 1f;
        private const float DiagonalCost = 1.41421356f;   // √2

        // 8 邻接方向；索引 4~7 是对角
        private static readonly int[] OffsetX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] OffsetY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        // ==================== 复用缓冲 ====================
        private NavGrid _grid;          // 缓存的网格；换了网格（重新生成关卡）就重建缓冲
        private int _size;
        private float[] _gScore;        // 起点到该节点的已知最小代价
        private int[] _cameFrom;        // 父节点索引，-1 = 无
        private int[] _visitStamp;      // 访问戳：== _searchId 表示"本次搜索访问过"
        private float[] _insertedF;     // 入堆时的 f 值（用于堆的惰性删除）
        private int _searchId;

        // 二叉最小堆（开放集）
        private int[] _heapIndex;
        private float[] _heapF;
        private int _heapCount;

        private readonly List<Vector2Int> _smoothBuffer = new List<Vector2Int>(256);

        // ==================== 对外接口 ====================

        /// <summary>
        /// 求 start → goal 的最短路。结果写入 result（会先 Clear，复用调用方的 List）。
        /// 返回 false 表示不可达 / 无网格 / 超出节点上限。
        /// </summary>
        /// <remarks>
        /// start 允许不可走（敌人可能因碰撞挤进墙边）——不强制要求起点可走，只是不会把它当成有效终点。
        /// goal 必须可走，否则直接返回 false（调用方应先把 goal 吸附到最近的可走格）。
        /// </remarks>
        public bool FindPath(NavGrid grid, Vector2Int start, Vector2Int goal, List<Vector2Int> result)
        {
            if (result == null) return false;
            result.Clear();
            if (grid == null) return false;
            if (!grid.IsWalkable(goal)) return false;
            if (!grid.InBounds(start.x, start.y)) return false;

            EnsureBuffers(grid);

            int startIdx = ToIndex(grid, start.x, start.y);
            int goalIdx = ToIndex(grid, goal.x, goal.y);
            if (startIdx < 0 || goalIdx < 0) return false;

            if (startIdx == goalIdx)
            {
                result.Add(start);
                return true;
            }

            _searchId++;
            _heapCount = 0;

            _gScore[startIdx] = 0f;
            _cameFrom[startIdx] = -1;
            _visitStamp[startIdx] = _searchId;
            HeapPush(startIdx, Heuristic(start.x, start.y, goal.x, goal.y));

            int expanded = 0;
            bool found = false;

            while (_heapCount > 0)
            {
                int current = HeapPop();
                if (current == goalIdx) { found = true; break; }

                if (++expanded > MaxSearchNodes) break;

                int cx = current % grid.Width + grid.MinX;
                int cy = current / grid.Width + grid.MinY;
                float cg = _gScore[current];

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + OffsetX[d];
                    int ny = cy + OffsetY[d];
                    if (!grid.IsWalkable(nx, ny)) continue;

                    bool diagonal = OffsetX[d] != 0 && OffsetY[d] != 0;
                    if (diagonal)
                    {
                        // 禁止穿角：斜着穿过墙的拐角时，两个正交格必须都能走
                        if (!grid.IsWalkable(cx + OffsetX[d], cy)) continue;
                        if (!grid.IsWalkable(cx, cy + OffsetY[d])) continue;
                    }

                    float ng = cg + (diagonal ? DiagonalCost : StraightCost);
                    int nidx = ToIndex(grid, nx, ny);
                    if (nidx < 0) continue;

                    // 已访问过且没有更优 → 跳过
                    if (_visitStamp[nidx] == _searchId && ng >= _gScore[nidx]) continue;

                    _visitStamp[nidx] = _searchId;
                    _gScore[nidx] = ng;
                    _cameFrom[nidx] = current;
                    HeapPush(nidx, ng + Heuristic(nx, ny, goal.x, goal.y));
                }
            }

            if (!found) return false;

            // 回溯：goal → start，然后反转
            int node = goalIdx;
            int guard = 0;
            while (node >= 0 && guard++ < 100000)
            {
                result.Add(new Vector2Int(node % grid.Width + grid.MinX, node / grid.Width + grid.MinY));
                if (node == startIdx) break;
                node = _cameFrom[node];
            }
            result.Reverse();

            Smooth(grid, result);
            return result.Count > 0;
        }

        /// <summary>
        /// 网格直线可见性（Bresenham）——用于两处：
        ///   1) 路径平滑：判断两个 waypoint 之间能否"拉直"
        ///   2) 敌人 AI：与玩家之间没有墙时直接冲，不必走 A* 路径
        /// 与 A* 一样禁止穿角（斜向穿越墙拐角视为不可见）。
        /// </summary>
        public static bool HasLineOfSight(NavGrid grid, Vector2Int a, Vector2Int b)
        {
            if (grid == null) return false;

            int x = a.x, y = a.y;
            int dx = Mathf.Abs(b.x - a.x);
            int dy = Mathf.Abs(b.y - a.y);
            int sx = a.x < b.x ? 1 : -1;
            int sy = a.y < b.y ? 1 : -1;
            int err = dx - dy;
            int guard = 0;

            while (guard++ < 4096)
            {
                if (x == b.x && y == b.y) return true;

                int e2 = err * 2;
                int nx = x, ny = y;
                if (e2 > -dy) { err -= dy; nx += sx; }
                if (e2 < dx) { err += dx; ny += sy; }

                // 禁止穿角（起点本身不检查，允许敌人当前身处墙边）
                if (nx != x && ny != y)
                {
                    if (!grid.IsWalkable(nx, y)) return false;
                    if (!grid.IsWalkable(x, ny)) return false;
                }
                if (!grid.IsWalkable(nx, ny)) return false;

                x = nx;
                y = ny;
            }
            return false;
        }

        // ==================== 内部实现 ====================

        private void EnsureBuffers(NavGrid grid)
        {
            int size = grid.Width * grid.Height;
            if (_grid == grid && _size == size) return;   // 同一张网格 → 缓冲可继续用

            _grid = grid;
            _size = size;
            _gScore = new float[size];
            _cameFrom = new int[size];
            _visitStamp = new int[size];
            _insertedF = new float[size];
            // 堆只用到索引 1.._heapCount（二叉堆从 1 开始），容量不足时 HeapPush 会自动扩容
            _heapIndex = new int[size + 1];
            _heapF = new float[size + 1];
            _heapCount = 0;
            _searchId = 0;
        }

        private int ToIndex(NavGrid grid, Vector2Int c) => ToIndex(grid, c.x, c.y);

        private int ToIndex(NavGrid grid, int x, int y)
        {
            int ix = x - grid.MinX;
            int iy = y - grid.MinY;
            if (ix < 0 || iy < 0 || ix >= grid.Width || iy >= grid.Height) return -1;
            return iy * grid.Width + ix;
        }

        /// <summary>八方向启发式（对角距离，比曼哈顿更贴合 8 邻接的真实代价）</summary>
        private static float Heuristic(int ax, int ay, int bx, int by)
        {
            int dx = Mathf.Abs(ax - bx);
            int dy = Mathf.Abs(ay - by);
            int min = Mathf.Min(dx, dy);
            return (dx + dy) + (DiagonalCost - 2f) * min;
        }

        private void HeapPush(int index, float f)
        {
            _insertedF[index] = f;

            // 惰性删除意味着同一个节点可能被多次入堆，堆的峰值容量不保证 ≤ 节点数。
            // 这里做一次容量自增长，避免极端地图下 IndexOutOfRange（正常情况永远不会触发）。
            if (_heapCount + 1 >= _heapIndex.Length)
            {
                int newCap = Mathf.Max(_heapIndex.Length * 2, 8);
                System.Array.Resize(ref _heapIndex, newCap);
                System.Array.Resize(ref _heapF, newCap);
            }

            _heapCount++;
            int i = _heapCount;
            _heapIndex[i] = index;
            _heapF[i] = f;

            // 上浮
            while (i > 1)
            {
                int parent = i >> 1;
                if (_heapF[parent] <= _heapF[i]) break;
                Swap(parent, i);
                i = parent;
            }
        }

        private int HeapPop()
        {
            // 惰性删除：堆里可能残留同节点的旧 f 值，弹出时跳过（当前 f 一定 ≤ 入堆时的 f）
            while (_heapCount > 0)
            {
                int top = _heapIndex[1];
                float topF = _heapF[1];

                _heapCount--;
                _heapIndex[1] = _heapIndex[_heapCount + 1];
                _heapF[1] = _heapF[_heapCount + 1];

                // 下沉
                int i = 1;
                while (true)
                {
                    int left = i << 1;
                    int right = left + 1;
                    int smallest = i;
                    if (left <= _heapCount && _heapF[left] < _heapF[smallest]) smallest = left;
                    if (right <= _heapCount && _heapF[right] < _heapF[smallest]) smallest = right;
                    if (smallest == i) break;
                    Swap(smallest, i);
                    i = smallest;
                }

                if (topF > _insertedF[top]) continue;   // 过期条目，丢
                return top;
            }
            return -1;
        }

        private void Swap(int a, int b)
        {
            int ti = _heapIndex[a]; _heapIndex[a] = _heapIndex[b]; _heapIndex[b] = ti;
            float tf = _heapF[a]; _heapF[a] = _heapF[b]; _heapF[b] = tf;
        }

        /// <summary>
        /// 路径平滑（贪心拉直）：消掉 8 方向 A* 必然产生的锯齿折线。
        /// 从当前点向后找"能直线到达"的最远点，中间点全部丢弃。
        /// </summary>
        private void Smooth(NavGrid grid, List<Vector2Int> path)
        {
            if (path.Count <= 2) return;

            _smoothBuffer.Clear();
            int cur = 0;
            _smoothBuffer.Add(path[0]);

            while (cur < path.Count - 1)
            {
                int next = Mathf.Min(path.Count - 1, cur + SmoothWindow);
                while (next > cur + 1 && !HasLineOfSight(grid, path[cur], path[next]))
                    next--;
                _smoothBuffer.Add(path[next]);
                cur = next;
            }

            path.Clear();
            path.AddRange(_smoothBuffer);
        }
    }
}
