using CryptWalker.HotUpdate.Pool;
using UnityEngine;

namespace CryptWalker.HotUpdate.Combat
{
    /// <summary>
    /// 远程武器：策划案 §3.1 长弓。
    /// 蓄力射击：右键开始蓄力 → 蓄满倍率提升至 2.0 → 松开发射。
    /// 弹道飞行：发射 Projectile prefab，飞行 + 命中检测 + 应用伤害 + 元素印记。
    /// 弹道穿透：词条 piercing（策划案 §3.1）穿透次数。
    ///
    /// 修复：
    ///   - 用 [SerializeField] prefab 引用 + WeaponBase 字段暴露（不直接读 WeaponConfig，避免字段嵌套太深）
    ///   - Calc 传 weaponElementType（策划 §3.3 元素附伤走主公式）
    ///   - 加 TickChargeRelease 文档（必须由外部 Update 调用，否则长弓无法自动松开发射）
    /// </summary>
    public class RangedWeapon : WeaponBase
    {
        [Header("弹道")]
        [Tooltip("弹道 prefab（策划 §8 用 ObjectPool 池化）。策划 §3.1 长弓用的普通弹道")]
        [SerializeField] private GameObject projectilePrefab;
        [Tooltip("弹道飞行速度（世界单位/秒）")]
        [SerializeField] private float baseSpeed = 12f;

        [Header("蓄力")]
        [SerializeField] private float chargeTime = 1.0f;     // 蓄满时间（策划案默认 1s）

        private float _chargeStart;                          // 0 = 未蓄力；>0 = 蓄力开始时间戳
        private bool _isCharging => _chargeStart > 0f;
        private float _chargeFactor => _isCharging ? Mathf.Clamp01((Time.time - _chargeStart) / chargeTime) : 0f;

        // 弹道对象池（延迟注册：首次开火时才建池，避免武器一挂上就预分配一堆对象）
        private ObjectPool<Projectile> _projectilePool;
        private const string ProjectilePoolName = "Projectile_Ranged";

        public bool IsCharging => _isCharging;

        public override void DoAttack(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker, Vector2 aimDir)
        {
            // 长弓主攻击 = 立即发射（不蓄力也能打，倍率按当前蓄力计算）
            FireProjectile(attacker, aimDir, _chargeFactor);
            _chargeStart = 0f;  // 释放后清蓄力
        }

        public override void DoSpecialSkill(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker, Vector2 aimDir)
        {
            // 长弓特殊技能 = 开始蓄力（策划案：右键按 → 蓄力，松开发射）
            if (!_isCharging) _chargeStart = Time.time;
        }

        /// <summary>
        /// 每帧检查：蓄满或松键时自动发射（由 PlayerController.Update 调用）。
        ///
        /// 🔴 必须修：之前这个方法没人调，长弓永远无法自动发射。PlayerController 应每帧调：
        ///   if (currentWeapon is RangedWeapon rw) rw.TickChargeRelease(this, aimDir, mouseUp);
        /// </summary>
        public void TickChargeRelease(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker, Vector2 aimDir, bool mouseUp)
        {
            if (!_isCharging) return;

            // 蓄满自动发射 / 松键发射
            if (_chargeFactor >= 1f || mouseUp)
            {
                FireProjectile(attacker, aimDir, _chargeFactor);
                _chargeStart = 0f;
            }
        }

        private void FireProjectile(CryptWalker.HotUpdate.Entity.Player.PlayerController attacker, Vector2 aimDir, float charge)
        {
            if (attacker == null) return;
            if (projectilePrefab == null)
            {
                Core.Log.Warn("[RangedWeapon] projectilePrefab 未指定（策划 §3.1 必须先在 Inspector 挂弹道 prefab）");
                return;
            }

            // 倍率：未蓄力 1.3，蓄满 2.0（策划案 §3.3）
            float multiplier = Mathf.Lerp(1.3f, 2.0f, charge);
            var snap = ModifierPipeline.Build(attacker);

            // 修复：传 weaponElementType 让策划 §3.3 元素附伤走主公式
            // 弹道命中时按敌人 Defense 实时算 dmg，这里只算"参考 dmg"（用于显示）
            float dmg = DamageCalculator.Calc(BaseAttack, multiplier, 0f, snap, ElementType);

            var pos = firePoint != null ? firePoint.position : attacker.transform.position;

            // 修复：弹道改为对象池复用（AGENTS.md §3.8-36 禁止 Instantiate/Destroy 来回创建子弹）
            Projectile proj = SpawnProjectile(pos);
            if (proj == null) return;

            // 策划 §4.2 多重箭矢遗物：额外弹道数（接口预留，由 PlayerController 注入 RelicManager）
            int extraProj = 0;
            var relicMgr = attacker.GetComponent<CryptWalker.HotUpdate.BuffSystem.RelicManager>();
            if (relicMgr != null) extraProj = relicMgr.ReadExtraProjectile();

            proj.Init(BaseAttack, multiplier, aimDir, baseSpeed, attacker.gameObject, ElementType, /*pierce*/ extraProj);

            Core.Log.Info($"[RangedWeapon] 发射弹道 charge={charge:F2} dmg={dmg:F1} extra={extraProj}");
        }

        /// <summary>
        /// 从对象池取一枚弹道（首次调用时按 prefab 建池）。
        /// 池不可用（PoolManager 未初始化）时退化为一次性 Instantiate，
        /// 目的是"池化失败也不能让武器开不出火"。
        /// </summary>
        private Projectile SpawnProjectile(Vector3 pos)
        {
            var pool = GetProjectilePool();
            if (pool != null)
            {
                var pooled = pool.Get();
                if (pooled == null) return null;
                pooled.SetPool(pool);            // 注入归属池，命中后归还而非 Destroy
                pooled.transform.position = pos;
                return pooled;
            }

            Core.Log.Warn("[RangedWeapon] PoolManager 不可用，弹道退化为 Instantiate（不影响开火）");
            var go = Object.Instantiate(projectilePrefab, pos, Quaternion.identity);
            var proj = go.GetComponent<Projectile>();
            if (proj == null) proj = go.AddComponent<Projectile>();
            return proj;   // 未注入池 → Projectile 命中后走 Destroy 兜底
        }

        /// <summary>取/建弹道池。initial=0：不在首次开火时批量预分配，避免帧尖峰；上限按策划案 §8 的 60。</summary>
        private ObjectPool<Projectile> GetProjectilePool()
        {
            if (_projectilePool != null) return _projectilePool;

            var mgr = PoolManager.Instance;
            if (mgr == null || projectilePrefab == null) return null;

            _projectilePool = mgr.Get<Projectile>(ProjectilePoolName);
            if (_projectilePool != null) return _projectilePool;

            var prefab = projectilePrefab;   // 闭包捕获局部变量，避免每次创建都读字段
            _projectilePool = mgr.RegisterPool(ProjectilePoolName,
                createFunc: () =>
                {
                    var go = Object.Instantiate(prefab);
                    var p = go.GetComponent<Projectile>();
                    return p != null ? p : go.AddComponent<Projectile>();
                },
                initial: 0,
                maxSize: 60);

            return _projectilePool;
        }
    }
}