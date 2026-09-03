using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CryptWalker.Character;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 从已切片的 Swordsman lvl1 sprite sheet 中读取所有子 sprite,
    /// 然后选中一个挂了 TopDownCharacterAnimator 的 GameObject,
    /// 自动按 [dir*4 + frame] 顺序填到对应 ActionRow.sprites 数组里。
    ///
    /// 步骤:
    ///   1) 选中场景里的 Swordsman 物体
    ///   2) 菜单点 "Tools/CryptWalker/Build:Swordsman Action Rows (从切片自动填)"
    ///   3) 它会扫描 WithShadow/ 下所有 *with_shadow.png 加载它们切片后的子 sprite
    ///   4) 按文件名解析 Action, 写入 Animator
    /// </summary>
    public static class AutoFillSwordsmanActionRows
    {
        private const string SHEET_FOLDER = "Assets/Art/Character/Swordsman/lvl1/WithShadow";
        private const string MENU = "Tools/CryptWalker/Build:Swordsman Action Rows (从切片自动填)";

        [MenuItem(MENU)]
        public static void Run()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("失败", "请先在 Hierarchy 选中 Swordsman 物体", "OK");
                return;
            }

            var animator = go.GetComponent<TopDownCharacterAnimator>();
            if (animator == null)
            {
                EditorUtility.DisplayDialog("失败", "该物体没挂 TopDownCharacterAnimator", "OK");
                return;
            }

            // 找所有 sheet
            var sheetAbs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SHEET_FOLDER));
            var sheets = Directory.GetFiles(sheetAbs, "*_with_shadow.png", SearchOption.TopDirectoryOnly);
            if (sheets.Length == 0)
            {
                EditorUtility.DisplayDialog("失败", $"在 {SHEET_FOLDER} 下找不到 *_with_shadow.png\n请先执行切片菜单", "OK");
                return;
            }

            var rows = new List<TopDownCharacterAnimator.ActionRow>();
            int totalSprites = 0;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (var sheetPath in sheets)
            {
                var normalized = Path.GetFullPath(sheetPath).Replace("\\", "/");
                var assetPath = normalized.StartsWith(projectRoot.Replace("\\", "/"))
                    ? normalized.Substring(projectRoot.Replace("\\", "/").Length).TrimStart('/')
                    : normalized;
                var subs = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (subs == null || subs.Length == 0)
                {
                    Debug.LogWarning($"[AutoFill] {assetPath} LoadAllAssetsAtPath 返回空(检查路径或切片)");
                    continue;
                }
                var spriteList = new List<Sprite>();
                foreach (var s in subs)
                {
                    if (s is Sprite sp) spriteList.Add(sp);
                }
                if (spriteList.Count == 0)
                {
                    Debug.LogWarning($"[AutoFill] {assetPath} 没有切出 sprite(可能没切片)");
                    continue;
                }
                spriteList.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

                var action = ParseAction(Path.GetFileNameWithoutExtension(assetPath));
                var row = new TopDownCharacterAnimator.ActionRow
                {
                    action = action,
                    sprites = spriteList.ToArray()
                };
                rows.Add(row);
                totalSprites += spriteList.Count;
                Debug.Log($"[AutoFill] {action} <- {Path.GetFileName(assetPath)} ({spriteList.Count} sprite)");
            }

            // 反射写入 private field actionRows
            var so = new SerializedObject(animator);
            var prop = so.FindProperty("actionRows");
            prop.arraySize = rows.Count;
            for (int i = 0; i < rows.Count; i++)
            {
                var elem = prop.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("action").enumValueIndex = (int)rows[i].action;
                var sp = elem.FindPropertyRelative("sprites");
                sp.arraySize = rows[i].sprites.Length;
                for (int j = 0; j < rows[i].sprites.Length; j++)
                    sp.GetArrayElementAtIndex(j).objectReferenceValue = rows[i].sprites[j];
            }
            so.ApplyModifiedProperties();

            EditorUtility.DisplayDialog("完成",
                $"已自动填 {rows.Count} 个 ActionRow,共 {totalSprites} 个 sprite。\n" +
                "现在可以运行场景测试 8 方向移动 + 攻击。",
                "OK");
        }

        private static TopDownCharacterAnimator.Action ParseAction(string filename)
        {
            // 例子: Swordsman_lvl1_Walk_Attack_with_shadow
            var lower = filename.ToLower();
            if (lower.Contains("walk_attack")) return TopDownCharacterAnimator.Action.WalkAttack;
            if (lower.Contains("run_attack"))  return TopDownCharacterAnimator.Action.RunAttack;
            if (lower.Contains("walk"))        return TopDownCharacterAnimator.Action.Walk;
            if (lower.Contains("run"))         return TopDownCharacterAnimator.Action.Run;
            if (lower.Contains("attack"))      return TopDownCharacterAnimator.Action.Attack;
            if (lower.Contains("hurt"))        return TopDownCharacterAnimator.Action.Hurt;
            if (lower.Contains("death"))       return TopDownCharacterAnimator.Action.Death;
            return TopDownCharacterAnimator.Action.Idle;
        }
    }
}
