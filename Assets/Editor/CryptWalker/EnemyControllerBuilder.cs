#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 敌人 AnimatorController 一键生成器。
    /// 背景：敌人动画由代码 EnemyFSM 直接 Animator.Play("Orc1_AttackSouth") 驱动，
    /// 所以 controller 不需要 transition/参数，只需要包含所有会被 Play 的同名状态 + 默认 IdleSouth。
    ///
    /// 规则：每个敌人 24 状态 = 6 动作（Idle/Walk/Run/Attack/Hurt/Death）× 4 方向（South/Left/Right/North），
    /// 状态挂对应 clip，默认状态 IdleSouth。已有空壳 controller 会被删除重建。
    /// 用法：菜单 Tools/CryptWalker/生成敌人AnimatorController
    /// </summary>
    public static class EnemyControllerBuilder
    {
        private const string AnimRoot = "Assets/Art/EnemyAnimator";
        private const string Menu     = "Tools/CryptWalker/生成敌人AnimatorController";

        private static readonly string[] Enemies = { "Orc1", "Orc2", "Orc3", "Vampires1", "Vampires2", "Vampires3" };
        private static readonly string[] Actions = { "Idle", "Walk", "Run", "Attack", "Hurt", "Death" };
        private static readonly string[] Dirs    = { "South", "Left", "Right", "North" };

        [MenuItem(Menu)]
        public static void Run()
        {
            int built = 0;
            foreach (var enemy in Enemies)
                if (BuildOne(enemy)) built++;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("敌人 Animator", $"已生成 {built} 个 controller → {AnimRoot}", "OK");
        }

        private static bool BuildOne(string enemy)
        {
            string dir = $"{AnimRoot}/{enemy}Animation";
            string ctrlPath = $"{dir}/{enemy}.controller";
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning($"[EnemyControllerBuilder] 缺目录: {dir}");
                return false;
            }

            // 删除旧空壳/旧配置，重建（代码 Play 驱动，无手动配置可保留）
            if (File.Exists(ctrlPath))
                AssetDatabase.DeleteAsset(ctrlPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = controller.layers[0].stateMachine;

            AnimatorState defaultState = null;
            int states = 0;
            foreach (var action in Actions)
            {
                foreach (var direction in Dirs)
                {
                    string clipName = $"{enemy}_{action}{direction}";
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{dir}/{clipName}.anim");
                    if (clip == null) continue;   // 该动作缺某方向则跳过

                    var state = sm.AddState(clipName);
                    state.motion = clip;
                    if (action == "Idle" && direction == "South") defaultState = state;
                    states++;
                }
                {
                    string clipName = $"{enemy}_{action}{dir}";
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{dir}/{clipName}.anim");
                    if (clip == null) continue;   // 该动作缺某方向则跳过

                    var state = sm.AddState(clipName);
                    state.motion = clip;
                    if (action == "Idle" && dir == "South") defaultState = state;
                    states++;
                }
            }

            if (defaultState == null)
                Debug.LogWarning($"[EnemyControllerBuilder] {enemy} 缺 IdleSouth，未设默认状态");
            else
                sm.defaultState = defaultState;

            EditorUtility.SetDirty(controller);
            Debug.Log($"[EnemyControllerBuilder] {enemy}.controller: {states} 状态，默认 {defaultState?.name}");
            return true;
        }
    }
}
#endif
