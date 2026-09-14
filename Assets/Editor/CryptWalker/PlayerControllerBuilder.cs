#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 玩家 AnimatorController 重建器（纯代码 Play 驱动版）。
    /// 背景：玩家 FSM 直接 Animator.Play("Swordsman_WalkSouth") 驱动，不用参数/transition。
    /// 所以 player.controller 重建为：扫描 {角色}Animation 目录下所有 clip → 每个建一个状态，
    /// 默认状态 = 该角色的 IdleSouth。无参数无连线。
    /// 用法：菜单 Tools/CryptWalker/生成玩家AnimatorController
    /// </summary>
    public static class PlayerControllerBuilder
    {
        private const string ClipDir    = "Assets/Art/PlayerAnimator/SwordsmanAnimation";
        private const string CtrlPath   = "Assets/Art/PlayerAnimator/SwordsmanAnimation/player.controller";
        private const string Menu       = "Tools/CryptWalker/生成玩家AnimatorController";

        [MenuItem(Menu)]
        public static void Run()
        {
            // 删除旧版（含参数/transition），重建纯状态版
            if (File.Exists(CtrlPath))
                AssetDatabase.DeleteAsset(CtrlPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
            var sm = controller.layers[0].stateMachine;

            // 扫描角色动画目录，clip 文件名 = 状态名（Animator.Play 按名字找）
            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipDir })
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => p.EndsWith(".anim"))
                .Select(p => new { Name = Path.GetFileNameWithoutExtension(p), Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(p) })
                .Where(x => x.Clip != null)
                .OrderBy(x => x.Name)
                .ToList();

            AnimatorState defaultState = null;
            foreach (var item in clips)
            {
                var state = sm.AddState(item.Name);   // 状态名 = clip 名（如 Swordsman_IdleSouth）
                state.motion = item.Clip;
                if (item.Name.EndsWith("IdleSouth")) defaultState = state;   // 默认待机
            }

            if (defaultState != null) sm.defaultState = defaultState;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string msg = $"player.controller 重建完成：{clips.Count} 状态（来自 {ClipDir}），默认 {defaultState?.name}\n" +
                         "如果 player.prefab 的 Animator Controller 变 None，重新拖入 player.controller 并 Apply。";
            Debug.Log($"[PlayerControllerBuilder] {msg}");
            EditorUtility.DisplayDialog("玩家 Animator", msg, "OK");
        }
    }
}
#endif
