using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CryptWalker.HotUpdate.UI
{
    /// <summary>
    /// 等高项虚拟列表（AGENTS.md §3.4「大列表必须用 ListView/ScrollView 虚拟化」）。
    ///
    /// ===== 解决什么问题 =====
    /// UGUI 的 ScrollRect + VerticalLayoutGroup 在 50 项以上就开始卡：
    /// 每加一项都触发整个 Content 重排 → SetVerticesDirty → Canvas Rebuild。
    /// 背包 1000 件装备时它是完全不可用的。
    ///
    /// 本组件的做法（与商业项目一致）：
    ///   1) 只为【可见区 + 缓冲】创建约 10~20 个 Item，其余数据不生成物体
    ///   2) Item 用对象池复用：滚动只改数据 + 改位置，不 Instantiate / Destroy
    ///   3) 数据驱动：滚到哪就调 onBind(item, index)，由业务自己填内容
    ///   4) 窗口移动时只处理"进出窗口的那几项"，不整屏重新绑定
    ///      —— 这点很关键：重新绑定会重复触发图标异步加载
    ///
    /// ===== 怎么用 =====
    ///   1) 面板里放一个 ScrollRect，Content 的锚点设 top-stretch、pivot=(0.5, 1)
    ///      （本组件按"顶部对齐、向下排"计算位置）
    ///   2) 做一个 Item 预制体（高度固定，比如 100）
    ///   3) 脚本里：
    ///        _list.Init(scrollRect, itemPrefab, 100f, OnBindItem);
    ///        _list.SetCount(bag.Count);          // 数据条数变了就调
    ///        _list.RefreshAll();                 // 内容变了但条数没变时调
    ///
    /// ===== 边界（不要拿它当万能列表）=====
    ///   · 只支持【垂直 + 等高】—— 背包 / 商店 / 遗物栏 / 排行榜都是这种，够用
    ///   · 高度不一致的列表（聊天记录、剧情对话）不适用，要另做变高方案
    ///
    /// ⚠️ 目前【没有调用方】—— 背包/商店还没开始做。
    ///    先写出来的原因：让"大列表必须虚拟化"这条规则有个现成的落点，
    ///    而不是等做到那块时临时手搓一个（临时手搓的结果通常就是直接上 LayoutGroup）。
    /// </summary>
    public class ListView : MonoBehaviour
    {
        // —— 外部注入 ——
        private ScrollRect _scroll;
        private RectTransform _content;
        private GameObject _itemPrefab;
        private float _itemHeight = 100f;
        private int _buffer = 2;                          // 可见区上下各多准备几项，滚动时不留白
        private Action<GameObject, int> _onBind;          // (item, dataIndex) → 业务填内容

        // —— 运行时状态 ——
        private int _count;                               // 数据总条数
        private readonly List<GameObject> _window = new List<GameObject>();  // 当前窗口内的 Item
        private int _windowFirst = -1;                    // _window[0] 对应第几条数据
        private readonly List<GameObject> _pool = new List<GameObject>();    // 回收池

        /// <summary>初始化。onBind 只在 Item【进入窗口】时调用一次，滚动中不会重复绑定。</summary>
        public void Init(ScrollRect scroll, GameObject itemPrefab, float itemHeight,
                         Action<GameObject, int> onBind, int buffer = 2)
        {
            _scroll = scroll;
            _itemPrefab = itemPrefab;
            _itemHeight = Mathf.Max(1f, itemHeight);
            _onBind = onBind;
            _buffer = Mathf.Max(0, buffer);

            _content = scroll != null ? scroll.content : null;
            if (_content == null)
            {
                Core.Log.Error("[ListView] ScrollRect.content 为空，虚拟列表无法工作");
                return;
            }

            // 自己接管竖直滚动（不用 LayoutGroup：它会把所有 Item 都算一遍，正是要避免的）
            if (_scroll != null) _scroll.onValueChanged.AddListener(OnScrollChanged);
        }

        private void OnDestroy()
        {
            if (_scroll != null) _scroll.onValueChanged.RemoveListener(OnScrollChanged);
        }

        /// <summary>设置数据条数（数据增删后调用）。内容变了但条数不变 → 用 RefreshAll。</summary>
        public void SetCount(int count)
        {
            _count = Mathf.Max(0, count);

            // Content 高度 = 总条数 × 行高，这样滚动条长度才对
            if (_content != null)
            {
                Vector2 size = _content.sizeDelta;
                size.y = _count * _itemHeight;
                _content.sizeDelta = size;
            }

            UpdateWindow(force: true);
        }

        /// <summary>重新绑定当前窗口内的所有 Item（条数不变、内容变了时用）</summary>
        public void RefreshAll()
        {
            for (int i = 0; i < _window.Count; i++)
            {
                int dataIndex = _windowFirst + i;
                if (dataIndex >= 0 && dataIndex < _count)
                    _onBind?.Invoke(_window[i], dataIndex);
            }
        }

        /// <summary>滚到指定数据项（会把它滚到可见区内）</summary>
        public void ScrollToIndex(int index)
        {
            if (_scroll == null || _content == null) return;
            index = Mathf.Clamp(index, 0, Mathf.Max(0, _count - 1));
            float max = Mathf.Max(0f, _content.sizeDelta.y - GetViewportHeight());
            float y = Mathf.Clamp(index * _itemHeight, 0f, max);
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, y);
            UpdateWindow(force: true);
        }

        private void OnScrollChanged(Vector2 _) => UpdateWindow(force: false);

        /// <summary>
        /// 重算可见窗口：
        ///   只回收"滚出窗口的"、只创建"滚进窗口的"，中间的 Item 一个都不碰
        ///   —— 所以滚动过程中不会重复触发 onBind（图标不会反复重新加载）。
        /// </summary>
        private void UpdateWindow(bool force)
        {
            if (_content == null || _itemPrefab == null || _onBind == null) return;

            if (_count <= 0)
            {
                ClearWindow();
                _windowFirst = -1;
                return;
            }

            float viewportHeight = GetViewportHeight();
            if (viewportHeight <= 0f) return;

            int visible = Mathf.CeilToInt(viewportHeight / _itemHeight) + 1;
            int need = Mathf.Min(_count, visible + _buffer * 2);

            // Content 向上滚了多少 → 第一项该从第几条数据开始
            int first = Mathf.FloorToInt(_content.anchoredPosition.y / _itemHeight) - _buffer;
            first = Mathf.Clamp(first, 0, Mathf.Max(0, _count - need));

            if (!force && first == _windowFirst && _window.Count == need) return;
            if (_windowFirst < 0) { _windowFirst = first; }

            // 1) 回收尾部滚出去的
            while (_window.Count > 0 && _windowFirst + _window.Count - 1 >= first + need)
            {
                Recycle(_window[_window.Count - 1]);
                _window.RemoveAt(_window.Count - 1);
            }
            // 2) 回收头部滚出去的（注意：回收头部要同步前移 _windowFirst）
            while (_window.Count > 0 && _windowFirst < first)
            {
                Recycle(_window[0]);
                _window.RemoveAt(0);
                _windowFirst++;
            }
            // 3) 头部补齐
            while (_windowFirst > first)
            {
                _windowFirst--;
                _window.Insert(0, Acquire(_windowFirst));
            }
            // 4) 尾部补齐
            while (_windowFirst + _window.Count < first + need)
            {
                _window.Add(Acquire(_windowFirst + _window.Count));
            }
            // 5) 窗口过大时从尾部裁（数据变少的情况）
            while (_window.Count > need)
            {
                Recycle(_window[_window.Count - 1]);
                _window.RemoveAt(_window.Count - 1);
            }
            // 6) 摆位置（Item 的锚点应设为 top-stretch，所以只改 y）
            for (int i = 0; i < _window.Count; i++)
                SetItemPosition(_window[i], _windowFirst + i);
        }

        private float GetViewportHeight()
        {
            if (_scroll != null && _scroll.viewport != null) return _scroll.viewport.rect.height;
            return _content != null ? _content.rect.height : 0f;
        }

        /// <summary>取一个可用 Item：优先复用池里的，没有再 Instantiate</summary>
        private GameObject Acquire(int dataIndex)
        {
            GameObject item;
            if (_pool.Count > 0)
            {
                item = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
            }
            else
            {
                item = Instantiate(_itemPrefab, _content);
            }

            item.SetActive(true);
            _onBind?.Invoke(item, dataIndex);
            return item;
        }

        private void Recycle(GameObject item)
        {
            if (item == null) return;
            item.SetActive(false);   // 隐藏而不是销毁 —— 下次滚动直接复用，不产生 Instantiate/Destroy
            _pool.Add(item);
        }

        private void SetItemPosition(GameObject item, int dataIndex)
        {
            if (item.transform is RectTransform rt)
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -dataIndex * _itemHeight);
        }

        private void ClearWindow()
        {
            for (int i = 0; i < _window.Count; i++) Recycle(_window[i]);
            _window.Clear();
        }
    }
}
