using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CryptWalker.HotUpdate.GameFlow.Dungeon
{
    /// <summary>
    /// 房间类型（方案 B：Grid-based）。
    ///
    /// 注意：取消 Corridor* 枚举 —— 走廊在程序化方案中是独立的 GameObject，
    /// 不属于"房间"，不需要 RoomType 标识。
    /// </summary>
    public enum RoomType { Start, Combat, End }

    /// <summary>
    /// 朝向枚举（用于房间出口 / 走廊方向）。
    /// </summary>
    public enum Direction { North, South, East, West }

    /// <summary>
    /// 网格房间数据（方案 B：Grid-based 程序化生成）。
    ///
    /// 字段语义：
    ///   GridPos     网格坐标 (gx, gy)，0 ≤ gx, gy < GRID_SIZE
    ///   WorldPos    房间根物体世界坐标（左下角）
    ///   Width/Height 房间格子尺寸（不含墙外皮）
    ///   Root/Floor/Wall/WallCollider 场景对象引用（DungeonGenerator 在 CreateRoom 时建）
    ///   Exits       出口方向列表（用于挖门洞和生成走廊）
    ///   Neighbors   相邻房间引用（已建立连接的 GridRoom）
    /// </summary>
    public class GridRoom
    {
        public RoomType Type;
        public Vector2Int GridPos;
        public Vector3 WorldPos;
        public int Width;
        public int Height;
        public GameObject Root;
        public Tilemap Floor;
        public Tilemap Wall;
        public TilemapCollider2D WallCollider;
        public List<Direction> Exits = new List<Direction>();
        public List<GridRoom> Neighbors = new List<GridRoom>();

        /// <summary>房间中心格子坐标（lx, ly）—— 用于刷敌人/玩家</summary>
        public Vector2Int CenterCell => new Vector2Int(Width / 2, Height / 2);

        /// <summary>房间中心世界坐标 —— 刷敌人/玩家用</summary>
        public Vector3 CenterWorld => WorldPos + new Vector3(Width * 0.5f, Height * 0.5f, 0);

        /// <summary>方向反转（South→North / East→West）</summary>
        public static Direction Opposite(Direction d)
        {
            switch (d)
            {
                case Direction.North: return Direction.South;
                case Direction.South: return Direction.North;
                case Direction.East:  return Direction.West;
                case Direction.West:  return Direction.East;
            }
            return d;
        }

        /// <summary>方向 → 网格偏移向量（dx, dy）</summary>
        public static Vector2Int Offset(Direction d)
        {
            switch (d)
            {
                case Direction.North: return new Vector2Int(0, 1);
                case Direction.South: return new Vector2Int(0, -1);
                case Direction.East:  return new Vector2Int(1, 0);
                case Direction.West:  return new Vector2Int(-1, 0);
            }
            return Vector2Int.zero;
        }
    }

    /// <summary>
    /// RoomData 保留作为兼容类型（GameFlowMachine 可能仍引用）。
    /// 实际数据存储在 GridRoom 中。
    /// </summary>
    public class RoomData
    {
        public RoomType Type;
        public GridRoom Room;
    }
}