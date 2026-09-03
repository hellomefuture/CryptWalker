using System.IO;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 把 Swordsman lvl1 8 方向 sprite sheet 批量切成 64x64 多 sprite。
    /// 切片规则:
    ///   - 4 行 × N 列 (行 = 动画帧序号 0..3, 列 = 8 方向)
    ///   - 每张图按文件名推断帧数: width / 64
    ///   - 切片后只保留"带阴影"版本,作为战斗内 sprite
    ///
    /// 命名约定:
    ///   Swordsman_lvl1_{Action}_with_shadow.png
    ///   -> 切出 N×8 个 sprite, 按 方向_列_行.png 命名
    ///   例: Swordsman_lvl1_idle_with_shadow.png 的 5 列 2 行 -> idle_dir5_frame2
    ///
    /// 实际使用: 我们用 Animator 重建,所以这里只设置 TextureImporter 的 Multiple 模式,
    /// 不生成额外的 PNG。运行时按 [dir, frame] 索引到具体 sprite。
    /// </summary>
    public static class SliceSwordsmanSpriteSheets
    {
        private const string FOLDER = "Assets/Art/Character/Swordsman/lvl1/WithShadow";
        private const int CELL = 64;
        private const string MENU = "Tools/CryptWalker/Slice:Swordsman lvl1 8 方向 SpriteSheet";

        [MenuItem(MENU)]
        public static void Run()
        {
            var abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FOLDER));
            if (!Directory.Exists(abs))
            {
                EditorUtility.DisplayDialog("失败", $"找不到目录:{FOLDER}", "OK");
                return;
            }

            var pngs = Directory.GetFiles(abs, "*.png", SearchOption.TopDirectoryOnly);
            int n = 0;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace("\\", "/");
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var p in pngs)
                {
                    var normalized = Path.GetFullPath(p).Replace("\\", "/");
                    var assetPath = normalized.StartsWith(projectRoot)
                        ? normalized.Substring(projectRoot.Length).TrimStart('/')
                        : normalized;
                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer == null) continue;

                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (tex == null) continue;

                    int cols = tex.width / CELL;
                    int rows = tex.height / CELL;
                    if (cols <= 0 || rows <= 0) continue;

                    // 基础设置
                    importer.textureType         = TextureImporterType.Sprite;
                    importer.spriteImportMode    = SpriteImportMode.Multiple;
                    importer.alphaIsTransparency = true;
                    importer.alphaSource         = TextureImporterAlphaSource.FromInput;
                    importer.mipmapEnabled       = false;
                    importer.filterMode          = FilterMode.Point;
                    importer.wrapMode            = TextureWrapMode.Clamp;
                    importer.npotScale           = TextureImporterNPOTScale.None;
                    importer.textureCompression  = TextureImporterCompression.Uncompressed;
                    importer.spritesheet         = ToSpriteSheet(tex, cols, rows, assetPath);

                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    settings.spritePixelsPerUnit = 64;
                    settings.spriteGenerateFallbackPhysicsShape = false;
                    importer.SetTextureSettings(settings);

                    importer.SaveAndReimport();
                    Debug.Log($"[Slice] {assetPath} -> {cols}列 x {rows}行 (单格 {CELL}x{CELL})");
                    n++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            EditorUtility.DisplayDialog("完成",
                $"已切片 {n} 张 sprite sheet。\n" +
                $"每张切为 {CELL}x{CELL} 的多 sprite。\n" +
                $"打开任一 PNG 的 Inspector 即可看到所有子 sprite 列表。",
                "OK");
        }

        /// <summary>
        /// 把一张大图按 cols × rows 切出 SpriteMetaData 列表。
        /// 索引规则: sprite[k] = 第 (k / cols) 行, 第 (k % cols) 列。
        /// 行优先 = 同一行内是同一方向的不同帧(0,1,2,3)。
        /// 策划约定 8 方向: col 0=下, 1=下右, 2=右, 3=上右, 4=上, 5=上左, 6=左, 7=下左 (按资源包约定)
        /// </summary>
        private static SpriteMetaData[] ToSpriteSheet(Texture2D tex, int cols, int rows, string assetPath)
        {
            var metas = new SpriteMetaData[cols * rows];
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    var meta = new SpriteMetaData
                    {
                        name = $"r{row}_c{col}",
                        rect = new Rect(col * CELL, (rows - 1 - row) * CELL, CELL, CELL),
                        pivot = new Vector2(0.5f, 0.5f),
                        alignment = (int)SpriteAlignment.Center,
                        border = Vector4.zero
                    };
                    metas[row * cols + col] = meta;
                }
            }
            return metas;
        }
    }
}
