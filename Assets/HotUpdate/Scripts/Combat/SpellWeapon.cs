using CryptWalker.HotUpdate.Pool;
using UnityEngine;

namespace CryptWalker.HotUpdate.Combat
{
    /// <summary>
    /// 法术武器：策划案 §3.1 元素法杖。
    /// 普通攻击 = 元素弹（带 ElementType 的小型 Projectile）。
    /// 特殊技能 = 蓄力范围法术（策划案 §3.1 蓄力范围法术，圆形 AOE 半径 3~4 格）。
    ///
    /// 修复：
    ///   - 用 SerializeField prefab 引用（去掉 Resources.Load 违反 AGENTS.md §3.9）
    ///   - Calc 传 weaponElementType（策划 §3.3 元素附伤走主公式）
    ///   - 命中后附加元素印记（之前 TODO 漏了，现在接 EnemyElementMarks）
    ///   - 元素弹接入对象池（AGENTS.md §3.8-36）
    /// </summary>
    public class SpellWeapon : WeaponBase
    {
        [Header("法球")]
        [Tooltip("元素弹 prefab（策划 §3.1 法杖主攻击）")]
        [SerializeField] private GameObject orbPrefab;
        [SerializeField] private float orbSpeed = 10f;

        // 元素弹对象池（延迟注册，首次施法才建池）
        private ObjectPool<Projectile> _orbPool;
        private const string OrbPoolName = "Projectile_Spell";

        public override void DoAttack(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker, Vector2 aimDir)
        {
            if (attacker == null) return;
            if (orbPrefab == null)
            {
                Core.Log.Warn("[SpellWeapon] orbPrefab 未指定（策划 §3.1 必须先在 Inspector 挂元素弹 prefab）");
                return;
            }

            var pos = firePoint != null ? firePoint.position : attacker.transform.position;

            // 修复：元素弹改为对象池复用（AGENTS.md §3.8-36）
            Projectile proj = SpawnOrb(pos);
            if (proj == null) return;

            // 修复：传 weaponElementType（策划 §3.3 元素附伤走主公式）
            float dmg = DamageCalculator.Calc(BaseAttack, WeaponMultiplier, 0f, ModifierPipeline.Build(attacker), ElementType);
            proj.Init(BaseAttack, WeaponMultiplier, aimDir, orbSpeed, attacker.gameObject, ElementType, /*pierce*/ 0);

            Core.Log.Info($"[SpellWeapon] 元素弹 {ElementType} → {aimDir} dmg={dmg:F1}");
        }

        /// <summary>
        /// 从对象池取一枚元素弹（首次调用时按 prefab 建池）。
        /// 池不可用（PoolManager 未初始化）时退化为一次性 Instantiate，保证攻击不哑火。
        /// </summary>
        private Projectile SpawnOrb(Vector3 pos)
        {
            var pool = GetOrbPool();
            if (pool != null)
            {
                var pooled = pool.Get();
                if (pooled == null) return null;
                pooled.SetPool(pool);            // 注入归属池，命中后归还而非 Destroy
                pooled.transform.position = pos;
                return pooled;
            }

            Core.Log.Warn("[SpellWeapon] PoolManager 不可用，元素弹退化为 Instantiate（不影响施法）");
            var go = Object.Instantiate(orbPrefab, pos, Quaternion.identity);
            var proj = go.GetComponent<Projectile>();
            if (proj == null) proj = go.AddComponent<Projectile>();
            return proj;   // 未注入池 → Projectile 命中后走 Destroy 兜底
        }

        /// <summary>取/建元素弹池。initial=0：避免首次施法批量预分配造成帧尖峰。</summary>
        private ObjectPool<Projectile> GetOrbPool()
        {
            if (_orbPool != null) return _orbPool;

            var mgr = PoolManager.Instance;
            if (mgr == null || orbPrefab == null) return null;

            _orbPool = mgr.Get<Projectile>(OrbPoolName);
            if (_orbPool != null) return _orbPool;

            var prefab = orbPrefab;
            _orbPool = mgr.RegisterPool(OrbPoolName,
                createFunc: () =>
                {
                    var go = Object.Instantiate(prefab);
                    var p = go.GetComponent<Projectile>();
                    return p != null ? p : go.AddComponent<Projectile>();
                },
                initial: 0,
                maxSize: 60);

            return _orbPool;
        }

        public override void DoSpecialSkill(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker, Vector2 aimDir)
        {
            if (attacker == null) return;

            // 蓄力范围法术（策划案 §3.1）：圆形 AOE，半径 3 格
            float aoeRadius = 3f;
            Vector3 center = attacker.transform.position + (Vector3)(aimDir * aoeRadius * 0.5f);
            var snap = ModifierPipeline.Build(attacker);
            float dmg = DamageCalculator.Calc(BaseAttack, WeaponMultiplier * 1.6f, 0f, snap, ElementType);

            var hits = Physics2D.OverlapCircleAll(center, aoeRadius);
            int hitCount = 0;
            foreach (var c in hits)
            {
                var enemy = c.GetComponent<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>();
                if (enemy == null || enemy.IsDead) continue;
                enemy.TakeDamage(dmg, this);

                // 修复：命中后挂元素印记（之前 TODO 漏了）
                var marks = enemy.GetComponent<EnemyElementMarks>();
                if (marks != null && ElementType != Data.EElementType.None)
                {
                    marks.AddMark(ElementType);
                }
                hitCount++;
            }

            Core.Log.Info($"[SpellWeapon] 蓄力 AOE @ {center} 半径 {aoeRadius} 命中 {hitCount} 敌");
        }
    }
}