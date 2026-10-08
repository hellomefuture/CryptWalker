using System.Collections.Generic;
using UnityEngine;

namespace CryptWalker.HotUpdate.Pool
{
    /// <summary>
    /// 对象池的非泛型接口（让 PoolManager 统一管理不同 T 的池）。
    /// </summary>
    internal interface IPool
    {
        /// <summary>池名（调试用）</summary>
        string Name { get; }

        /// <summary>清空池（场景切换/退出时）</summary>
        void Clear();
    }

    /// <summary>对象被取出时调用</summary>
    public interface IPoolable
    {
        void OnSpawn();
        void OnRecycle();
    }

    /// <summary>
    /// 泛型对象池（策划案 §8 + AGENTS.md 3.8）。
    /// 限制：T 必须同时是 MonoBehaviour（GameObject 操作）和 IPoolable（生命周期回调）。
    /// </summary>
    public class ObjectPool<T> : IPool where T : MonoBehaviour, IPoolable
    {
        private readonly Queue<T> _free;
        private readonly System.Func<T> _createFunc;
        private readonly Transform _poolRoot;
        private readonly int _maxSize;

        public string Name { get; }

        public int FreeCount => _free.Count;
        public int TotalCount { get; private set; }

        public ObjectPool(string name, System.Func<T> createFunc, int initial = 10, int maxSize = 50, Transform poolRoot = null)
        {
            if (createFunc == null) throw new System.ArgumentNullException(nameof(createFunc));
            if (initial < 0) initial = 0;
            if (maxSize < initial) maxSize = initial;

            Name = name;
            _createFunc = createFunc;
            _maxSize = maxSize;
            _poolRoot = poolRoot;
            _free = new Queue<T>(initial);

            // 预热池
            for (int i = 0; i < initial; i++)
            {
                var obj = _createFunc();
                obj.OnRecycle();
                if (obj.gameObject.activeSelf) obj.gameObject.SetActive(false);
                if (_poolRoot != null) obj.transform.SetParent(_poolRoot);
                _free.Enqueue(obj);
                TotalCount++;
            }
        }

        public T Get(Transform parent = null)
        {
            T obj;
            if (_free.Count > 0)
            {
                obj = _free.Dequeue();
            }
            else
            {
                // 修复：防御 _createFunc 抛异常（开发期 prefab 没挂好时常见），不让池崩溃
                try
                {
                    obj = _createFunc();
                    TotalCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[ObjectPool<{typeof(T).Name}>] {Name}：createFunc 抛异常 — {e.Message}");
                    return null;
                }
            }

            if (obj == null)
            {
                Debug.LogError($"[ObjectPool<{typeof(T).Name}>] {Name}：createFunc 返回了 null");
                return null;
            }

            if (!obj.gameObject.activeSelf) obj.gameObject.SetActive(true);
            if (parent != null) obj.transform.SetParent(parent);
            obj.OnSpawn();
            return obj;
        }

        public void Return(T obj)
        {
            if (obj == null) return;

            obj.OnRecycle();
            if (obj.gameObject.activeSelf) obj.gameObject.SetActive(false);
            if (_poolRoot != null) obj.transform.SetParent(_poolRoot);

            if (_free.Count >= _maxSize)
            {
                Object.Destroy(obj.gameObject);
                TotalCount--;
                return;
            }

            _free.Enqueue(obj);
        }

        public void Clear()
        {
            while (_free.Count > 0)
            {
                var obj = _free.Dequeue();
                if (obj != null) Object.Destroy(obj.gameObject);
            }
            TotalCount = 0;
        }
    }
}