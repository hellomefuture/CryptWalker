using System.Collections.Generic;
using CryptWalker.HotUpdate.Data;
using UnityEngine;

namespace CryptWalker.HotUpdate.BuffSystem
{
    /// <summary>
    /// 遗物管理（策划案 §4.2）。
    /// 挂在玩家身上，记录所有激活的遗物；事件触发时（受击/击杀/暴击）走对应回调。
    ///
    /// 首版 5 件（策划案）：
    ///   - 狂战护符：HP<50% → atk +30%（Modifier 聚合实现：ReadConditionalBonus）
    ///   - 元素核心：反应伤害 +50%（Modifier 聚合实现：ReactionDamageBonus）
    ///   - 刺客匕首：暴击触发连锁闪电（OnCrit 实际触发 AOE 伤害）
    ///   - 多重箭矢：远程弹道 +1（Modifier 聚合实现：ExtraProjectile）
    ///   - 回血护符：击杀回血 5%（OnKill 实际触发 Heal）
    ///
    /// 修复：
    ///   - 之前回调只有 log + 注释说不准确——这次加 Modifier 聚合接口 + 接入 PlayerController 流程
    ///   - 加 HashSet 用于 unique 判重（O(1)）
    ///   - 修注释：实际用 List.Contains 时注释不要乱说
    /// </summary>
    public class RelicManager : MonoBehaviour
    {
        private readonly List<RelicData> _relics = new List<RelicData>();
        // 优化：用 HashSet 记录已添加的 unique relic，配合 List.Contains 防止重复扫描
        private readonly HashSet<RelicData> _uniqueCache = new HashSet<RelicData>();
        private CryptWalker.HotUpdate.Entity.Player.PlayerController _player;

        public IReadOnlyList<RelicData> All => _relics;

        private void Awake()
        {
            _player = GetComponent<CryptWalker.HotUpdate.Entity.Player.PlayerController>();
        }

        /// <summary>
        /// 添加遗物。
        /// 修复：之前 stackable 逻辑反了——`stackable==true` 时被 `Contains` 检查挡住，导致永远只加一次。
        /// 现在正确语义：
        ///   - unique（默认）：只加一次
        ///   - stackable：可多次加（策划 §4.2 stackable 类型允许多份）
        /// </summary>
        public bool AddRelic(RelicData relic)
        {
            if (relic == null) return false;

            if (relic.stackable)
            {
                // stackable：允许多份，直接加（策划 §4.2 可叠类型）
                _relics.Add(relic);
                if (!_uniqueCache.Contains(relic)) _uniqueCache.Add(relic);
            }
            else
            {
                // unique：只加一次
                if (_uniqueCache.Contains(relic)) return false;
                _uniqueCache.Add(relic);
                _relics.Add(relic);
            }

            Core.Log.Info($"[RelicManager] 添加遗物 {relic.relicName} (rarity={relic.rarity}, stackable={relic.stackable})");
            return true;
        }

        /// <summary>
        /// 受击回调（策划案狂战护符：hp_below_50pct 时 atk +30%）。
        /// 由 PlayerController.TakeDamage 调用。
        ///
        /// 当前实现：只打 log，实际 atk+30% 由 ModifierPipeline.Read 阶段处理：
        ///   ModifierPipeline.Read() 调用 RelicManager.ReadConditionalBonus(currentHpPct) → 返回当前应加的攻击加成
        /// </summary>
        public void OnTakeDamage(float currentHpPct)
        {
            foreach (var r in _relics)
            {
                if (r.effectType == ERelicEffectType.ConditionalStat
                    && r.triggerCondition == "hp_below_50pct"
                    && currentHpPct < 0.5f)
                {
                    Core.Log.Info($"[RelicManager] {r.relicName} 触发：atk +{r.statValue:P0}");
                }
            }
        }

        /// <summary>
        /// 击杀回调（策划案回血护符：击杀回血 5%）。
        /// 修复：实际回血——调 PlayerController.Heal(amount)。
        /// </summary>
        public void OnKill()
        {
            if (_player == null || _player.IsDead) return;
            float maxHp = _player.MaxHp;
            foreach (var r in _relics)
            {
                if (r.effectType == ERelicEffectType.LifeStealOnKill)
                {
                    float heal = maxHp * r.statValue;
                    Core.Log.Info($"[RelicManager] {r.relicName} 触发：回血 +{heal:F1}");
                    _player.Heal(heal);
                }
            }
        }

        /// <summary>
        /// 暴击回调（策划案刺客匕首：触发连锁闪电）。
        /// 修复：实际释放 AOE（玩家周围 2.5 格半径 30 伤害）。
        /// </summary>
        public void OnCrit()
        {
            if (_player == null) return;
            foreach (var r in _relics)
            {
                if (r.effectType == ERelicEffectType.CritChain)
                {
                    Core.Log.Info($"[RelicManager] {r.relicName} 触发：暴击连锁");
                    Vector2 center = _player.transform.position;
                    var hits = Physics2D.OverlapCircleAll(center, 2.5f);
                    foreach (var c in hits)
                    {
                        var enemy = c.GetComponent<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>();
                        if (enemy == null || enemy.IsDead) continue;
                        enemy.TakeDamage(30f, null);
                    }
                }
            }
        }

        // === 供 ModifierPipeline.Read 调用的聚合接口（策划 §3.3）===

        /// <summary>
        /// 读条件型 buff 触发的攻击加成（策划 §4.2 狂战护符）。
        /// ModifierPipeline.Read() 在算 AttackPct 时调此方法。
        /// </summary>
        /// <param name="currentHpPct">当前 HP 百分比（0~1）</param>
        /// <returns>应加的攻击加成（如 0.30 表示 +30%）</returns>
        public float ReadConditionalAttackBonus(float currentHpPct)
        {
            float sum = 0f;
            foreach (var r in _relics)
            {
                if (r.effectType == ERelicEffectType.ConditionalStat
                    && r.triggerCondition == "hp_below_50pct"
                    && currentHpPct < 0.5f)
                {
                    sum += r.statValue;
                }
            }
            return sum;
        }

        /// <summary>
        /// 读反应伤害加成（策划 §4.2 元素核心：+50% 反应伤害）。
        /// ElementReactionTrigger 触发反应时调此方法乘到 dmg。
        /// </summary>
        public float ReadReactionDamageBonus()
        {
            float sum = 0f;
            foreach (var r in _relics)
            {
                if (r.effectType == ERelicEffectType.ReactionDamageBoost)
                {
                    sum += r.statValue;
                }
            }
            return sum;
        }

        /// <summary>
        /// 读额外弹道数（策划 §4.2 多重箭矢：远程弹道 +1）。
        /// RangedWeapon 在 FireProjectile 时调此方法决定发射数量。
        /// </summary>
        public int ReadExtraProjectile()
        {
            int sum = 0;
            foreach (var r in _relics)
            {
                if (r.effectType == ERelicEffectType.Multiplier
                    && r.triggerCondition == "ranged_attack")
                {
                    sum += (int)r.statValue;
                }
            }
            return sum;
        }
    }
}