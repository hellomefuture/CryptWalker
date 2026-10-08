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
    /// 地图房间模板基类（普通 class，供子类继承复用 Tile 加载 + Tilemap 构建工具方法）。
    /// ⚠️ 不能声明 static class —— static 类不能被继承（CS0713），且不能有 protected 成员（CS1057）。
    /// </summary>
    public abstract class MapRoomBuilderBase
    {
        public const string TileDir   = "Assets/Art/Environment/Tiles/TileAssets";
        public const string PrefabDir = "Assets/Art/Prefabs/Maps";
        public const int FloorIndex = 0;
        public const int WallIndex  = 26;
        public const int W = 21, H = 15;
        public const float CellSize = 1f;

        public static List<Tile> LoadTiles()
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

        public static void CreateAnchor(Transform parent, string name, int gridX, int gridY)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(gridX * CellSize, gridY * CellSize, 0f);
        }

        public static Tilemap CreateLayer(Transform parent, string name, TileBase tile, bool[,] floor, bool isFloorLayer)
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

        /// <summary>建房间标准结构（地板 + 墙 + 出生 + 出口），各 Run() 复用</summary>
        public static void BuildStandardRoom(
            string mapName,
            bool addPlayerSpawn,    Vector2Int playerSpawnPos,
            bool addExit,           Vector2Int exitPos,
            List<Vector2Int> enemyAnchors = null)
        {
            var tiles = LoadTiles();
            if (tiles == null) return;

            var oldScene = GameObject.Find(mapName);
            if (oldScene != null) Object.DestroyImmediate(oldScene);

            var floor = new bool[W, H];
            for (int x = 1; x < W - 1; x++)
                for (int y = 1; y < H - 1; y++)
                    floor[x, y] = true;

            var gridGo = new GameObject(mapName);
            gridGo.AddComponent<Grid>().cellSize = new Vector3(CellSize, CellSize, 0f);

            var floorMap = CreateLayer(gridGo.transform, "Floor", tiles[FloorIndex], floor, true);
            var wallMap  = CreateLayer(gridGo.transform, "Wall",  tiles[WallIndex],  floor, false);
            wallMap.gameObject.AddComponent<TilemapCollider2D>();
            floorMap.GetComponent<TilemapRenderer>().sortingOrder = -1000;
            wallMap.GetComponent<TilemapRenderer>().sortingOrder  = -999;

            if (addPlayerSpawn) CreateAnchor(gridGo.transform, "Anchor_SpawnPlayer", playerSpawnPos.x, playerSpawnPos.y);
            if (addExit)        CreateAnchor(gridGo.transform, "Anchor_Exit",        exitPos.x,        exitPos.y);
            if (enemyAnchors != null)
                for (int i = 0; i < enemyAnchors.Count; i++)
                    CreateAnchor(gridGo.transform, $"Anchor_Enemy{i + 1}", enemyAnchors[i].x, enemyAnchors[i].y);

            Directory.CreateDirectory(PrefabDir);
            string prefabPath = $"{PrefabDir}/{mapName}.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(gridGo, prefabPath, InteractionMode.AutomatedAction);

            Debug.Log($"[{mapName}] 已生成并存为 {prefabPath}");
        }
    }

    /// <summary>
    /// 起始房模板（无敌人，玩家出生 + 北边出口）。
    /// </summary>
    public static class MapRoomStartBuilder
    {
        private const string Menu = "Tools/CryptWalker/生成Map_Room_M_Start01";
        private const string MapName = "Map_Room_M_Start01";

        [MenuItem(Menu)]
        public static void Run()
        {
            // ⚠️ 子类的 MapName 常量传入 BuildStandardRoom —— 直接传字符串字面量也行
            MapRoomBuilderBase.BuildStandardRoom(
                MapName,
                addPlayerSpawn: true, playerSpawnPos: new Vector2Int(10, 7),
                addExit:        true, exitPos:        new Vector2Int(10, 14));

            EditorUtility.DisplayDialog("地图模板", $"已生成 {MapName}\n（玩家出生 + 北边出口，无敌人）", "OK");
        }
    }

    /// <summary>
    /// 出口房模板（无敌人，南边入口 + 中央通关门）。
    /// 修复：Anchor_Entry 必须在 BuildStandardRoom 之后、保存 prefab 之前加
    /// 否则 SaveAsPrefabAsset 不会把 Anchor_Entry 存进 prefab → DungeonGenerator 找不到 Entry → End 房不移动 → 和 Start 重叠
    /// </summary>
    public static class MapRoomEndBuilder
    {
        private const string Menu = "Tools/CryptWalker/生成Map_Room_M_End01";
        private const string MapName = "Map_Room_M_End01";
        private const string PrefabPath = "Assets/Art/Prefabs/Maps/Map_Room_M_End01.prefab";

        [MenuItem(Menu)]
        public static void Run()
        {
            // 删旧 prefab 和场景实例（保证重新生成）
            AssetDatabase.DeleteAsset(PrefabPath);
            var oldScene = GameObject.Find(MapName);
            if (oldScene != null) Object.DestroyImmediate(oldScene);

            // 1) 建基础结构（Grid + Floor + Wall + Anchor_Exit）
            MapRoomBuilderBase.BuildStandardRoom(
                MapName,
                addPlayerSpawn: false, playerSpawnPos: new Vector2Int(0, 0),  // 必填但不用
                addExit:        true, exitPos: new Vector2Int(10, 7),       // 中央通关门
                enemyAnchors:   null);

            // 2) 加 Anchor_Entry（南边入口，y=1 在 Wall 内侧）
            var root = GameObject.Find(MapName);
            if (root != null)
            {
                var go = new GameObject("Anchor_Entry");
                go.transform.SetParent(root.transform);
                go.transform.position = new Vector3(10 * MapRoomBuilderBase.CellSize, 1 * MapRoomBuilderBase.CellSize, 0);
            }

            // 3) 一次性保存：grid + Floor + Wall + Anchor_Exit + Anchor_Entry 全部进 prefab
            if (root != null)
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
            }

            EditorUtility.DisplayDialog("地图模板", $"已生成 {MapName}\n（南边入口 + 中央通关门）", "OK");
        }
    }
}
#endif