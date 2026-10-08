using UnityEngine;
using UnityEngine.Tilemaps;

namespace CryptWalker.Main.Data
{
    /// <summary>
    /// 程序化生成关卡所用的 Tile 资源集合（方案 B：Grid-based 完全程序化）。
    ///
    /// 资源流：
    ///   LevelConfig.asset 通过 Inspector 拖入 RoomTileset.asset 引用
    ///   → RoomTileset 持有 Floor / Wall 两个 Tile 资产
    ///   → DungeonGenerator 在运行时通过 LevelConfig.roomTileset 拿，
    ///     直接调用 UnityEngine.Tilemaps API 绘制房间和走廊
    ///
    /// 为什么放 AOT 端：与 LevelConfig 一致，是两侧都能引用的"配置类型"，
    /// 违反"HotUpdate 严禁引用 AOT API"的硬约束是被显式允许的例外。
    /// </summary>
    [CreateAssetMenu(menuName = "CryptWalker/RoomTileset", fileName = "RoomTileset")]
    public class RoomTileset : ScriptableObject
    {
        [Header("Tile Assets (从 Assets/Art/Environment/Tiles/TileAssets 拖入)")]
        [Tooltip("地板 tile，用于填充房间内部和走廊")]
        public TileBase Floor;

        [Tooltip("墙 tile，用于房间边缘和走廊两侧")]
        public TileBase Wall;
    }
}