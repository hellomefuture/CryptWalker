using UnityEngine;

namespace CryptWalker.HotUpdate.Combat
{
    /// <summary>
    /// 近战武器：策划案 §3.1 长剑。
    /// 三连击：每段独立伤害，独立动画，攻击状态连击由 PlayerFSM 串起来（策划案 §3.6）。
    /// 突进斩：特殊技能，玩家向面对方向位移 + 矩形范围伤害（策划案 §3.1 specialSkillType=dash_forward）。
    /// </summary>
    public class MeleeWeapon : WeaponBase
    {
        [Header("近战调试")]
        [SerializeField] private float arcAngle = 90f;   // 扇形攻击角度（度）

        public override void DoAttack(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker, Vector2 aimDir)
        {
            if (attacker == null) return;

            // 近战命中判定：扇形范围（策划案 §3.3 主公式统一走 DamageCalculator）
            float range = AttackRange;
            float halfArc = arcAngle * 0.5f;
            var snap = ModifierPipeline.Build(attacker);

            var hits = Physics2D.OverlapCircleAll(attacker.transform.position, range);
            int hitCount = 0;
            foreach (var c in hits)
            {
                var enemy = c.GetComponent<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>();
                if (enemy == null || enemy.IsDead) continue;

                // 扇形角度判定（dot > cos(halfArc) 等价于 angle < halfArc，更快）
                Vector2 toEnemy = (Vector2)enemy.transform.position - (Vector2)attacker.transform.position;
                if (toEnemy.sqrMagnitude < 0.01f) continue;   // 防止 normalize NaN
                Vector2 toEnemyN = toEnemy.normalized;
                float cosAngle = Vector2.Dot(aimDir.normalized, toEnemyN);
                float cosThreshold = Mathf.Cos(halfArc * Mathf.Deg2Rad);
                if (cosAngle < cosThreshold) continue;

                // 单段伤害（连击伤害叠加在 PlayerFSM 里按 combo 阶段 × 系数）
                // 传 this 作为 attacker → EnemyBase.TakeDamage 会自动附加元素印记
                float dmg = DamageCalculator.Calc(BaseAttack, WeaponMultiplier, enemy.Defense, snap);
                enemy.TakeDamage(dmg, this);
                hitCount++;
            }

            Core.Log.Info($"[MeleeWeapon] 三连击段命中 {hitCount} 敌，aim={aimDir}");
        }

        public override void DoSpecialSkill(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker, Vector2 aimDir)
        {
            if (attacker == null) return;

            // 突进斩：策划案 §3.1 dash_forward
            // 1) 玩家朝 aimDir 方向位移（策划案 §3.1 闪避/突进：1~2 格距离）
            // 2) 位移路径上的矩形范围受伤害
            float dashDistance = 3f;
            Vector3 targetPos = attacker.transform.position + (Vector3)(aimDir * dashDistance);

            // 修复：用 Rigidbody2D.MovePosition 平滑移动（避免 transform.position = ... 瞬移穿墙）
            var rb = attacker.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.MovePosition(targetPos);
            }
            else
            {
                attacker.transform.position = targetPos;
            }

            // 矩形范围伤害（策划案：突进斩 = 路径矩形）
            // 修复：OverlapBoxAll 的 angle 参数是度数（float），不能用 Vector2 aimDir
            float rectWidth = 1.5f;
            float boxAngle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
            var snap = ModifierPipeline.Build(attacker);
            var hits = Physics2D.OverlapBoxAll(targetPos, new Vector2(dashDistance, rectWidth), boxAngle);
            foreach (var c in hits)
            {
                var enemy = c.GetComponent<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>();
                if (enemy == null) continue;
                float dmg = DamageCalculator.Calc(BaseAttack, WeaponMultiplier * 1.5f, enemy.Defense, snap);
                enemy.TakeDamage(dmg, this);
            }

            Core.Log.Info($"[MeleeWeapon] 突进斩 → {targetPos}");
        }
    }
}