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
    /// 地图模板生成器 —— Map_Room_M_Combat01（基础战斗房，肉鸽两级生成的核心单元）。
    ///
    /// 结构（生成的 Hierarchy）：
    ///   Map_Room_M_Combat01 (Grid)
    ///   ├── Floor                  地板层（全部铺）+ TilemapRenderer sortingOrder -1000
    ///   ├── Wall                   墙层（四周外圈）+ TilemapCollider2D（挡移动）+ sortingOrder -999
    ///   ├── Anchor_SpawnPlayer     玩家出生点（格子 (10, 7)，房间中央）
    ///   ├── Anchor_Enemy1          敌人刷出点 1（格子 (4, 4)）
    ///   ├── Anchor_Enemy2          敌人刷出点 2（格子 (16, 10)）
    ///   └── Anchor_Exit            出口点（格子 (20, 7)，东边中央，肉鸽拼图接口）
    ///
    /// 这些 Anchor 是空 GameObject —— 以后肉鸽算法读这些 Transform 就能：
    ///   - 在 Anchor_SpawnPlayer 放玩家
    ///   - 在 Anchor_Enemy* 刷敌人
    ///   - 从 Anchor_Exit 拼走廊到下一个房间
    ///
    /// 用法：菜单 Tools/CryptWalker/生成Map_Room_M_Combat01
    ///   → 自动建场景物体 + 存为 Assets/Art/Prefabs/Maps/Map_Room_M_Combat01.prefab
    ///
    /// Tile 索引如果视觉不匹配，改 FloorIndex / WallIndex 重跑。
    /// </summary>
    public static class MapRoomCombatBuilder
    {
        private const string TileDir   = "Assets/Art/Environment/Tiles/TileAssets";
        private const string PrefabDir = "Assets/Art/Prefabs/Maps";
        private const string Menu      = "Tools/CryptWalker/生成Map_Room_M_Combat01";
        private const string MapName   = "Map_Room_M_Combat01";

        // Tile 索引（walls_floor_0..279，按图集行列序）；视觉不对就改这里
        private const int FloorIndex = 0;
        private const int WallIndex  = 26;

        private const int W = 21, H = 15;  // 21×15 = 中型房间（地图用 M=中）
        private const float CellSize = 1f;  // 1×1（用户确认：cellSize 1:1）

        [MenuItem(Menu)]
        public static void Run()
        {
            var tiles = LoadTiles();
            if (tiles == null) return;

            // 删旧场景实例（重跑不叠加）
            var oldScene = GameObject.Find(MapName);
            if (oldScene != null) Object.DestroyImmediate(oldScene);

            // ---- 生成地板布局：外圈 = 墙，内部 = 地板（无障碍，纯净模板）----
            var floor = new bool[W, H];
            for (int x = 1; x < W - 1; x++)
                for (int y = 1; y < H - 1; y++)
                    floor[x, y] = true;

            // ---- 建 Grid + 两层 Tilemap ----
            var gridGo = new GameObject(MapName);
            gridGo.AddComponent<Grid>().cellSize = new Vector3(CellSize, CellSize, 0f);

            var floorMap = CreateLayer(gridGo.transform, "Floor", tiles[FloorIndex], floor, true);
            var wallMap  = CreateLayer(gridGo.transform, "Wall",  tiles[WallIndex],  floor, false);
            wallMap.gameObject.AddComponent<TilemapCollider2D>();
            floorMap.GetComponent<TilemapRenderer>().sortingOrder = -1000;
            wallMap.GetComponent<TilemapRenderer>().sortingOrder  = -999;

            // ---- 标记点（空 GameObject，肉鸽算法的接口）----
            // 修复：原来 Anchor_Exit 在 (20, 7) 东边中央——和 Corridor 的 Entry (2, 0) 南边中央方向不匹配
            // PlaceRoomByAnchor 让 Corridor 嵌入到 Combat 房内部，地图看起来很乱
            // 现在：Anchor_Entry 在 (10, 0) 南边中央，Anchor_Exit 在 (10, 14) 北边中央——Corridor 上下对接
            CreateAnchor(gridGo.transform, "Anchor_SpawnPlayer", W / 2, H / 2);          // 房间中央 (10, 7)
            CreateAnchor(gridGo.transform, "Anchor_Enemy1",      4,  4);
            CreateAnchor(gridGo.transform, "Anchor_Enemy2",      16, 10);
            CreateAnchor(gridGo.transform, "Anchor_Entry",       W / 2, 0);            // 南边中央 (10, 0)
            CreateAnchor(gridGo.transform, "Anchor_Exit",        W / 2, H - 1);        // 北边中央 (10, 14)

            // ---- 存为 prefab ----
            Directory.CreateDirectory(PrefabDir);
            string prefabPath = $"{PrefabDir}/{MapName}.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(gridGo, prefabPath, InteractionMode.AutomatedAction);
            // 保存场景实例（让用户能在场景里调整/确认）
            // 不再 DestroyImmediate，让 prefab 实例留在场景里

            Debug.Log($"[MapRoomCombatBuilder] {MapName} 已生成并存为 {prefabPath}");
            EditorUtility.DisplayDialog("地图模板",
                $"已生成 {MapName}\n\n存于：{prefabPath}\n\n包含：Floor+Wall 双 Tilemap + 4 个 Anchor\n\n下一步：在场景里选中它再拖拽使用，或用程序化 SpawnEnemy() 在 Anchor_Enemy* 位置生成敌人。",
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

        /// <summary>创建 Anchor 标记点（世界坐标 = 格子坐标 × cellSize）</summary>
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