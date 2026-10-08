using System.Collections.Generic;
using CryptWalker.HotUpdate.Data;
using UnityEngine;

namespace CryptWalker.HotUpdate.BuffSystem
{
    /// <summary>
    /// Buff 运行时实例（策划案 §4.1）。
    /// 由 BuffManager 创建：玩家身上某 BuffData 的当前堆叠。
    ///
    /// 同 BuffData 可叠加：策划案约束：
    ///   - 第 1 次：×1.0
    ///   - 第 2 次：×0.7
    ///   - 第 3 次：×0.5（达到 maxStack 上限）
    ///
    /// 计算实际值用 GetEffectiveValue()：原始值 × StackFactor(stackCount)
    /// </summary>
    public class BuffInstance
    {
        public BuffData Data;
        public int StackCount;

        public BuffInstance(BuffData data, int initialStacks = 1)
        {
            Data = data;
            // 修复：data.maxStack 可能为 0 或负数，加防御
            int maxStack = data.maxStack > 0 ? data.maxStack : 1;
            StackCount = Mathf.Clamp(initialStacks, 1, maxStack);
        }

        /// <summary>
        /// 策划案 §4.1 叠加因子：
        ///   stack=1 → 1.0
        ///   stack=2 → 0.7
        ///   stack≥3 → 0.5
        ///
        /// 修复：负数/0 情况返回 0（避免给玩家负 buff 增益）
        /// </summary>
        public static float StackFactor(int stack)
        {
            if (stack <= 0) return 0f;
            switch (stack)
            {
                case 1: return 1.0f;
                case 2: return 0.7f;
                default: return 0.5f;
            }
        }

        public float GetEffectiveValue() => Data.value * StackFactor(StackCount);
    }

    /// <summary>
    /// Buff 三选一抽卡（策划案 §4.1）。
    /// 每级升满后 BuffManager.RollForLevelUp(player) 返回 3 个不同 BuffData 让玩家三选一。
    /// </summary>
    public static class BuffRoulette
    {
        /// <summary>
        /// 策划案 §4.1 稀有度权重：白 50 / 蓝 30 / 紫 15 / 金 5（共 100）
        /// </summary>
        private static readonly Dictionary<EBuffRarity, int> RarityWeights = new Dictionary<EBuffRarity, int>
        {
            { EBuffRarity.White, 50 },
            { EBuffRarity.Blue, 30 },
            { EBuffRarity.Purple, 15 },
            { EBuffRarity.Gold, 5 },
        };

        /// <summary>
        /// 从 Buff 池按权重抽 3 个不同 BuffData。
        /// 池是外部传入的（策划案"30+ 种"），保证每抽结果互不相同。
        ///
        /// 修复：
        ///   - 用 HashSet 查重（O(1)），不再用 List.Contains（O(n)）
        ///   - 加权求和 total=0 时（所有 rarity 都不在字典里）走顺序兜底，保证能返回 3 个
        ///   - maxStack=0 的 Buff 自动过滤（防御性）
        /// </summary>
        public static List<BuffData> RollThree(IReadOnlyList<BuffData> pool, System.Random rnd)
        {
            var picks = new List<BuffData>(3);
            if (pool == null || pool.Count == 0) return picks;

            var pickedSet = new HashSet<BuffData>();  // O(1) 查重

            // 加权总和（过滤掉 maxStack<=0 的无效 buff）
            int total = 0;
            foreach (var b in pool)
            {
                if (b == null || b.maxStack <= 0) continue;
                total += RarityWeights.TryGetValue(b.rarity, out int w) ? w : 0;
            }

            for (int n = 0; n < 3; n++)
            {
                BuffData pick = null;

                if (total > 0)
                {
                    int target = rnd.Next(0, total);
                    int accum = 0;
                    foreach (var b in pool)
                    {
                        if (b == null || b.maxStack <= 0) continue;
                        if (pickedSet.Contains(b)) continue;
                        accum += RarityWeights.TryGetValue(b.rarity, out int w) ? w : 0;
                        if (accum > target) { pick = b; break; }
                    }
                }

                // 兜底：取第一个未选中的（加权失败或 total=0）
                if (pick == null)
                {
                    foreach (var b in pool)
                    {
                        if (b == null || b.maxStack <= 0) continue;
                        if (pickedSet.Contains(b)) continue;
                        pick = b;
                        break;
                    }
                }

                if (pick != null)
                {
                    picks.Add(pick);
                    pickedSet.Add(pick);
                }
            }
            return picks;
        }
    }
}