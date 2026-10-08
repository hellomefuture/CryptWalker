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
    /// T 字走廊模板生成器 —— Map_Corridor_T_01（5×15 顶窄 + 7×3 横出"门口"）。
    ///
    /// 结构（生成的 Hierarchy）：
    ///   Map_Corridor_T_01 (Grid)
    ///   ├── Floor       地板层（T 字形：纵道 5×15 + 顶部一道横出 7×3）
    ///   ├── Wall        墙层（T 字形外墙）+ TilemapCollider2D
    ///   ├── Anchor_Entry       入口（南端中央，纵道 (2, 0)）
    ///   └── Anchor_Exit1, Anchor_Exit2   出口：左边 (0, 8) / 右边 (6, 8)
    ///
    /// 布局示意（5 宽 × 15 高，纵道 + 顶部一道门）
    ///     ┌─────┐         ← 横道 7×3
    ///     │  │  │
    ///   ┌─┘  └─┐
    ///   │     │          ← 纵道 5×15，南端是 Entry
    ///   │     │
    ///   │     │
    ///   │     │          Entry 在南端中央
    ///   └─────┘
    ///
    /// 用法：菜单 Tools/CryptWalker/生成Map_Corridor_T_01
    /// </summary>
    public static class MapCorridorTBuilder
    {
        private const string TileDir   = "Assets/Art/Environment/Tiles/TileAssets";
        private const string PrefabDir = "Assets/Art/Prefabs/Maps";
        private const string Menu      = "Tools/CryptWalker/生成Map_Corridor_T_01";
        private const string MapName   = "Map_Corridor_T_01";

        private const int FloorIndex = 0;
        private const int WallIndex  = 26;

        private const int W = 7, H = 15;       // 总宽 7（够装横道 + 纵道），总高 15
        private const int CrossY = 11;          // 横道顶部 y（y >= 11 是横道区）
        private const int CrossW = 7;           // 横道宽度 = 整个地图宽度
        private const float CellSize = 1f;

        [MenuItem(Menu)]
        public static void Run()
        {
            var tiles = LoadTiles();
            if (tiles == null) return;

            var oldScene = GameObject.Find(MapName);
            if (oldScene != null) Object.DestroyImmediate(oldScene);

            // ---- T 字地板布局 ----
            var floor = new bool[W, H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // 横道：顶部 CrossY 以上，x 全部通
                    // 纵道：底部 CrossY 以下，中间 5 列通（左右各空 1 列放墙）
                    if (y >= CrossY) floor[x, y] = true;
                    else floor[x, y] = (x >= 1 && x <= 5);
                }

            var gridGo = new GameObject(MapName);
            gridGo.AddComponent<Grid>().cellSize = new Vector3(CellSize, CellSize, 0f);

            var floorMap = CreateLayer(gridGo.transform, "Floor", tiles[FloorIndex], floor, true);
            var wallMap  = CreateLayer(gridGo.transform, "Wall",  tiles[WallIndex],  floor, false);
            wallMap.gameObject.AddComponent<TilemapCollider2D>();
            floorMap.GetComponent<TilemapRenderer>().sortingOrder = -1000;
            wallMap.GetComponent<TilemapRenderer>().sortingOrder  = -999;

            // ---- 锚点 ----
            // Entry 在南端纵道中央（x=3，y=0），Exit1/2 在横道两端（x=0 和 x=6，y=11）
            CreateAnchor(gridGo.transform, "Anchor_Entry",  3, 0);
            CreateAnchor(gridGo.transform, "Anchor_Exit1", 0, CrossY);
            CreateAnchor(gridGo.transform, "Anchor_Exit2", W - 1, CrossY);

            Directory.CreateDirectory(PrefabDir);
            string prefabPath = $"{PrefabDir}/{MapName}.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(gridGo, prefabPath, InteractionMode.AutomatedAction);

            Debug.Log($"[MapCorridorTBuilder] {MapName} 已生成并存为 {prefabPath}");
            EditorUtility.DisplayDialog("地图模板",
                $"已生成 {MapName}\n\n存于：{prefabPath}\n\n包含：Floor+Wall 双 Tilemap + 3 个 Anchor\n  - Entry: 南端纵道中央\n  - Exit1 / Exit2: 横道两端（左/右）",
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