#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 「无武器」动作生成器 —— 用美术的分层素材（lvl1/Parts/）合成出不含剑的动作。
    ///
    /// ===== 为什么需要 =====
    /// 现有 32 个 clip 用的是 WithShadow/ 里【已经合成好、剑烘进每一帧】的图，
    /// 所以玩家「没装备武器也永远举着剑」。
    /// 而 Parts/ 是按部位分层的（shadow / body / head / sword / sword_back），
    /// 只要【不叠加 sword 与 sword_back】就能得到无武器版本 ——
    /// 也就是美术需求清单 §4.1 要求的那 3 张图（Idle / Walk / Run），一张都不用重画。
    /// （清单 §7 当初就推测素材包里有分层版，这就是。）
    ///
    /// ===== 这个工具做什么 =====
    ///   1. 直接解 Parts/{动作}_{shadow|body|head}.png 的像素（不依赖导入设置）
    ///   2. 按 shadow → body → head 做 alpha 合成 → 写出 NoWeapon/{动作}_no_weapon.png
    ///   3. 按美术清单 §8 的规范导入：Multiple 切片 / 64×64 / PPU=16 / Point / 不压缩
    ///      行序 South / Left / Right / North（从上到下），pivot 居中
    ///   4. 生成 4 个 clip：Swordsman_{动作}{方向}_NoWeapon
    ///      —— 与原版【并存，不覆盖】，以后接上武器还能用回带剑的版本
    ///
    /// ===== 两个必须知道的细节 =====
    ///   · **每方向的帧数不一样**：清单 §3 备注「Idle 的 North 只有 4 帧，其他方向 12 帧」。
    ///     图集里那几格是空的 —— 按固定列数硬切会做出「8 帧全透明」的 clip（待机时闪）。
    ///     所以这里对每一行【自动探测真正有内容的帧区间】，只把有效帧写进 clip。
    ///     现有 Swordsman_IdleNorth.anim 就是 4 帧 / stopTime=0.333，本工具跟它保持一致。
    ///   · **stopTime = 帧数 / 12**（采样率 12）：IdleSouth 12 帧→1.0s、WalkSouth 6 帧→0.5s、
    ///     IdleNorth 4 帧→0.333s，与现有 32 个 clip 的写法完全一致。
    ///
    /// 跑完之后：Tools/CryptWalker/生成玩家AnimatorController，把新 clip 变成 Animator 状态。
    /// 运行时：PlayerController.PlayAction 在「没装备武器」时会优先用 _NoWeapon 版本。
    /// </summary>
    public static class PlayerUnarmedAnimationBuilder
    {
        private const string PartsDir = "Assets/Art/Character/Player/Swordsman/lvl1/Parts";
        private const string OutDir   = "Assets/Art/Character/Player/Swordsman/lvl1/NoWeapon";
        private const string ClipDir  = "Assets/Art/PlayerAnimator/SwordsmanAnimation";

        private const int Cell       = 64;    // 每帧 64×64（美术清单 §8）
        private const int Rows       = 4;     // 4 行 = 4 个方向
        private const int Ppu        = 16;    // PPU = 16（美术清单 §8）
        private const int SampleRate = 12;    // 采样率与现有 32 个 clip 一致

        /// <summary>行序（从上到下）—— 与 PlayerController.DirName 的 1..4 完全一致</summary>
        private static readonly string[] RowDirs = { "South", "Left", "Right", "North" };

        /// <summary>各动作的列数（图集列数）—— 来自美术清单 §3 的实测表</summary>
        private static readonly Dictionary<string, int> ActionCols = new Dictionary<string, int>
        {
            { "Idle", 12 }, { "Walk", 6 }, { "Run", 8 }, { "Hurt", 5 }, { "Death", 7 },
        };

        /// <summary>
        /// 本次要生成的动作。清单 §4.1 要的就是这 3 个（Idle / Walk / Run）。
        /// 想再做 §4.3 的受击 / 死亡无剑版，加 "Hurt" / "Death" 即可。
        /// </summary>
        private static readonly string[] Actions = { "Idle", "Walk", "Run" };

        [MenuItem("Tools/CryptWalker/生成无武器动作（Parts 合成）", priority = 100)]
        public static void Run()
        {
            if (!Directory.Exists(OutDir))
            {
                Directory.CreateDirectory(OutDir);
                AssetDatabase.Refresh();
            }

            var log = new StringBuilder();
            int doneActions = 0, totalClips = 0;

            foreach (string action in Actions)
            {
                if (!ActionCols.TryGetValue(action, out int cols))
                {
                    log.AppendLine($"  跳过 {action}：不在帧数表里（请补 ActionCols）");
                    continue;
                }

                string bodyPath = PartPath(action, "body");
                if (!File.Exists(bodyPath))
                {
                    log.AppendLine($"  跳过 {action}：找不到 {Path.GetFileName(bodyPath)}");
                    continue;
                }

                int[] first, last;
                Texture2D composed = Compose(action, cols, out first, out last, out string err);
                if (composed == null)
                {
                    log.AppendLine($"  跳过 {action}：{err}");
                    continue;
                }

                string outFile = $"{OutDir}/Swordsman_lvl1_{action}_no_weapon.png";
                File.WriteAllBytes(outFile, composed.EncodeToPNG());
                Object.DestroyImmediate(composed);
                AssetDatabase.Refresh();

                ApplyImportSettings(outFile, action, cols);
                int made = BuildClips(outFile, action, cols, first, last, log);

                doneActions++;
                totalClips += made;
                log.AppendLine($"  {action}：合成 1 张（{cols} 列 × {Rows} 行）+ 生成 {made} 个 clip");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string msg = $"无武器动作生成完成：{doneActions} 个动作 / {totalClips} 个 clip\n{log}\n"
                       + "下一步：Tools/CryptWalker/生成玩家AnimatorController（把新 clip 变成 Animator 状态）\n"
                       + "运行时：没装备武器时会自动优先播 _NoWeapon 版本（已接好，不用改代码）。";
            Debug.Log($"[UnarmedBuilder] {msg}");
            EditorUtility.DisplayDialog("无武器动作", msg, "OK");
        }

        // ==================== 合成 ====================

        private static string PartPath(string action, string part)
            => $"{PartsDir}/Swordsman_lvl1_{action}_{part}.png";

        /// <summary>
        /// 把 shadow / body / head 三层合成为一张（故意不叠加 sword 与 sword_back）。
        /// shadow 与 head 允许缺（缺了就不叠），body 必须有 —— 它决定 canvas 尺寸。
        /// 顺带算出每一行【有效帧区间】，供生成 clip 时用（见类注释）。
        /// </summary>
        private static Texture2D Compose(string action, int cols,
                                         out int[] first, out int[] last, out string error)
        {
            error = null;
            first = null;
            last = null;
            int w = cols * Cell, h = Rows * Cell;

            Texture2D body = LoadPng(PartPath(action, "body"));
            if (body == null) { error = "body 图读不出来"; return null; }
            if (body.width != w || body.height != h)
            {
                error = $"body 尺寸 {body.width}×{body.height}，与预期 {w}×{h}（{cols} 列 × {Rows} 行 × {Cell}）不符";
                Object.DestroyImmediate(body);
                return null;
            }

            var px = new Color32[w * h];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            Over(px, PartPath(action, "shadow"));   // 影子在最底
            Over(px, PartPath(action, "body"));     // 身体
            Over(px, PartPath(action, "head"));     // 头在最上
            // ⚠️ 这里就是"没有武器"：不叠 sword / sword_back

            FindRowRanges(px, w, cols, out first, out last);

            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.SetPixels32(px);
            dst.Apply();

            Object.DestroyImmediate(body);
            return dst;
        }

        /// <summary>
        /// 逐行找出「真正有内容」的帧区间（含首含尾）。
        /// 判据 = 该格内是否存在任何非透明像素（影子也算，所以"只有影子没身体"不会被误判成空帧）。
        /// 取的是首尾范围而不是简单裁尾：这样无论素材是左对齐还是右对齐都能正确处理。
        /// </summary>
        private static void FindRowRanges(Color32[] px, int w, int cols, out int[] first, out int[] last)
        {
            first = new int[Rows];
            last = new int[Rows];
            for (int row = 0; row < Rows; row++)
            {
                first[row] = -1;
                last[row] = -1;
                for (int col = 0; col < cols; col++)
                {
                    if (!CellHasPixel(px, w, row, col)) continue;
                    if (first[row] < 0) first[row] = col;
                    last[row] = col;
                }
            }
        }

        private static bool CellHasPixel(Color32[] px, int w, int row, int col)
        {
            int x0 = col * Cell;
            int y0 = (Rows - 1 - row) * Cell;   // 行 0（South）在图集最上面
            for (int y = y0; y < y0 + Cell; y++)
            {
                int rowBase = y * w;
                for (int x = x0; x < x0 + Cell; x++)
                    if (px[rowBase + x].a != 0) return true;
            }
            return false;
        }

        /// <summary>把一张同尺寸的层按 "source over" 叠到 dst 上；尺寸不符或缺失则跳过（不报错）</summary>
        private static void Over(Color32[] dst, string partPath)
        {
            Texture2D tex = LoadPng(partPath);
            if (tex == null) return;

            if (tex.width * tex.height != dst.Length)
            {
                Debug.LogWarning($"[UnarmedBuilder] 跳过一层：{Path.GetFileName(partPath)} 尺寸 " +
                                 $"{tex.width}×{tex.height} 与底图不一致");
                Object.DestroyImmediate(tex);
                return;
            }

            Color32[] src = tex.GetPixels32();
            for (int i = 0; i < dst.Length; i++)
            {
                Color32 s = src[i];
                if (s.a == 0) continue;

                Color32 d = dst[i];
                float sa = s.a / 255f;
                float da = d.a / 255f;
                float outA = sa + da * (1f - sa);
                if (outA <= 0f) continue;

                float inv = 1f - sa;
                dst[i] = new Color32(
                    (byte)Mathf.RoundToInt((s.r * sa + d.r * da * inv) / outA),
                    (byte)Mathf.RoundToInt((s.g * sa + d.g * da * inv) / outA),
                    (byte)Mathf.RoundToInt((s.b * sa + d.b * da * inv) / outA),
                    (byte)Mathf.RoundToInt(outA * 255f));
            }
            Object.DestroyImmediate(tex);
        }

        /// <summary>直接解 PNG，不依赖该资产的导入设置（部位图现在还是 Single/PPU100）</summary>
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

        // ==================== 导入与切片 ====================

        private static void ApplyImportSettings(string assetPath, string action, int cols)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[UnarmedBuilder] 取不到 TextureImporter：{assetPath}");
                return;
            }

            // 美术清单 §8 的导入规范
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = Ppu;
            importer.filterMode          = FilterMode.Point;
            importer.textureCompression  = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled       = false;
            importer.wrapMode            = TextureWrapMode.Clamp;

            // 图集还是按固定网格切（列数 × 4 行）；空帧保留在切片里，
            // 只是生成 clip 时不引用它们 —— 这样切片规则简单，也不会因为素材补帧而要重切。
            var metas = new List<SpriteMetaData>(cols * Rows);
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    metas.Add(new SpriteMetaData
                    {
                        name = SpriteName(action, row * cols + col),
                        // Unity 的 sprite rect 原点在【左下角】；行 0（South）是图集最上面一行
                        rect = new Rect(col * Cell, (Rows - 1 - row) * Cell, Cell, Cell),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero,
                    });
                }
            }
            // 说明：spritesheet 是旧 API（Unity 已标 Deprecated，但 2022.3 仍然可用）。
            // 官方建议改用 UnityEditor.U2D.Sprites.ISpriteEditorDataProvider ——
            // 那要额外引 2D Sprite 包、多写几十行，换来的只是一个警告，所以先留旧写法，
            // 将来升 Unity 大版本、这个 API 真被移除了再换。
