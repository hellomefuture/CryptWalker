#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 敌人动画循环修正器。
    /// 背景：早期生成的 clip 只有 Idle 循环，Walk/Run 播一遍就停（敌人巡逻/追击会卡）。
    /// 本工具把已有 clip 中 Idle/Walk/Run 的 loopTime 打开，不改帧数据。
    /// 用法：菜单 Tools/CryptWalker/修正敌人动画循环
    /// </summary>
    public static class EnemyLoopFixer
    {
        private const string AnimRoot = "Assets/Art/EnemyAnimator";
        private const string Menu     = "Tools/CryptWalker/修正敌人动画循环";

        private static readonly string[] Enemies = { "Orc1", "Orc2", "Orc3", "Vampires1", "Vampires2", "Vampires3" };
        private static readonly string[] DirSuffixes = { "South", "Left", "Right", "North" };

        [MenuItem(Menu)]
        public static void Run()
        {
            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimRoot });
            int fixedCount = 0;

            foreach (var guid in clips)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) continue;

                string action = ParseAction(clip.name);
                bool loop = action is "Idle" or "Walk" or "Run";

                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                if (settings.loopTime != loop)
                {
                    settings.loopTime = loop;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    EditorUtility.SetDirty(clip);
                    fixedCount++;
                    Debug.Log($"[EnemyLoopFixer] {clip.name}: loopTime -> {loop}");
                }
            }

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("敌人动画循环", $"已修正 {fixedCount} 个 clip（Idle/Walk/Run 设为循环）", "OK");
        }

        /// <summary>从 clip 名解析动作：Orc1_AttackSouth → Attack（去掉敌人前缀 + 方向后缀）</summary>
        private static string ParseAction(string clipName)
        {
            string name = clipName;
            foreach (var enemy in Enemies)
                if (name.StartsWith(enemy + "_")) { name = name.Substring((enemy + "_").Length); break; }

            foreach (var dir in DirSuffixes)
                if (name.EndsWith(dir)) { name = name.Substring(0, name.Length - dir.Length); break; }

            return name;
        }
    }
}
#endif
