#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 玩家动画 Clip 生成器（4 方向版，与敌人同构）。
    /// 规则：
    ///   1. 玩家图集（Swordsman/lvl1/WithShadow/*.png）= 4 行，每行一个方向
    ///   2. 每行生成一个 clip：{角色}_{动作}{方向}，如 Swordsman_IdleSouth、Swordsman_WalkAttackNorth
    ///   3. 采样率 12fps；Idle/Walk/Run/WalkAttack/RunAttack 循环，Attack/Hurt/Death 不循环
    ///   4. 输出到 Art/PlayerAnimator/{角色}Animation/
    ///   5. 图集若未切片则自动按 64×64 网格切片（4 行）
    ///
    /// ⚠️ 行序默认与敌人一致（行0=Right, 行1=Left, 行2=North, 行3=South）。
    /// 生成后方向对不上就改 RowToDir 重跑。
    /// </summary>
    public static class PlayerAnimClipGenerator
    {
        private const string CharacterRoot = "Assets/Art/Character/Player/Swordsman";
        private const string ClipRoot      = "Assets/Art/PlayerAnimator";
        private const string Menu          = "Tools/CryptWalker/生成玩家动画Clip";

        // 行号 → 方向（用户最终核对：行0=North, 行1=Right, 行2=Left, 行3=South）
        private static readonly string[] RowToDir = { "North", "Right", "Left", "South" };

        // 方向修正映射（用户反馈：left/right 反了、north/south 反了）
        private static readonly (string Old, string New)[] DirFix =
        {
            ("South", "North"), ("North", "South"),
            ("Left",  "Right"), ("Right", "Left"),
        };

        /// <summary>修正已有 clip 的方向名（两阶段：先临时名避免交换撞名，再改最终名）</summary>
        [MenuItem("Tools/CryptWalker/修正玩家动画方向")]
        public static void RenameFix()
        {
            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { $"{ClipRoot}/{charName}Animation" });
            var paths = guids.Select(g => AssetDatabase.GUIDToAssetPath(g)).ToList();
            int n = 0;

            // Phase 1：全部先改成临时名（交换改名会撞名，必须两阶段）
            foreach (string path in paths)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                foreach (var (oldSuffix, newSuffix) in DirFix)
                {
                    if (!name.EndsWith(oldSuffix)) continue;
                    string dir = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                    AssetDatabase.RenameAsset(path, $"TMP_{name.Substring(0, name.Length - oldSuffix.Length) + newSuffix}");
                    break;
                }
            }
            AssetDatabase.SaveAssets();

            // Phase 2：临时名 → 最终名
            var tmpGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { $"{ClipRoot}/{charName}Animation" });
            foreach (string guid in tmpGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("TMP_")) continue;
                AssetDatabase.RenameAsset(path, name.Substring(4));
                n++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("玩家动画", $"已修正 {n} 个 clip 的方向名", "OK");
        }

        [MenuItem(Menu)]
        public static void Run()
        {
            Directory.CreateDirectory(ClipRoot);

            var pngs = Directory.GetFiles(CharacterRoot, "*.png", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith("shadow_single.png") && !p.EndsWith("shadow_death.png")) // 阴影单独文件跳过
                .ToArray();
            if (pngs.Length == 0)
            {
                EditorUtility.DisplayDialog("玩家动画", $"未在 {CharacterRoot} 找到图集", "OK");
                return;
            }

            int count = 0;
            var errors = new List<string>();
            foreach (var png in pngs)
            {
                string texPath = png.Replace("\\", "/").Replace(Application.dataPath, "Assets");
                EnsureSliced(texPath);
                count += BuildClips(texPath, errors);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string msg = $"生成 {count} 个 AnimationClip → {ClipRoot}/{charName}Animation" +
                         (errors.Count > 0 ? $"\n跳过（无法解析）：{string.Join(", ", errors)}" : "");
            Debug.Log($"[PlayerAnimClipGenerator] {msg}");
            EditorUtility.DisplayDialog("玩家动画", msg, "OK");
        }

        private const string charName = "Swordsman";

        /// <summary>确保图集已设为 Sprite(Multiple) 且按 64×64 网格切片</summary>
        private static void EnsureSliced(string texPath)
        {
            var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (ti == null) return;

            bool changed = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
            if (ti.spriteImportMode != SpriteImportMode.Multiple) { ti.spriteImportMode = SpriteImportMode.Multiple; changed = true; }

            if (ti.spritesheet == null || ti.spritesheet.Length == 0)
            {
                bool prevReadable = ti.isReadable;
                ti.isReadable = true;
                ti.SaveAndReimport();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                int cols = tex.width / 64, rows = tex.height / 64;
                ti.isReadable = prevReadable;

                var metas = new List<SpriteMetaData>();
                string baseName = Path.GetFileNameWithoutExtension(texPath);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        string spriteName = $"{baseName}_{r}_{c}";
                        metas.Add(new SpriteMetaData
                        {
                            name = spriteName,
                            rect = new Rect(c * 64, r * 64, 64, 64),
                            // 2022.3 无 spriteID/internalID API —— Unity 导入时按名字自动分配稳定 ID
                        });
                    }
                ti.spritesheet = metas.ToArray();
                changed = true;
            }

            if (changed) ti.SaveAndReimport();
        }

        private static int BuildClips(string texPath, List<string> errors)
        {
            // ---- 1) 从文件名解析 动作：Swordsman_lvl1_Walk_Attack → WalkAttack ----
            string fileName = Path.GetFileNameWithoutExtension(texPath);
            string core = fileName.Replace("_with_shadow", "").Replace("With_shadow", "");
            string rest = core.StartsWith(charName) ? core.Substring(charName.Length) : core;
            var segs = rest.Split('_')
                .Where(s => !string.IsNullOrEmpty(s)
                            && !s.StartsWith("lvl", System.StringComparison.OrdinalIgnoreCase)) // 跳过 lvl1 等级段
                .ToList();
            if (segs.Count == 0) { errors.Add(fileName); return 0; }
            string action = string.Join("", segs.Select(Capitalize));   // Walk_Attack → WalkAttack

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
                string a = action.ToLowerInvariant();
                settings.loopTime = a is "idle" or "walk" or "run" or "walkattack" or "runattack";
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                string clipName = $"{charName}_{action}{dir}";       // Swordsman_WalkAttackSouth
                string subDir = $"{ClipRoot}/{charName}Animation";   // SwordsmanAnimation/
                Directory.CreateDirectory(subDir);
                AssetDatabase.CreateAsset(clip, $"{subDir}/{clipName}.anim");
                Debug.Log($"[PlayerAnimClip] {clipName}: {rowFrames.Count} 帧 @12fps{(settings.loopTime ? " [循环]" : "")}");
                made++;
            }
            return made;
        }

        private static string Capitalize(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
    }
}
#endif
