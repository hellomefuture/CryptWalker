using System.IO;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 批量把 Assets/Art/environment/*.png 的 TextureImporter 设为：
    ///   Texture Type: Sprite (2D and UI)
    ///   Sprite Mode:  Single
    ///   Pixels Per Unit: 64（与 1 Tile=1 世界单位 对齐）
    ///   Alpha Is Transparency: true
    ///   Mesh Type: Full Rect
    ///   Filter Mode: Point（保留像素硬边）
    ///   Compression: None
    /// </summary>
    public static class SetSpriteImportSettings
    {
        private const string FOLDER = "Assets/Art/environment";
        private const string MENU   = "Tools/CryptWalker/设置 environment PNG 为 Sprite";

        [MenuItem(MENU)]
        public static void Run()
        {
            var abs = Path.Combine(Application.dataPath, "..", FOLDER);
            if (!Directory.Exists(abs))
            {
                EditorUtility.DisplayDialog("失败", $"找不到目录：{FOLDER}", "OK");
                return;
            }

            var pngs = Directory.GetFiles(abs, "*.png", SearchOption.TopDirectoryOnly);
            int n = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var p in pngs)
                {
                    var assetPath = p.Replace("\\", "/").Replace(Application.dataPath, "Assets");
                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer == null) continue;

                    importer.textureType         = TextureImporterType.Sprite;
                    importer.spriteImportMode    = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.alphaSource         = TextureImporterAlphaSource.FromInput;
                    importer.mipmapEnabled       = false;
                    importer.filterMode          = FilterMode.Point;
                    importer.wrapMode            = TextureWrapMode.Clamp;
                    importer.npotScale           = TextureImporterNPOTScale.None;
                    importer.textureCompression  = TextureImporterCompression.Uncompressed;

                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    settings.spritePixelsPerUnit = 64;
                    settings.spriteGenerateFallbackPhysicsShape = false;
                    importer.SetTextureSettings(settings);

                    importer.SaveAndReimport();
                    Debug.Log($"[SpriteImport] {assetPath} -> Sprite 64ppu, alpha");
                    n++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            EditorUtility.DisplayDialog("完成", $"已处理 {n} 张 PNG。\n已设为 Sprite (2D and UI)，64 PPU，点过滤，无压缩。", "OK");
        }
    }
}
