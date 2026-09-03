#if UNITY_EDITOR
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.Editor
{
    /// <summary>
    /// Project 窗口右上角 "在文件资源管理器中打开" 按钮
    /// 选中任意资源/文件夹，右上角的 "Open" 按钮会拉起系统资源管理器并定位到该资源
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectWindowOpenButton
    {
        static ProjectWindowOpenButton()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
        }

        private static void OnProjectWindowItemGUI(string guid, Rect rect)
        {
            // 只在 Project 窗口最顶部的选中项位置绘制（取 ListMode 顶部 16px 区域）
            // 简单做法：检测 rect.y < 20 但 rect 是真实资产 Rect，改用 Selection 判断
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            // 只对单选做按钮
            if (Selection.count != 1) return;
            var selGuid = Selection.assetGUIDs;
            if (selGuid == null || selGuid.Length != 1) return;
            if (selGuid[0] != guid) return;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return;

            // 按钮位置 = 当前选中项的 rect 右上角偏内
            // Project 窗口 OneColumn 下 rect 高度 16；TwoColumn 高度也是 16
            const float btnWidth = 60f;
            const float btnHeight = 16f;
            var btnRect = new Rect(rect.xMax - btnWidth - 2f, rect.y, btnWidth, btnHeight);
            if (GUI.Button(btnRect, "Open", EditorStyles.miniButton))
            {
                OpenInExplorer(path);
                Event.current.Use();
            }
        }

        private static void OpenInExplorer(string assetPath)
        {
            // 资产路径 → 绝对路径
            string abs = System.IO.Path.GetFullPath(assetPath);
            if (System.IO.Directory.Exists(abs))
            {
                // 文件夹：在资源管理器中打开并定位
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/e,/select,\"{abs}\"",
                    UseShellExecute = true
                });
            }
            else if (System.IO.File.Exists(abs))
            {
                // 文件：定位到文件
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/e,/select,\"{abs}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                // 兜底：打开 Assets 根目录
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{System.IO.Path.GetFullPath("Assets")}\"",
                    UseShellExecute = true
                });
            }
        }
    }
}
#endif