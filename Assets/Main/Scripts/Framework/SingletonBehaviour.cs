using UnityEngine;

namespace CryptWalker.Main.Framework
{
    /// <summary>泛型 MonoBehaviour 单例基类（AOT 主工程侧使用）</summary>
    public abstract class SingletonBehaviour<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T s_instance;

        /// <summary>实例；首次访问自动在场景中创建</summary>
        public static T Instance
        {
            get
            {
                if (s_instance == null)
                {
                    if (Application.isPlaying)
                        s_instance = Object.FindObjectOfType<T>();
                    if (s_instance == null)
                        s_instance = new GameObject($"[{typeof(T).Name}]").AddComponent<T>();
                }
                return s_instance;
            }
        }

        protected virtual void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this as T;
        }
    }
}