#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 敌人动画 Clip 生成器（4 方向版）。
    /// 规则（对齐玩家协议：FaceDir 1=South 2=West 3=East 4=North）：
    ///   1. 每个敌人 PNG 图集（已 64×64 切片）= 4 行，每行是一个方向
    ///   2. 每行生成一个 clip：{敌人}_{动作}{方向}，如 Orc1_AttackSouth、Vampires1_IdleNorth
    ///   3. 采样率 12fps（与玩家一致）；Idle 循环，其余不循环
    ///   4. 输出到 Art/EnemyAnimator/
    ///
    /// ⚠️ 行序映射：素材 4 行的方向顺序不一定固定。
    /// 如果生成后方向对不上（比如朝上的动画叫了 South），改下面的 RowToDir 数组后重跑即可。
    /// </summary>
    public static class EnemyAnimClipGenerator
    {
        private const string EnemyRoot = "Assets/Art/Character/Enemy";
        private const string ClipDir   = "Assets/Art/EnemyAnimator";
        private const string Menu      = "Tools/CryptWalker/生成敌人动画Clip";

        // 行号 → 方向（用户核对后确认：行0=Right, 行1=Left, 行2=North, 行3=South）
        private static readonly string[] RowToDir = { "Right", "Left", "North", "South" };

        [MenuItem(Menu)]
        public static void Run()
        {
            Directory.CreateDirectory(ClipDir);

            var pngs = Directory.GetFiles(EnemyRoot, "*.png", SearchOption.AllDirectories);
            int count = 0;
            var errors = new List<string>();

            foreach (var png in pngs)
            {
                string texPath = png.Replace("\\", "/").Replace(Application.dataPath, "Assets");
                count += BuildClips(texPath, errors);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string msg = $"生成 {count} 个 AnimationClip → {ClipDir}" +
                         (errors.Count > 0 ? $"\n跳过（无法解析）：{string.Join(", ", errors)}" : "");
            Debug.Log($"[EnemyAnimClipGenerator] {msg}");
            EditorUtility.DisplayDialog("敌人动画", msg, "OK");
        }

        private static int BuildClips(string texPath, List<string> errors)
        {
            // ---- 1) 从文件名解析 敌人名_动作 ----
            string fileName = Path.GetFileNameWithoutExtension(texPath);
            string core = fileName.Replace("_with_shadow", "").Replace("With_shadow", "");
            string[] parts = core.Split('_');
            if (parts.Length < 2) { errors.Add(fileName); return 0; }

            string enemy = Capitalize(parts[0]);
            string action = string.Join("_", parts.Skip(1))
                .Replace("_front", "").Replace("front", "");
            string actionName = Capitalize(action);

            // ---- 2) 加载全部 sprite，按行分组 ----
            var sprites = AssetDatabase.LoadAllAssetsAtPath(texPath)
                .OfType<Sprite>()
                .OrderBy(s => s.rect.y).ThenBy(s => s.rect.x)
                .ToList();
            if (sprites.Count == 0) { errors.Add(fileName); return 0; }

            int rows = (int)(sprites.Max(s => s.rect.yMax) / 64f);
            rows = Mathf.Max(rows, 1);

            // ---- 3) 每行生成一个 clip ----
            int made = 0;
            for (int r = 0; r < rows; r++)
            {
                string dir = r < RowToDir.Length ? RowToDir[r] : $"Row{r}";
                var rowFrames = sprites
                    .Where(s => (int)s.rect.y == r * 64)
                    .OrderBy(s => s.rect.x)
                    .ToList();
                if (rowFrames.Count == 0) continue;

                var clip = new AnimationClip { frameRate = 12 };
                var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
                var keys = new ObjectReferenceKeyframe[rowFrames.Count];
                for (int i = 0; i < rowFrames.Count; i++)
                    keys[i] = new ObjectReferenceKeyframe { time = i / 12f, value = rowFrames[i] };
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                // 循环动作：Idle/Walk/Run（持续播放）；Attack/Hurt/Death 播一遍
                string a = action.ToLowerInvariant();
                settings.loopTime = a is "idle" or "walk" or "run";
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                string clipName = $"{enemy}_{actionName}{dir}";
                string subDir = $"{ClipDir}/{enemy}Animation";     // 按敌人种类分组：Orc1 → Orc1Animation/
                Directory.CreateDirectory(subDir);
                AssetDatabase.CreateAsset(clip, $"{subDir}/{clipName}.anim");
                Debug.Log($"[EnemyAnimClip] {clipName}: {rowFrames.Count} 帧 @12fps{(settings.loopTime ? " [循环]" : "")}");
                made++;
            }
            return made;
        }

        private static string Capitalize(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
    }
}
#endif
