using UnityEngine;

namespace CryptWalker.Main
{
    /// <summary>
    /// 桥接实现：主工程为热更程序集注入的真实能力。
    /// </summary>
    public sealed class AppBridge : IAppBridge
    {
        public string PersistentDataPath { get; }

        public AppBridge(string persistentDataPath)
        {
            PersistentDataPath = persistentDataPath;
        }

        public void Log(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Info:
                    Debug.Log($"[HotUpdate] {message}");
                    break;
                case LogLevel.Warn:
                    Debug.LogWarning($"[HotUpdate] {message}");
                    break;
                case LogLevel.Error:
                    Debug.LogError($"[HotUpdate] {message}");
                    break;
            }
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}