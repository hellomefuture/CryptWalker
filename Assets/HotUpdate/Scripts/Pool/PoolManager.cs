using System.Collections.Generic;
using CryptWalker.HotUpdate.Core;
using UnityEngine;

namespace CryptWalker.HotUpdate.Pool
{
    /// <summary>
    /// 对象池管理中心（策划案 §8 + AGENTS.md 3.8）。
    /// 业务侧按名字取池：
    ///   var bulletPool = PoolManager.Instance.Get&lt;Bullet&gt;("Bullet");
    ///   var b = bulletPool.Get();
    ///   bulletPool.Return(b);
    ///
    /// 使用规范（策划案 §8 表）：
    ///   普通敌人   initial=15 maxSize=30
    ///   精英/Boss  initial=2  maxSize=5
    ///   玩家弹道   initial=30 maxSize=60
    ///   敌人投射物 initial=20 maxSize=40
    ///   伤害数字   initial=20 maxSize=50
    ///   拾取物     initial=40 maxSize=80
    ///   特效       initial=15 maxSize=30
    ///
    /// 池注册时机：在模块初始化阶段（Modules.Pool = Register(new PoolManager()) 后调 Init），
    /// 业务系统在 DataService 之后、UI/Audio 之前完成池注册（AGENTS.md 3.3 强制）。
    /// </summary>
    public class PoolManager : IModule
    {
        private readonly Dictionary<string, IPool> _pools = new Dictionary<string, IPool>();
        private Transform _root;

        public static PoolManager Instance { get; private set; }

        public void Init()
        {
            Instance = this;
            var rootGo = new GameObject("[PoolRoot]");
            rootGo.transform.position = Vector3.zero;
            Object.DontDestroyOnLoad(rootGo);
            _root = rootGo.transform;
            Core.Log.Info("[PoolManager] 初始化完成");
        }

        public void Shutdown()
        {
            foreach (var pool in _pools.Values) pool?.Clear();
            _pools.Clear();
            // 修复：先置空 _root 再 Destroy，避免在 Destroy 后续流程（如 IsDestroyed 检查）用到 _root 引用
            var rootGo = _root != null ? _root.gameObject : null;
            _root = null;
            if (rootGo != null) Object.Destroy(rootGo);
            Instance = null;
        }

        /// <summary>
        /// 注册一个池。重复注册同名池会覆盖。
        /// </summary>
        public ObjectPool<T> RegisterPool<T>(string name, System.Func<T> createFunc, int initial, int maxSize)
            where T : MonoBehaviour, IPoolable
        {
            var pool = new ObjectPool<T>(name, createFunc, initial, maxSize, _root);
            _pools[name] = pool;
            return pool;
        }

        /// <summary>取已注册的池。没注册返回 null。</summary>
        public ObjectPool<T> Get<T>(string name) where T : MonoBehaviour, IPoolable
        {
            if (_pools.TryGetValue(name, out var obj))
                return obj as ObjectPool<T>;
            return null;
        }

        public bool HasPool(string name) => _pools.ContainsKey(name);
    }
}