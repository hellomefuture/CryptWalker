#if UNITY_EDITOR
using System.IO;
using CryptWalker.HotUpdate.Combat;
using CryptWalker.HotUpdate.Data;
using CryptWalker.HotUpdate.Entity.Player;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 独立武器装配工具（元气骑士式：武器是玩家身上的一个支点节点 + 一张贴图）。
    ///
    /// 分两步，对应两个菜单：
    ///   ① 导出武器图：把美术做在 UI_Weapon 图集里的 Sword / Bow / Arrow 三个切片，
    ///      裁成独立 PNG 放进 Assets/Art/Weapon/Player/，并按【手持武器规范】导入：
    ///        Single / PPU=16 / Point / 不压缩 / Pivot 沿用源切片（握把、中心、尾巴）
    ///      —— 为什么要导出而不是直接用图集：
    ///         同一张贴图只能有一个 PPU。图集是 UI 图标（PPU=100），
    ///         手持武器必须 PPU=16，否则尺寸和像素密度都对不上。
    ///         导出成独立文件后，图集可以原样留着当 UI 图标用，两边互不影响。
    ///   ② 装配到 player.prefab：建 Weapon_Sword.asset（若不存在）→ 给 prefab 加
    ///      WeaponPivot（挂点）+ WeaponSprite（SpriteRenderer）+ MeleeWeapon 组件，
    ///      并把 PlayerController 上的 weaponPivot / weaponRenderer / currentWeapon 一起接好。
    ///
    /// 两个菜单都可以反复跑（幂等），改完图直接重跑 ① 即可。
    /// </summary>
    public static class PlayerWeaponRigBuilder
    {
        private const string AtlasPath    = "Assets/Art/ui_Texture/UI_Weapon/UI_Weapon.png";
        private const string OutDir       = "Assets/Art/Weapon/Player";
        private const string PlayerPrefab = "Assets/Art/Prefabs/player.prefab";
        private const string DataDir      = "Assets/Art/Data";
        private const string PivotName    = "WeaponPivot";
        private const string SpriteNode   = "WeaponSprite";

        /// <summary>手持武器的 PPU（与角色一致；图集那边是 100，这是必须分开的原因）</summary>
        private const int WeaponPpu = 16;

        /// <summary>要导出/装配的武器：源切片名 → 输出文件名</summary>
        private static readonly string[][] Exports =
        {
            new[] { "UI_Weapon_Sword", "Weapon_Sword.png" },
            new[] { "UI_Weapon_Bow",   "Weapon_Bow.png"   },
            new[] { "UI_Weapon_Arrow", "Weapon_Arrow.png" },   // 箭矢是弹道，先导出，预制体以后再说
        };

        // ==================== ① 导出武器图 ====================

        [MenuItem("Tools/CryptWalker/① 导出武器图（从 UI_Weapon 图集）", priority = 120)]
        public static void ExportSprites()
        {
            if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);

            int ok = 0;
            var log = new System.Text.StringBuilder();

            foreach (var e in Exports)
            {
                string spriteName = e[0], outFile = e[1];
                if (ExportOne(spriteName, outFile, log)) ok++;
            }

            AssetDatabase.Refresh();
            string msg = $"导出完成：{ok}/{Exports.Length}\n{log}\n"
                       + $"输出目录：{OutDir}（PPU={WeaponPpu}、Point、不压缩、Pivot 沿用源切片）";
            Debug.Log($"[WeaponRig] {msg}");
            EditorUtility.DisplayDialog("导出武器图", msg, "OK");
        }

        private static bool ExportOne(string spriteName, string outFile, System.Text.StringBuilder log)
        {
            Sprite sp = FindSprite(spriteName);
            if (sp == null)
            {
                log.AppendLine($"  {spriteName}：图集里找不到这个切片（名字对不对？）");
                return false;
            }

            Texture2D src = LoadPng(AtlasPath);
            if (src == null)
            {
                log.AppendLine($"  {spriteName}：图集 PNG 读不出来");
                return false;
            }

            Rect r = sp.rect;                       // 像素坐标，原点在【左下】
            int w = Mathf.RoundToInt(r.width);
            int h = Mathf.RoundToInt(r.height);
            int x0 = Mathf.RoundToInt(r.x);
            int y0 = Mathf.RoundToInt(r.y);

            // GetPixels32 与 sprite rect 都是【左下为原点】，所以能直接按行拷贝，不用翻转
            Color32[] all = src.GetPixels32();
            var px = new Color32[w * h];
            int copied = 0;
            for (int y = 0; y < h; y++)
            {
                int srcStart = (y0 + y) * src.width + x0;
                if (srcStart < 0 || srcStart + w > all.Length) break;
                System.Array.Copy(all, srcStart, px, y * w, w);
                copied++;
            }
            Object.DestroyImmediate(src);

            if (copied != h)
            {
                log.AppendLine($"  {spriteName}：切片 rect 越界（{r}），跳过");
                return false;
            }

            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.SetPixels32(px);
            dst.Apply();

            string outPath = $"{OutDir}/{outFile}";
            File.WriteAllBytes(outPath, dst.EncodeToPNG());
            Object.DestroyImmediate(dst);
            AssetDatabase.ImportAsset(outPath);

            // 把源切片的 pivot 原样搬过来（归一化坐标，尺寸没变所以值可以直接用）
            Vector2 pivotNorm = new Vector2(sp.pivot.x / r.width, sp.pivot.y / r.height);
            ApplyWeaponImport(outPath, pivotNorm);

            log.AppendLine($"  {spriteName} → {outFile}  ({w}×{h}, pivot={pivotNorm.x:F2},{pivotNorm.y:F2})");
            return true;
        }

        private static void ApplyWeaponImport(string assetPath, Vector2 pivotNorm)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[WeaponRig] 取不到 TextureImporter：{assetPath}");
                return;
            }

            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = WeaponPpu;
            importer.filterMode          = FilterMode.Point;
            importer.textureCompression  = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled       = false;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivotNorm;
            importer.SetTextureSettings(settings);

            importer.SaveAndReimport();
        }

        // ==================== ② 装配到 player.prefab ====================

        [MenuItem("Tools/CryptWalker/② 装配独立武器到 player.prefab", priority = 121)]
        public static void BuildRig()
        {
            // —— 1) Weapon_Sword.asset（不存在就建）——
            string cfgPath = $"{DataDir}/Weapon_Sword.asset";
            WeaponConfig cfg = AssetDatabase.LoadAssetAtPath<WeaponConfig>(cfgPath);
            bool createdCfg = false;
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<WeaponConfig>();
                cfg.weaponType = EWeaponType.Melee;
                cfg.elementType = EElementType.None;
                cfg.baseAttack = 25f;
                cfg.attackSpeed = 1f;
                cfg.range = 1.5f;
                cfg.specialSkillCD = 6f;
                cfg.specialSkillType = "dash_forward";
                // 图标里刀尖的朝向（相对 +X，逆时针为正）：当前图标是「斜向右上 45°」
                cfg.spriteAngleOffset = 45f;
                AssetDatabase.CreateAsset(cfg, cfgPath);
                createdCfg = true;
            }

            // 贴图与握把偏移每次装配都刷新一次，方便改图后重跑
            Sprite weaponSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{OutDir}/Weapon_Sword.png");
            cfg.weaponSprite = weaponSprite;

            // UI 图标 = 图集里的【原始】切片（PPU=100 ✓）
            // 注意与 weaponSprite 的区别 ✗：那个是【导出文件】（PPU=16 ✓），只给世界里的 SpriteRenderer 用 ✓
            //   同一张图不能两用 —— 图集的 PPU 由 UI 决定 ✓ 而手持武器必须 PPU=16 才和角色像素密度一致 ✓
            //   选武器面板（Img_Weapon*）要显示的是这里这个 icon ✓
            cfg.icon = FindSprite("UI_Weapon_Sword");

            EditorUtility.SetDirty(cfg);

            // —— 2) 改 prefab ——
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            string result;
            try
            {
                var pc = root.GetComponent<PlayerController>();
                if (pc == null)
                {
                    result = $"player.prefab 上没有 PlayerController，装不了";
                }
                else
                {
                    // 挂点
                    Transform pivot = root.transform.Find(PivotName);
                    if (pivot == null)
                    {
                        var go = new GameObject(PivotName);
                        go.transform.SetParent(root.transform, false);
                        pivot = go.transform;
                    }

                    // 挂点下的贴图节点
                    SpriteRenderer sr = pivot.GetComponentInChildren<SpriteRenderer>(true);
                    if (sr == null)
                    {
                        var go = new GameObject(SpriteNode, typeof(SpriteRenderer));
                        go.transform.SetParent(pivot, false);
                        sr = go.GetComponent<SpriteRenderer>();
                    }
                    sr.sprite = weaponSprite;
                    sr.sortingLayerName = "Character";

                    // 武器组件（近战）
                    MeleeWeapon melee = root.GetComponent<MeleeWeapon>();
                    if (melee == null) melee = root.AddComponent<MeleeWeapon>();

                    var soMelee = new SerializedObject(melee);
                    soMelee.FindProperty("config").objectReferenceValue = cfg;
                    soMelee.ApplyModifiedPropertiesWithoutUndo();

                    // 把 PlayerController 的三个引用接好
                    var so = new SerializedObject(pc);
                    so.FindProperty("weaponPivot").objectReferenceValue = pivot;
                    so.FindProperty("weaponRenderer").objectReferenceValue = sr;
                    so.FindProperty("currentWeapon").objectReferenceValue = melee;
                    so.ApplyModifiedPropertiesWithoutUndo();

                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);

                    result = $"player.prefab 已装配：{PivotName} / {SpriteNode} / MeleeWeapon(config={cfg.name})\n"
                           + $"PlayerController：weaponPivot / weaponRenderer / currentWeapon 已接好";
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string msg = (createdCfg ? $"已新建 {cfgPath}（近战/攻击 25/范围 1.5）\n" : $"{cfgPath} 已存在，只更新贴图\n")
                       + result + "\n\n"
                       + (weaponSprite == null
                           ? "⚠️ 没找到 Weapon_Sword.png —— 先跑【① 导出武器图】"
                           : $"武器贴图：{weaponSprite.name}（PPU={WeaponPpu}）");
            Debug.Log($"[WeaponRig] {msg}");
            EditorUtility.DisplayDialog("装配独立武器", msg, "OK");
        }

        // ==================== 小工具 ====================

        private static Sprite FindSprite(string spriteName)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AtlasPath))
                if (o is Sprite sp && sp.name == spriteName) return sp;
            return null;
        }

        /// <summary>直接解 PNG，避免依赖该资产的导入设置（图集是 Multiple 切片）</summary>
        private static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(tex);
                return null;
            }
            return tex;
        }
    }
}
#endif
