#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 敌人序列帧图集自动切片工具。
    /// 处理 Art/Character/Enemy/**/WithShadow/*.png（单行序列帧图集）。
    ///
    /// 分两个阶段（关键）：
    ///   Pass 1：设 isReadable + 真正导入，读像素检测每帧边界
    ///           —— 必须在 StartAssetEditing 块之外，否则 SaveAndReimport 被延迟，纹理读不到
    ///   Pass 2：批量写入 Sprite(Multiple) + spritesheet + PPU 64 + Point + 无压缩
    ///
    /// 用法：菜单 Tools/CryptWalker/敌人图集自动切片
    /// </summary>
    public static class EnemySpriteImporter
    {
        private const string EnemyRoot = "Assets/Art/Character/Enemy";
        private const string Menu = "Tools/CryptWalker/敌人图集自动切片";

        [MenuItem(Menu)]
        public static void Run()
        {
            if (!Directory.Exists(EnemyRoot))
            {
                EditorUtility.DisplayDialog("失败", $"找不到目录：{EnemyRoot}", "OK");
                return;
            }

            string[] files = Directory.GetFiles(EnemyRoot, "*.png", SearchOption.AllDirectories);
            var warnings = new List<string>();
            int totalFrames = 0;

            // ========== Pass 1：读像素检测帧边界（不在批量块内）==========
            var detected = new Dictionary<string, SpriteMetaData[]>();   // assetPath -> 帧
            foreach (var file in files)
            {
                string assetPath = file.Replace("\\", "/").Replace(Application.dataPath, "Assets");

                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) { warnings.Add($"{Path.GetFileName(assetPath)}: importer 为空"); continue; }

                // 关键：先切到 Single 清空 spritesheet + nameFileIdTable（脏表会导致
                // "Identifier uniqueness violation" 同名 sprite FileId 撞车），再重新切
                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                }

                importer.isReadable = true;
                importer.SaveAndReimport();      // 立即真正导入，纹理才可读
                AssetDatabase.Refresh();

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex == null || !tex.isReadable)
                {
                    warnings.Add($"{Path.GetFileName(assetPath)}: 纹理仍不可读，跳过");
                    continue;
                }

                var rects = DetectFrames(tex, assetPath);
                if (rects.Count <= 1)
                    warnings.Add($"{Path.GetFileName(assetPath)}: 只检测到 {rects.Count} 帧（可能帧间无透明间隙，需手动 Slice）");
                detected[assetPath] = rects.ToArray();
                totalFrames += rects.Count;
            }

            // ========== Pass 2：逐个写入切片设置 ==========
            // ⚠️ 不能包在 StartAssetEditing 里（旧版 bug 根因）：批量块内 SaveAndReimport
            // 被延迟，与 importer 状态变更合并成两次导入 → sprite 重复生成 → FileId 撞车。
            // 玩家工具（PlayerAnimClipGenerator）一直是顺序导入，从未报错 —— 对齐它。
            foreach (var kv in detected)
            {
                var importer = AssetImporter.GetAtPath(kv.Key) as TextureImporter;
                if (importer == null) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.alphaIsTransparency = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spritePixelsPerUnit = 16;   // 与地图 Tile 图集 PPU 一致（用户确认）
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteGenerateFallbackPhysicsShape = false;
                importer.SetTextureSettings(settings);

                importer.spritesheet = kv.Value;
                importer.isReadable = false;   // 运行时不需要可读，省内存
                importer.SaveAndReimport();   // 顺序导入：立即生效，不延迟
            }
            AssetDatabase.Refresh();

            string msg = $"处理 {files.Length} 个图集，共切出 {totalFrames} 帧。" +
                         (warnings.Count > 0 ? $"\n\n警告 {warnings.Count} 条（需手动检查）：\n{string.Join("\n", warnings)}" : "\n全部自动切片成功");
            Debug.Log($"[EnemySpriteImporter] {msg}");
            EditorUtility.DisplayDialog("敌人图集切片", msg, "OK");
        }

        /// <summary>
        /// 按 64×64 网格切片（图集 = 4 行 × N 列，每帧 64×64）。
        /// 跳过全透明格子（避免一堆空帧），命名 {文件名}_{行}_{列}（行优先，方便 Animator 按行定位）。
        /// </summary>
        private static List<SpriteMetaData> DetectFrames(Texture2D tex, string assetPath)
        {
            const int CELL = 64;
            var pixels = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            int cols = w / CELL, rows = h / CELL;
            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            var rects = new List<SpriteMetaData>();

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var cell = new Rect(c * CELL, r * CELL, CELL, CELL);
                    if (IsCellEmpty(pixels, w, h, cell)) continue;   // 全透明格跳过
                    string spriteName = $"{baseName}_{r}_{c}";
                    rects.Add(new SpriteMetaData
                    {
                        name = spriteName,
                        rect = cell,
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        // 2022.3 无 spriteID/internalID API，无需显式设置 ——
                        // Unity 导入时会按名字自动分配稳定 ID（玩家图集同款做法，已验证可行）
                    });
                }
            }
            return rects;
        }

        /// <summary>判断格子内是否全透明（GetPixels32 原点在左下，与 Rect 一致）</summary>
        private static bool IsCellEmpty(Color32[] pixels, int w, int h, Rect cell)
        {
            int x0 = (int)cell.x, y0 = (int)cell.y;
            for (int y = y0; y < y0 + (int)cell.height; y++)
                for (int x = x0; x < x0 + (int)cell.width; x++)
                    if (pixels[y * w + x].a > 8) return false;
            return true;
        }
    }
}
#endif
