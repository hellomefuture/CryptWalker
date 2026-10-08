#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 测试地图生成器（Tilemap 版）。
    /// 用 walls_floor 的 Tile 资产程序化摆一张地牢测试图：
    ///   - Floor 层：房间内部铺地板
    ///   - Wall 层：房间外圈 + 随机障碍，带 TilemapCollider2D（挡移动）
    /// 布局 = 简单房间 + 十字走廊（可复现，随机种子固定）。
    ///
    /// 用法：菜单 Tools/CryptWalker/生成测试地图。
    /// 若地板/墙选错 Tile（视觉不对），改 floorIndex/wallIndex 重跑（会先删旧地图）。
    /// </summary>
    public static class TestMapGenerator
    {
        private const string TileDir = "Assets/Art/Environment/Tiles/TileAssets";
        private const string Menu    = "Tools/CryptWalker/生成测试地图";

        // Tile 索引（walls_floor_0..279，按图集行列序）；视觉不对就调这两个
        private const int FloorIndex = 0;
        private const int WallIndex  = 26;

        private const int W = 21, H = 15;   // 地图尺寸

        [MenuItem(Menu)]
        public static void Run()
        {
            // 删旧地图（重跑不叠加）
            var old = GameObject.Find("DungeonMap");
            if (old != null) Object.DestroyImmediate(old);

            var tiles = LoadTiles();
            if (tiles == null) return;

            // ---- 生成布局：true=地板, false=墙/空 ----
            var floor = new bool[W, H];
            for (int x = 1; x < W - 1; x++)
                for (int y = 1; y < H - 1; y++)
                    floor[x, y] = true;

            // 随机障碍（固定种子，可复现）
            // 注意：必须写 System.Random —— 裸写 Random 会被 using UnityEngine 解析成
            // UnityEngine.Random（静态类，不能 new），报"无法创建静态类实例"。
            var rnd = new System.Random(42);
            for (int i = 0; i < 12; i++)
            {
                int x = rnd.Next(3, W - 3), y = rnd.Next(3, H - 3);// 避免边界
                floor[x, y] = false;
            }

            // ---- 建 Grid + 两层 Tilemap ----
            var gridGo = new GameObject("DungeonMap");
            var grid = gridGo.AddComponent<Grid>();
            grid.cellSize = Vector3.one;   // 1×1（用户确认：cellSize 1:1）

            var floorMap = CreateLayer(gridGo.transform, "Floor", tiles[FloorIndex], floor, true);
            var wallMap  = CreateLayer(gridGo.transform, "Wall",  tiles[WallIndex],  floor, false);
            wallMap.gameObject.AddComponent<TilemapCollider2D>();   // 墙挡移动

            // 排序：地图永远垫底（角色动态 -y*100 范围约 ±300，地图设 -1000/-999 确保在其下）
            // 否则玩家站地图内 y>0 时 sortingOrder 为负，会被 sortingOrder=0 的地图盖住
            floorMap.GetComponent<TilemapRenderer>().sortingOrder = -1000;
            wallMap.GetComponent<TilemapRenderer>().sortingOrder  = -999;

            EditorUtility.SetDirty(gridGo);
            Debug.Log($"[TestMapGenerator] 测试地图生成完成：{W}x{H}，Floor+Wall 两层");
            EditorUtility.DisplayDialog("测试地图", "已生成 DungeonMap（Floor+Wall 两层）。\n若地板/墙视觉不对，改工具里 FloorIndex/WallIndex 重跑。", "OK");
        }

        /// <summary>建一层 Tilemap 并按布局摆 Tile</summary>
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

        /// <summary>按编号加载 Tile 资产（walls_floor_0..N）</summary>
        private static List<Tile> LoadTiles()
        {
            var guids = AssetDatabase.FindAssets("t:Tile", new[] { TileDir });
            var tiles = guids
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Select(p => new { Path = p, Tile = AssetDatabase.LoadAssetAtPath<Tile>(p) })
                .Where(x => x.Tile != null)
                .OrderBy(x => int.TryParse(System.IO.Path.GetFileNameWithoutExtension(x.Path).Split('_').Last(), out int n) ? n : 0)
                .Select(x => x.Tile)
                .ToList();

            if (tiles.Count == 0 || tiles.Count <= Mathf.Max(FloorIndex, WallIndex))
            {
                EditorUtility.DisplayDialog("测试地图", $"Tile 资产不足（{tiles.Count} 个），请确认 {TileDir}", "OK");
                return null;
            }
            return tiles;
        }
    }
}
#endif
