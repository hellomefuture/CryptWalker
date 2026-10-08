using System.Collections.Generic;
using CryptWalker.HotUpdate.Data;
using UnityEngine;

namespace CryptWalker.HotUpdate.BuffSystem
{
    /// <summary>
    /// Buff 管理（策划案 §4.1）。
    /// 挂在玩家身上，记录所有 BuffInstance；提供 Add/Roll/Query 接口。
    /// </summary>
    public class BuffManager : MonoBehaviour
    {
        private readonly Dictionary<BuffData, BuffInstance> _buffs = new Dictionary<BuffData, BuffInstance>();

        public IReadOnlyDictionary<BuffData, BuffInstance> All => _buffs;

        /// <summary>
        /// 增加 Buff（已存在则叠加，否则新建）。
        /// 修复：达 maxStack 上限返回 null（提示调用  方已满）。
        /// </summary>
        public BuffInstance AddBuff(BuffData data)
        {
            if (data == null || data.maxStack <= 0) return null;
            if (_buffs.TryGetValue(data, out var inst))
            {
                if (inst.StackCount < data.maxStack) inst.StackCount++;
                else return null;   // 已达上限
            }
            else
            {
                inst = new BuffInstance(data, 1);
                _buffs[data] = inst;
            }
            Core.Log.Info($"[BuffManager] 添加 {data.displayName} ×{inst.StackCount}（{inst.GetEffectiveValue():F2}）");
            return inst;
        }

        /// <summary>移除 Buff</summary>
        public void RemoveBuff(BuffData data)
        {
            if (data != null) _buffs.Remove(data);
        }

        /// <summary>
        /// 查询指定 effectType 的总和（策划 §4.1 用于 Modifier 聚合）。
        /// ModifierPipeline.Build 调用此方法填充 ModifierSnapshot。
        /// </summary>
        public float AggregateValue(EBuffEffectType type)
        {
            float sum = 0f;
            foreach (var kv in _buffs)
            {
                if (kv.Key.effectType == type) sum += kv.Value.GetEffectiveValue();
            }
            return sum;
        }

        /// <summary>
        /// 随机抽 3 个不同 Buff 给玩家三选一。
        ///
        /// 修复：seed 默认值含义反了——之前 `seed=0 时随机，seed≠0 时固定`，应该反过来。
        /// 正确语义：seed=-1（默认）= 随机；seed≥0 = 用该 seed 复现（调试/回放用）。
        /// </summary>
        /// <param name="pool">Buff 池</param>
        /// <param name="seed">随机种子，-1 表示真随机（默认），≥0 表示用固定种子（可复现）</param>
        public List<BuffData> RollForLevelUp(IReadOnlyList<BuffData> pool, int seed = -1)
        {
            var rnd = seed < 0 ? new System.Random() : new System.Random(seed);
            return BuffRoulette.RollThree(pool, rnd);
        }
    }
}