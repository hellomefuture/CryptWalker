#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 直线走廊模板生成器 —— Map_Corridor_Straight01（5×15 长条走廊，肉鸽两级生成的连接件）。
    ///
    /// 结构（生成的 Hierarchy）：
    ///   Map_Corridor_Straight01 (Grid)
    ///   ├── Floor                  地板层（5×15 全铺）
    ///   ├── Wall                   墙层（上下两行 + 左右最外）+ TilemapCollider2D
    ///   ├── Anchor_Entry           入口（南端中央，(2, 0)）
    ///   └── Anchor_Exit            出口（北端中央，(2, 14)）
    ///
    /// Anchor_Entry / Anchor_Exit 是肉鸽算法拼接两个 prefab 的"对接口"——
    /// Exit_A 接到 Entry_B 即连通。
    ///
    /// 用法：菜单 Tools/CryptWalker/生成Map_Corridor_Straight01
    ///   → 自动建场景物体 + 存为 Assets/Art/Prefabs/Maps/Map_Corridor_Straight01.prefab
    /// Tile 视觉不匹配改 FloorIndex / WallIndex 重跑。
    /// </summary>
    public static class MapCorridorStraightBuilder
    {
        private const string TileDir   = "Assets/Art/Environment/Tiles/TileAssets";
        private const string PrefabDir = "Assets/Art/Prefabs/Maps";
        private const string Menu      = "Tools/CryptWalker/生成Map_Corridor_Straight01";
        private const string MapName   = "Map_Corridor_Straight01";

        // 与战斗房保持同一套 Tile 索引（同一图集 walls_floor）
        private const int FloorIndex = 0;
        private const int WallIndex  = 26;

        private const int W = 5, H = 15;   // 长条：宽 5，长 15（南北贯通）
        private const float CellSize = 1f;

        [MenuItem(Menu)]
        public static void Run()
        {
            var tiles = LoadTiles();
            if (tiles == null) return;

            var oldScene = GameObject.Find(MapName);
            if (oldScene != null) Object.DestroyImmediate(oldScene);

            // ---- 走廊布局：边缘 = 墙，内部 = 地板 ----
            // 修复：之前 floor[x, y] = true（全 true）→ Wall 层 !floor 全 false → 没墙
            // 现在：边缘（x=0/4、y=0/14）floor=false → Wall=true（4 周有墙）
            //       中间（x=1..3）floor=true → 无墙（贯通）
            // 拼接时 ClearWallAtAnchor 挖 3×3 自动清掉入口/出口位置的墙
            var floor = new bool[W, H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    if (x > 0 && x < W - 1 && y > 0 && y < H - 1)
                        floor[x, y] = true;   // 中间全通
                }

            // ---- 建 Grid + 两层 Tilemap ----
            var gridGo = new GameObject(MapName);
            gridGo.AddComponent<Grid>().cellSize = new Vector3(CellSize, CellSize, 0f);

            var floorMap = CreateLayer(gridGo.transform, "Floor", tiles[FloorIndex], floor, true);
            var wallMap  = CreateLayer(gridGo.transform, "Wall",  tiles[WallIndex],  floor, false);
            wallMap.gameObject.AddComponent<TilemapCollider2D>();
            floorMap.GetComponent<TilemapRenderer>().sortingOrder = -1000;
            wallMap.GetComponent<TilemapRenderer>().sortingOrder  = -999;

            // ---- 标记点：南北端中央（走廊是"贯通"的，宽度 5，中央是 x=2）----
            CreateAnchor(gridGo.transform, "Anchor_Entry", W / 2, 0);
            CreateAnchor(gridGo.transform, "Anchor_Exit",  W / 2, H - 1);

            // ---- 存 prefab ----
            Directory.CreateDirectory(PrefabDir);
            string prefabPath = $"{PrefabDir}/{MapName}.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(gridGo, prefabPath, InteractionMode.AutomatedAction);

            Debug.Log($"[MapCorridorStraightBuilder] {MapName} 已生成并存为 {prefabPath}");
            EditorUtility.DisplayDialog("地图模板",
                $"已生成 {MapName}\n\n存于：{prefabPath}\n\n包含：Floor+Wall 双 Tilemap + 2 个 Anchor（南北入口/出口）",
                "OK");
        }

        private static Tilemap CreateLayer(Transform parent, string name, TileBase tile, bool[,] floor, bool isFloorLayer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            var map = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    bool put = isFloorLayer ? floor[x, y] : !floor[x, y];
                    if (put) map.SetTile(new Vector3Int(x, y, 0), tile);
                }
            return map;
        }

        private static void CreateAnchor(Transform parent, string name, int gridX, int gridY)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(gridX * CellSize, gridY * CellSize, 0f);
        }

        private static List<Tile> LoadTiles()
        {
            var guids = AssetDatabase.FindAssets("t:Tile", new[] { TileDir });
            var tiles = guids
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Select(p => new { Path = p, Tile = AssetDatabase.LoadAssetAtPath<Tile>(p) })
                .Where(x => x.Tile != null)
                .OrderBy(x => int.TryParse(Path.GetFileNameWithoutExtension(x.Path).Split('_').Last(), out int n) ? n : 0)
                .Select(x => x.Tile)
                .ToList();

            if (tiles.Count == 0 || tiles.Count <= Mathf.Max(FloorIndex, WallIndex))
            {
                EditorUtility.DisplayDialog("地图模板", $"Tile 资产不足（{tiles.Count} 个），请确认 {TileDir}", "OK");
                return null;
            }
            return tiles;
        }
    }
}
#endif