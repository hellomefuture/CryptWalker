using UnityEngine;

namespace CryptWalker.HotUpdate.Combat
{
    /// <summary>
    /// 元素反应触发器（策划案 §3.2 四种反应）。
    /// 挂在敌人身上（和 EnemyElementMarks 一起），负责把印记反应转成具体效果。
    ///
    /// 反应判定（策划案 §3.2）：
    ///   火+冰  蒸汽爆炸：半径3格伤害 = 攻击×1.8，致盲 2s（攻击 50% Miss）
    ///   火+雷  过载爆发：半径4格伤害 = 攻击×2.2，击退 3 格 + 打断施法
    ///   冰+雷  感电连锁：初始 = 攻击×1.2，连锁 +1 次 ×1.15，最多链 5 敌
    ///   火+毒  剧毒爆燃：伤害 = 已损生命×25%（Boss 衰减至 10%），移除目标全部毒层
    /// </summary>
    [RequireComponent(typeof(EnemyElementMarks))]
    public class ElementReactionTrigger : MonoBehaviour
    {
        private EnemyElementMarks _marks;
        private CryptWalker.HotUpdate.Entity.Enemy.EnemyBase _enemy;

        private void Awake()
        {
            _marks = GetComponent<EnemyElementMarks>();
            _enemy = GetComponent<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>();
        }

        private void Update()
        {
            // 每帧检测：是否触发反应
            _marks?.TryReact((a, b, dt) =>
            {
                var reaction = EnemyElementMarks.GetReactionType(a, b);
                switch (reaction)
                {
                    case ElementReaction.Vapor: TriggerVaporExplosion(a, b); break;
                    case ElementReaction.Overload: TriggerOverload(a, b); break;
                    case ElementReaction.ElectroChained: TriggerElectroChain(a, b); break;
                    case ElementReaction.PoisonDetonation: TriggerPoisonDetonation(a, b); break;
                }
            });
        }

        // === 各反应具体效果实现（策划案 §3.2 公式）===

        private void TriggerVaporExplosion(Data.EElementType a, Data.EElementType b)
        {
            // 半径3格，伤害 = 攻击×1.8 + 致盲 2s（50% Miss）
            float radius = 3f;
            float attackBase = _enemy != null ? _enemy.Attack : 10f;
            float dmg = attackBase * 1.8f;

            var hits = Physics2D.OverlapCircleAll(transform.position, radius);
            foreach (var c in hits)
            {
                var enemy = c.GetComponent<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>();
                if (enemy == null) continue;
                enemy.TakeDamage(dmg, null);
                // 修复：标记敌人"致盲 2s"——用一个最小 Buff（策划 §3.2 "50% Miss"）
                // 简单实现：用 EnemyBase 自带 IsBlinded 标记（如果敌人有玩家目标，攻击 50% 概率 miss）
                enemy.ApplyBlind(2f);
            }
            Core.Log.Info($"[ElementReaction] 蒸汽爆炸 @ {transform.position}");
        }

        private void TriggerOverload(Data.EElementType a, Data.EElementType b)
        {
            // 半径4格，伤害 = 攻击×2.2 + 击退 3 格 + 打断施法
            float radius = 4f;
            float attackBase = _enemy != null ? _enemy.Attack : 10f;
            float dmg = attackBase * 2.2f;

            var hits = Physics2D.OverlapCircleAll(transform.position, radius);
            foreach (var c in hits)
            {
                var enemy = c.GetComponent<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>();
                if (enemy == null) continue;
                enemy.TakeDamage(dmg, null);
                // 击退（策划案 §3.1 击退方向跟随来袭反方向，这里用相对位置推）
                Vector2 knockback = ((Vector2)enemy.transform.position - (Vector2)transform.position).normalized * 3f;
                enemy.ApplyKnockback(knockback, 0.2f);
                // 修复：打断施法（策划 §3.2）。敌人当前没有"施法状态"概念（攻击是即时结算）
                // 简化：标记 IsStunned 一段时间，影响 EnemyFSM 状态转换
                enemy.ApplyStun(0.5f);
            }
            Core.Log.Info($"[ElementReaction] 过载爆发 @ {transform.position}");
        }

        private void TriggerElectroChain(Data.EElementType a, Data.EElementType b)
        {
            // 冰+雷：初始 = 攻击×1.2，连锁 +1 次 ×1.15，最多链 5 敌
            float attackBase = _enemy != null ? _enemy.Attack : 10f;
            float dmg = attackBase * 1.2f;
            const int MaxChain = 5;

            CryptWalker.HotUpdate.Entity.Enemy.EnemyBase current = _enemy;
            for (int i = 0; i < MaxChain && current != null; i++)
            {
                current.TakeDamage(dmg, null);
                // 找最近的下一个敌人（策划案："自动索敌"）
                CryptWalker.HotUpdate.Entity.Enemy.EnemyBase next = FindNearestEnemy(current.transform.position, current, 5f);
                dmg *= 1.15f;   // 连锁衰减
                current = next;
            }
            Core.Log.Info($"[ElementReaction] 感电连锁触发");
        }

        private void TriggerPoisonDetonation(Data.EElementType a, Data.EElementType b)
        {
            // 火+毒：伤害 = 已损生命×25%（Boss衰减10%），移除目标全部毒层
            if (_enemy == null) return;
            float lostHp = _enemy.MaxHp - _enemy.CurrentHp;
            float lostPct = _enemy.IsBoss ? 0.10f : 0.25f;
            float dmg = lostHp * lostPct;
            _enemy.TakeDamage(dmg, null);
            _marks.RemoveAllPoisonStacks();
            Core.Log.Info($"[ElementReaction] 剧毒爆燃 → 伤害 {dmg:F1} (已损 {lostPct:P0})");
        }

        private CryptWalker.HotUpdate.Entity.Enemy.EnemyBase FindNearestEnemy(Vector3 from, CryptWalker.HotUpdate.Entity.Enemy.EnemyBase exclude, float maxDist)
        {
            CryptWalker.HotUpdate.Entity.Enemy.EnemyBase nearest = null;
            float nearestDist = maxDist;
            foreach (var e in FindObjectsOfType<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>())
            {
                if (e == exclude) continue;
                float d = Vector3.Distance(from, e.transform.position);
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearest = e;
                }
            }
            return nearest;
        }
    }
}