using System;
using System.Collections.Generic;
using UnityEngine;

namespace CryptWalker.HotUpdate.Core
{
    /// <summary>
    /// 泛型对象池（对应框架设计 3.4 / 策划案 8 章）。
    /// 解决：Instantiate / Destroy 高频调用导致 GC 和卡顿（弹道/敌人/伤害数字/特效）。
    /// 方案：借还复用 —— Get 优先从池拿，Return 回池，超上限直接销毁。
    ///
    /// 为什么用 createFunc 委托而不是内部加载 Prefab：
    ///   1. 解耦 —— 池不依赖资源系统，"怎么造对象"由调用方决定
    ///   2. 热更纪律 —— 热更侧不直接依赖 AOT 资源 API，资源加载留在调用方
    ///
    /// 用法：
    ///   var pool = new ObjectPool<Bullet>(
    ///       create: () => Instantiate(bulletPrefab).GetComponent<Bullet>(),
    ///       root:   poolRoot,
    ///       maxSize: 60);
    ///   var b = pool.Get();      // 取出（激活 + OnSpawn）
    ///   pool.Return(b);          // 归还（清理 + 隐藏 + 入池）
    /// </summary>
    public class ObjectPool<T> where T : MonoBehaviour, IPoolable
    {
        private readonly Func<T> _create;      // 创建新实例的工厂（调用方注入）
        private readonly Transform _root;      // 池容器：隐藏的回收对象挂这里
        private readonly int _maxSize;         // 池上限：超过则销毁而非入池（防池膨胀）
        private readonly Queue<T> _pool = new Queue<T>();

        public int Count => _pool.Count;

        public ObjectPool(Func<T> create, Transform root, int maxSize = 30)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            _root = root;
            _maxSize = maxSize;
        }

        /// <summary>取出：优先复用池内对象，没有才创建新实例。</summary>
        public T Get()
        {
            T item = _pool.Count > 0 ? _pool.Dequeue() : _create();

            item.gameObject.SetActive(true);   // 先激活再复位（OnSpawn 里可能访问组件）
            item.OnSpawn();
            return item;
        }

        /// <summary>归还：先清理状态，再决定入池 or 销毁。</summary>
        public void Return(T item)
        {
            if (item == null) return;

            item.OnRecycle();

            if (_pool.Count < _maxSize)
            {
                item.transform.SetParent(_root, false);   // 收进池容器
                item.gameObject.SetActive(false);          // 隐藏 = "不在场"
                _pool.Enqueue(item);
            }
            else
            {
                // 关键：超过上限直接销毁，而不是无限塞池 —— 策划案 8 章的明确要求
                UnityEngine.Object.Destroy(item.gameObject);
            }
        }

        /// <summary>清空池（切换场景/销毁池时调用），避免隐藏对象残留。</summary>
        public void Clear()
        {
            while (_pool.Count > 0)
                UnityEngine.Object.Destroy(_pool.Dequeue().gameObject);
        }
    }
}
