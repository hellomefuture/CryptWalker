#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 一键把项目从"spriteRenderer.sortingOrder 偏移大数值"切到"Sorting Layer 分层"方案。
    ///
    /// 做的事（菜单 Tools/CryptWalker/切到 Sorting Layer 分层）：
    ///   1) 在 ProjectSettings/TagManager.asset 注入 Map / Character Sorting Layer
    ///   2) 遍历 Assets/Resources/Maps/*.prefab：
    ///      - Floor Tilemap → Sorting Layer="Map",  Order=0
    ///      - Wall  Tilemap → Sorting Layer="Map",  Order=1（墙在地板上面）
    ///   3) 遍历 Assets/Art/Prefabs/player.prefab + Orc*.prefab：
    ///      - SpriteRenderer → Sorting Layer="Character", Order=0
    ///   4) 不动 EnemyController/PlayerController 的代码——用户切换完后会单独改 sortingOrder 公式
    ///
    /// 用法：菜单 Tools/CryptWalker/切到 Sorting Layer 分层 → 等 Console 输出"完成"
    /// </summary>
    public static class SortingLayerFixer
    {
        private const string MenuPath = "Tools/CryptWalker/切到 Sorting Layer 分层";

        private const string MapLayerName = "Map";
        private const string CharacterLayerName = "Character";

        // prefab 路径前缀（按项目实际布局）
        private const string MapPrefabFolder = "Assets/Resources/Maps";
        private const string CharacterPrefabFolder = "Assets/Art/Prefabs";

        [MenuItem(MenuPath)]
        public static void Run()
        {
            int step = 0;
            const int totalSteps = 4;

            // 1) 注入 Sorting Layer
            step++;
            EditorUtility.DisplayProgressBar("Sorting Layer 切换", "1/4 注入 Project Settings...", step / (float)totalSteps);
            InjectSortingLayers();
            Debug.Log($"[SortingLayerFixer] {step}/{totalSteps} Project Settings 已注入 {MapLayerName} / {CharacterLayerName}");

            // 2) 房间 prefab Tilemap
            step++;
            EditorUtility.DisplayProgressBar("Sorting Layer 切换", "2/4 房间 Tilemap...", step / (float)totalSteps);
            int mapCount = FixMapPrefabs();
            Debug.Log($"[SortingLayerFixer] {step}/{totalSteps} 房间 prefab 已处理 {mapCount} 个");

            // 3) 角色 prefab SpriteRenderer
            step++;
            EditorUtility.DisplayProgressBar("Sorting Layer 切换", "3/4 角色 SpriteRenderer...", step / (float)totalSteps);
            int charCount = FixCharacterPrefabs();
            Debug.Log($"[SortingLayerFixer] {step}/{totalSteps} 角色 prefab 已处理 {charCount} 个");

            // 4) 保存
            step++;
            EditorUtility.DisplayProgressBar("Sorting Layer 切换", "4/4 保存...", step / (float)totalSteps);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.ClearProgressBar();
            Debug.Log("[SortingLayerFixer] 完成！Tilemap 已在 Map 层，玩家/敌人 SpriteRenderer 已在 Character 层");
        }

        // ===================================================================
        // 1) 注入 Sorting Layer
        // ===================================================================
        private static void InjectSortingLayers()
        {
            // 拿到当前所有层
            var current = new List<string>();
            foreach (var l in InternalEditorUtility.layers)
            {
                if (!string.IsNullOrEmpty(l) && !current.Contains(l)) current.Add(l);
            }

            // 修复：InternalEditorUtility.sortingLayers 在 Unity 2022 不存在
            // 直接读 TagManager.asset 的 m_SortingLayers 数组（策划方案直接序列化层）
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var sortingLayersProp = tagManager.FindProperty("m_SortingLayers");

            var sortingLayers = new List<string>();
            for (int i = 0; i < sortingLayersProp.arraySize; i++)
            {
                string layerName = sortingLayersProp.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
                if (!string.IsNullOrEmpty(layerName)) sortingLayers.Add(layerName);
            }

            bool changed = false;
            if (!sortingLayers.Contains(MapLayerName))
            {
                sortingLayersProp.InsertArrayElementAtIndex(sortingLayers.Count);
                var newEntry = sortingLayersProp.GetArrayElementAtIndex(sortingLayers.Count);
                newEntry.FindPropertyRelative("name").stringValue = MapLayerName;
                newEntry.FindPropertyRelative("uniqueID").intValue = sortingLayers.Count;
                changed = true;
                sortingLayers.Add(MapLayerName);
            }
            if (!sortingLayers.Contains(CharacterLayerName))
            {
                sortingLayersProp.InsertArrayElementAtIndex(sortingLayers.Count);
                var newEntry = sortingLayersProp.GetArrayElementAtIndex(sortingLayers.Count);
                newEntry.FindPropertyRelative("name").stringValue = CharacterLayerName;
                newEntry.FindPropertyRelative("uniqueID").intValue = sortingLayers.Count;
                changed = true;
                sortingLayers.Add(CharacterLayerName);
            }

            if (changed)
            {
                tagManager.ApplyModifiedProperties();
                Debug.Log($"[SortingLayerFixer] TagManager 已添加: {string.Join(", ", sortingLayers)}");
            }
            else
            {
                Debug.Log("[SortingLayerFixer] Sorting Layer 已存在，跳过");
            }
        }

        // ===================================================================
        // 2) 房间 prefab Tilemap（Floor/Wall 子物体）
        // ===================================================================
        private static int FixMapPrefabs()
        {
            int count = 0;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { MapPrefabFolder });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                bool modified = false;
                foreach (var tilemapRenderer in prefab.GetComponentsInChildren<TilemapRenderer>(true))
                {
                    // 按子物体名字区分 Floor/Wall（项目里的命名约定）
                    string parentName = tilemapRenderer.transform.parent != null ? tilemapRenderer.transform.parent.name : tilemapRenderer.name;
                    int desiredOrder = parentName == "Wall" ? 1 : 0;

                    if (tilemapRenderer.sortingLayerName != MapLayerName || tilemapRenderer.sortingOrder != desiredOrder)
                    {
                        tilemapRenderer.sortingLayerName = MapLayerName;
                        tilemapRenderer.sortingOrder = desiredOrder;
                        EditorUtility.SetDirty(tilemapRenderer);
                        modified = true;
                    }
                }

                if (modified)
                {
                    EditorUtility.SetDirty(prefab);
                    count++;
                    Debug.Log($"[SortingLayerFixer] {path}  Tilemap 已设到 {MapLayerName} 层");
                }
            }
            return count;
        }

        // ===================================================================
        // 3) 角色 prefab（player + Orc*）SpriteRenderer
        // ===================================================================
        private static int FixCharacterPrefabs()
        {
            int count = 0;
            // 玩家 prefab + Orc1/2/3
            var names = new[] { "player.prefab", "Orc1.prefab", "Orc2.prefab", "Orc3.prefab" };
            foreach (var n in names)
            {
                string path = $"{CharacterPrefabFolder}/{n}";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                bool modified = false;
                foreach (var sr in prefab.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr.sortingLayerName != CharacterLayerName)
                    {
                        sr.sortingLayerName = CharacterLayerName;
                        sr.sortingOrder = 0;  // Character 层内默认 0（Y 排序由代码动态设）
                        EditorUtility.SetDirty(sr);
                        modified = true;
                    }
                }

                if (modified)
                {
                    EditorUtility.SetDirty(prefab);
                    count++;
                    Debug.Log($"[SortingLayerFixer] {path}  SpriteRenderer 已设到 {CharacterLayerName} 层");
                }
            }
            return count;
        }
    }
}
#endif