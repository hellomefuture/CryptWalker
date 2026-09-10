#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 玩家 AnimatorController 重建器（纯代码 Play 驱动版）。
    /// 背景：玩家 FSM 改为 Animator.Play("WalkSouth") 驱动，不再用参数/transition。
    /// 所以 player.controller 重建为：12 状态（Idle/Walk/Attack × 4 方向）+ 挂 clip + 默认 IdleSouth，
    /// 不含任何参数和连线。
    /// 用法：菜单 Tools/CryptWalker/生成玩家AnimatorController
    /// </summary>
    public static class PlayerControllerBuilder
    {
        private const string AnimDir    = "Assets/Art/PlayerAnimator";
        private const string CtrlPath   = "Assets/Art/PlayerAnimator/player.controller";
        private const string Menu       = "Tools/CryptWalker/生成玩家AnimatorController";

        private static readonly string[] Actions = { "Idle", "Walk", "Attack" };
        private static readonly string[] Dirs    = { "South", "Left", "Right", "North" };

        [MenuItem(Menu)]
        public static void Run()
        {
            // 删除旧版（含参数/transition），重建纯状态版
            if (File.Exists(CtrlPath))
                AssetDatabase.DeleteAsset(CtrlPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
            var sm = controller.layers[0].stateMachine;

            AnimatorState defaultState = null;
            int states = 0;
            foreach (var action in Actions)
            {
                foreach (var dir in Dirs)
                {
                    string stateName = $"{action}{dir}";              // WalkSouth
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimDir}/{stateName}.anim");
                    if (clip == null) continue;

                    var state = sm.AddState(stateName);
                    state.motion = clip;
                    if (action == "Idle" && dir == "South") defaultState = state;
                    states++;
                }
            }

            if (defaultState != null) sm.defaultState = defaultState;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string msg = $"player.controller 重建完成：{states} 状态，无参数/transition，默认 {defaultState?.name}\n" +
                         "如果 player.prefab 的 Animator Controller 变 None，重新拖入 player.controller 并 Apply。";
            Debug.Log($"[PlayerControllerBuilder] {msg}");
            EditorUtility.DisplayDialog("玩家 Animator", msg, "OK");
        }
    }
}
#endif
