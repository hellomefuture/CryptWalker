#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CryptWalker.Main.Editor
{
    /// <summary>
    /// 一键创建启动场景（含 MainLauncher 节点）。
    /// 菜单：Tools/CryptWalker/创建启动场景
    /// </summary>
    public static class CreateBootScene
    {
        private const string ScenePath = "Assets/Scenes/Boot.unity";

        [MenuItem("Tools/CryptWalker/创建启动场景")]
        public static void Create()
        {
            // 1) 新场景
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2) 创建主相机
            var camGo = new GameObject("Main Camera");
            camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0, 0, -10);

            // 3) 创建 EventSystem（如果用了 UI）
            // var es = new GameObject("EventSystem");
            // es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            // es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // 4) 挂 MainLauncher
            var launcherGo = new GameObject("MainLauncher");
            launcherGo.AddComponent<CryptWalker.Main.Boot.MainLauncher>();

            // 5) 保存到 Assets/Scenes/Boot.unity
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 6) 加进 Build Settings（Scene 0）
            var newScene = EditorBuildSettings.scenes;
            // 避免重复
            bool exists = false;
            for (int i = 0; i < newScene.Length; i++)
                if (newScene[i].path == ScenePath) { exists = true; break; }
            if (!exists)
            {
                System.Array.Resize(ref newScene, newScene.Length + 1);
                newScene[newScene.Length - 1] = new EditorBuildSettingsScene(ScenePath, true);
                // 把 Boot 放最前
                for (int i = newScene.Length - 1; i > 0; i--)
                    newScene[i] = newScene[i - 1];
                newScene[0] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = newScene;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[CryptWalker] 启动场景已创建并设为 Scene 0: {ScenePath}");
            Debug.Log("现在按 Play，应该看到三行日志：[Main] CryptWalker 启动中 / [Main] YooAsset mode=... / [HotUpdate] GameRoot 启动");
        }
    }
}
#endif