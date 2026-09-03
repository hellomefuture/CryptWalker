using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CryptWalker.Character;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 一键搭建 8 方向俯视角色测试场景。
    ///  - 建一个空场景
    ///  - 加 Main Camera (orthographic) + TopDownFollowCamera
    ///  - 加地面 (灰色方块)
    ///  - 加主角 Prefab (有 TopDownCharacterController + TopDownCharacterAnimator + SpriteRenderer + CharacterController)
    ///
    /// 前提: 你已经执行了 "Tools/CryptWalker/Slice:Swordsman lvl1 8 方向 SpriteSheet",
    ///       并且手工把 8 个动作 × 32 sprite 填到 ActionRow 数组(可用配套按钮 Build Swordsman Action Rows 自动填)。
    /// </summary>
    public static class BuildSwordsmanTestScene
    {
        private const string SCENE_PATH = "Assets/Scenes/SwordsmanTopDownTest.unity";
        private const string MENU_BUILD = "Tools/CryptWalker/Build:Swordsman 8-Dir Test Scene";

        [MenuItem(MENU_BUILD)]
        public static void Run()
        {
            // 新场景
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 主相机
            var cam = Camera.main;
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0, 0, -10f);
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f, 1f);

            // 地面
            var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0, 0, 0.5f);
            ground.transform.localScale = new Vector3(20, 20, 1);
            var groundMat = new Material(Shader.Find("Sprites/Default"));
            groundMat.color = new Color(0.2f, 0.18f, 0.15f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

            // 主角
            var player = new GameObject("Swordsman");
            player.transform.position = Vector3.zero;
            var sr = player.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 10;
            var cc = player.AddComponent<CharacterController>();
            cc.radius = 0.3f;
            cc.height = 0.2f; // 俯视 2D 角色压扁
            cc.center = new Vector3(0, 0, 0);
            player.AddComponent<TopDownCharacterController>();
            player.AddComponent<TopDownCharacterAnimator>();

            // 摄像机跟随
            var follow = cam.gameObject.AddComponent<TopDownFollowCamera>();
            // 用反射或 SerializedObject 设 private field target
            var so = new SerializedObject(follow);
            so.FindProperty("target").objectReferenceValue = player.transform;
            so.ApplyModifiedProperties();

            // 引导色块(给玩家参照坐标)
            for (int i = 0; i < 4; i++)
            {
                var angle = i * 90f * Mathf.Deg2Rad;
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = $"Marker_{i}";
                marker.transform.position = new Vector3(Mathf.Cos(angle) * 4, Mathf.Sin(angle) * 4, 0.3f);
                marker.transform.localScale = new Vector3(0.4f, 0.4f, 0.1f);
                var m = new Material(Shader.Find("Sprites/Default"));
                m.color = Color.HSVToRGB(i * 0.25f, 0.6f, 1f);
                marker.GetComponent<MeshRenderer>().sharedMaterial = m;
            }

            // 保存场景
            Directory.CreateDirectory(Path.GetDirectoryName(SCENE_PATH));
            EditorSceneManager.SaveScene(scene, SCENE_PATH);

            // 选中主角,方便用户继续配 sprite
            Selection.activeGameObject = player;
            EditorUtility.DisplayDialog("完成",
                $"测试场景已建好:{SCENE_PATH}\n\n" +
                "下一步:\n" +
                "  1) 在 Project 里选中切片后的 sprite sheet\n" +
                "  2) 在 Swordsman 物体的 Inspector 里,\n" +
                "     TopDownCharacterAnimator.actionRows 数组,\n" +
                "     把 8 个 ActionRow 配好(每个 32 sprite)\n" +
                "  3) 运行场景, WASD 移动, Shift 跑, J/左键攻击\n",
                "OK");
        }
    }
}
