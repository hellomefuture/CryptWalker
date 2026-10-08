using System.Collections.Generic;
using UnityEngine;

namespace CryptWalker.HotUpdate.Combat
{
    /// <summary>
    /// 敌人元素印记（策划案 §3.2）。
    /// 挂在 EnemyBase 上，每元素独立计时，最多同时 2 枚。
    ///
    /// 规则（策划案 §0 已修正）：
    ///   1) 每次攻击附元素 → 给敌人挂印记，持续 5 秒
    ///   2) 每元素独立计时
    ///   3) 同一敌人最多 2 枚
    ///   4) 第 3 种元素仅当能与剩余 1 枚组成有效反应时才替换旧印记，否则拒绝
    ///   5) 内置冷却：同一敌人每 0.5s 只能触发一次元素反应
    ///   6) 反应触发后清除参与反应的两枚印记
    ///
    /// 使用：被武器攻击命中后调 AddMark(elementType, duration)；Update 自动过期 + 检测反应。
    /// </summary>
    public class EnemyElementMarks : MonoBehaviour
    {
        /// <summary>单个印记槽（策划案允许同元素叠加，但策划案 §3.2 只用"2 枚不同元素"——这里用单实例即可）</summary>
        private class Mark
        {
            public Data.EElementType Element;
            public float ExpireTime;
        }

        private const int MaxMarks = 2;
        private const float DefaultDuration = 5f;
        private const float ReactionCooldown = 0.5f;

        private readonly List<Mark> _marks = new List<Mark>(MaxMarks);
        private int _poisonStacks;
        private float _lastReactionTime = -999f;

        public bool HasMark(Data.EElementType e)
        {
            for (int i = 0; i < _marks.Count; i++)
                if (_marks[i].Element == e) return true;
            return false;
        }

        public int PoisonStacks => _poisonStacks;

        /// <summary>
        /// 尝试添加一枚元素印记。
        /// 返回 true = 添加成功；false = 拒绝（第 3 种元素 + 无法与剩余组成有效反应）。
        /// </summary>
        public bool AddMark(Data.EElementType element, float duration = DefaultDuration)
        {
            if (element == Data.EElementType.None) return false;

            // 已存在 → 刷新计时
            for (int i = 0; i < _marks.Count; i++)
            {
                if (_marks[i].Element == element)
                {
                    _marks[i].ExpireTime = Time.time + duration;
                    if (element == Data.EElementType.Poison) _poisonStacks++;
                    return true;
                }
            }

            // 未达上限 → 直接加
            if (_marks.Count < MaxMarks)
            {
                _marks.Add(new Mark { Element = element, ExpireTime = Time.time + duration });
                if (element == Data.EElementType.Poison) _poisonStacks++;
                return true;
            }

            // 已达上限 2 枚 + 第 3 种元素 → 只能替换与"剩余 1 枚"能组成有效反应的旧印记
            for (int i = 0; i < _marks.Count; i++)
            {
                if (CanReact(_marks[i].Element, element))
                {
                    if (_marks[i].Element == Data.EElementType.Poison) _poisonStacks = Mathf.Max(0, _poisonStacks - 1);
                    _marks.RemoveAt(i);
                    _marks.Add(new Mark { Element = element, ExpireTime = Time.time + duration });
                    if (element == Data.EElementType.Poison) _poisonStacks++;
                    return true;
                }
            }

            return false;
        }

        /// <summary>清除指定的元素印记（用 for 倒序避免修改 List 导致遍历崩溃）</summary>
        public void RemoveMark(Data.EElementType element)
        {
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                if (_marks[i].Element == element)
                {
                    if (element == Data.EElementType.Poison) _poisonStacks = Mathf.Max(0, _poisonStacks - 1);
                    _marks.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>清除所有毒层</summary>
        public void RemoveAllPoisonStacks() => _poisonStacks = 0;

        /// <summary>
        /// 检测 + 触发一次反应。返回 true 表示触发了反应。
        /// 关键修复：先保存 element 到本地变量，再 RemoveMark（避免 List 修改时索引失效崩溃）。
        /// </summary>
        public bool TryReact(System.Action<Data.EElementType, Data.EElementType, float> trigger)
        {
            if (Time.time - _lastReactionTime < ReactionCooldown) return false;

            // 找两个能反应的印记（用 for + 索引，但 RemoveMark 前先存 element 值）
            for (int i = 0; i < _marks.Count; i++)
            {
                for (int j = i + 1; j < _marks.Count; j++)
                {
                    var elemI = _marks[i].Element;
                    var elemJ = _marks[j].Element;
                    if (CanReact(elemI, elemJ))
                    {
                        trigger?.Invoke(elemI, elemJ, Time.time - _lastReactionTime);

                        // 修复：先存 element 再 Remove（RemoveMark 会改 List，索引失效）
                        RemoveMark(elemI);
                        RemoveMark(elemJ);
                        _lastReactionTime = Time.time;
                        return true;
                    }
                }
            }
            return false;
        }

        private void Update()
        {
            // 修复：用 for 倒序，避免迭代时修改 List
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                if (_marks[i].ExpireTime < Time.time)
                {
                    if (_marks[i].Element == Data.EElementType.Poison) _poisonStacks = Mathf.Max(0, _poisonStacks - 1);
                    _marks.RemoveAt(i);
                }
            }
        }

        /// <summary>策划案 §3.2 反应表（4 种）</summary>
        public static bool CanReact(Data.EElementType a, Data.EElementType b)
        {
            if (a == Data.EElementType.None || b == Data.EElementType.None) return false;
            if (a == b) return false;
            // 4 种反应 + 双向
            return (a == Data.EElementType.Fire && b == Data.EElementType.Ice)
                || (a == Data.EElementType.Ice && b == Data.EElementType.Fire)
                || (a == Data.EElementType.Fire && b == Data.EElementType.Thunder)
                || (a == Data.EElementType.Thunder && b == Data.EElementType.Fire)
                || (a == Data.EElementType.Ice && b == Data.EElementType.Thunder)
                || (a == Data.EElementType.Thunder && b == Data.EElementType.Ice)
                || (a == Data.EElementType.Fire && b == Data.EElementType.Poison)
                || (a == Data.EElementType.Poison && b == Data.EElementType.Fire);
        }

        /// <summary>策划案 §3.2 反应 → 反应类型映射</summary>
        public static ElementReaction GetReactionType(Data.EElementType a, Data.EElementType b)
        {
            if (!CanReact(a, b)) return ElementReaction.None;
            if ((a == Data.EElementType.Fire && b == Data.EElementType.Ice) || (a == Data.EElementType.Ice && b == Data.EElementType.Fire))
                return ElementReaction.Vapor;
            if ((a == Data.EElementType.Fire && b == Data.EElementType.Thunder) || (a == Data.EElementType.Thunder && b == Data.EElementType.Fire))
                return ElementReaction.Overload;
            if ((a == Data.EElementType.Ice && b == Data.EElementType.Thunder) || (a == Data.EElementType.Thunder && b == Data.EElementType.Ice))
                return ElementReaction.ElectroChained;
            if ((a == Data.EElementType.Fire && b == Data.EElementType.Poison) || (a == Data.EElementType.Poison && b == Data.EElementType.Fire))
                return ElementReaction.PoisonDetonation;
            return ElementReaction.None;
        }
    }

    /// <summary>元素反应类型（策划案 §3.2）</summary>
    public enum ElementReaction
    {
        None,
        Vapor,
        Overload,
        ElectroChained,
        PoisonDetonation,
    }
}