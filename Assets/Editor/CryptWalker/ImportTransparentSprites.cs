using System.IO;
using UnityEditor;
using UnityEngine;
using System.Linq;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 把 Tools/output_pngs/ 下的 .png 复制到 Assets/Art/ 对应子目录。
    /// 然后扫描 Assets/Art/，删除 .jpg（已有 .png 替代）。
    /// </summary>
    public static class ImportTransparentSprites
    {
        private const string SRC = "Tools/output_pngs";
        private const string DST = "Assets/Art";
        private const string MENU = "Tools/CryptWalker/抠图导入：复制PNG并删除JPG";

        [MenuItem(MENU)]
        public static void Run()
        {
            string src = Path.Combine(Application.dataPath, "..", SRC);
            string dst = Path.Combine(Application.dataPath, "..", DST);

            if (!Directory.Exists(src))
            {
                EditorUtility.DisplayDialog("失败", $"找不到源目录：{src}\n请先跑 Tools/remove_bg.py 生成 PNG。", "OK");
                return;
            }

            int copyCount = 0;
            int delCount = 0;
            try
            {
                AssetDatabase.StartAssetEditing();

                // 1) 复制 png：保留子目录结构
                foreach (var srcFile in Directory.GetFiles(src, "*.png", SearchOption.AllDirectories))
                {
                    var rel = srcFile.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                    var dstFile = Path.Combine(dst, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dstFile));
                    File.Copy(srcFile, dstFile, overwrite: true);
                    Debug.Log($"[Import] 复制 {rel}");
                    copyCount++;
                }

                // 2) 删除 .jpg：只在 png 已存在的情况下删
                foreach (var jpgFile in Directory.GetFiles(dst, "*.jpg", SearchOption.AllDirectories))
                {
                    var pngSame = Path.ChangeExtension(jpgFile, ".png");
                    if (File.Exists(pngSame))
                    {
                        File.Delete(jpgFile);
                        // 同步删 .meta
                        var meta = jpgFile + ".meta";
                        if (File.Exists(meta)) File.Delete(meta);
                        Debug.Log($"[Import] 删除 {Path.GetFileName(jpgFile)}（已用 PNG 替代）");
                        delCount++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // 触发 Unity 重新导入
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            EditorUtility.DisplayDialog(
                "完成",
                $"已复制 {copyCount} 个 PNG\n已删除 {delCount} 个旧 JPG\n\n" +
                "PNG 已自动导入为透明底 Sprite。\n" +
                "记得把每个 PNG 的 Inspector → Texture Type 设为 Sprite（2D and UI）→ Apply。",
                "OK"
            );
        }
    }
}