#pragma warning disable 0618
            importer.spritesheet = metas.ToArray();
#pragma warning restore 0618
            importer.SaveAndReimport();
        }

        private static string SpriteName(string action, int index)
            => $"Swordsman_lvl1_{action}_no_weapon_{index}";

        // ==================== 生成 clip ====================

        private static int BuildClips(string sheetAsset, string action, int cols,
                                      int[] first, int[] last, StringBuilder log)
        {
            // 用名字建索引，不依赖 LoadAllAssetsAtPath 的返回顺序
            var map = new Dictionary<string, Sprite>(cols * Rows);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(sheetAsset))
                if (o is Sprite sp) map[sp.name] = sp;

            int made = 0;
            for (int row = 0; row < Rows; row++)
            {
                string dir = RowDirs[row];
                string clipName = $"Swordsman_{action}{dir}_NoWeapon";
                string clipPath = $"{ClipDir}/{clipName}.anim";

                if (first[row] < 0)
                {
                    log.AppendLine($"    {clipName}：该行整行是空的，跳过");
                    continue;
                }

                // 只取该行有效帧（Idle 的 North 就是 4 帧，不是 12）
                int count = last[row] - first[row] + 1;
                var keys = new ObjectReferenceKeyframe[count];
                bool ok = true;
                for (int i = 0; i < count; i++)
                {
                    int index = row * cols + first[row] + i;
                    if (!map.TryGetValue(SpriteName(action, index), out Sprite sp))
                    {
                        log.AppendLine($"    {clipName}：切片取不到（{SpriteName(action, index)}），跳过");
                        ok = false;
                        break;
                    }
                    keys[i] = new ObjectReferenceKeyframe
                    {
                        time = i / (float)SampleRate,      // 时间从 0 开始
                        value = sp,
                    };
                }
                if (!ok) continue;

                var clip = new AnimationClip { frameRate = SampleRate };

                // 绑到根物体的 m_Sprite —— 与现有 clip 的 path: 空 完全一致
                var binding = new EditorCurveBinding
                {
                    type = typeof(SpriteRenderer),
                    path = "",
                    propertyName = "m_Sprite",
                };
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

                // 循环 + 时长 = 帧数 / 采样率（与现有 32 个 clip 的写法一致）
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime  = true;
                settings.startTime = 0f;
                settings.stopTime  = count / (float)SampleRate;
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                AssetDatabase.DeleteAsset(clipPath);        // 允许反复重跑
                AssetDatabase.CreateAsset(clip, clipPath);
                log.AppendLine($"    {clipName}：{count} 帧 / {settings.stopTime:F3}s");
                made++;
            }
            return made;
        }
    }
}
#endif
