using CryptWalker.HotUpdate.Pool;
using UnityEngine;

namespace CryptWalker.HotUpdate.Combat
{
    /// <summary>
    /// 弹道 / 投射物（策划案 §3.1 长弓 + 法杖用）。
    ///
    /// 生命周期：
    ///   1) 武器从对象池 Get() 取出 → 调 Init(baseAttack, multiplier, dir, speed, attacker, element, pierce)
    ///   2) Update 朝 dir 方向匀速飞行
    ///   3) 命中首个敌人：Calc 实时算 dmg（含敌人 Defense）+ 元素印记，然后归还池（或穿透则继续飞行）
    ///   4) 飞行 N 秒后自动归还池（防卡死场景）
    ///
    /// 修复记录：
    ///   - 命中时重新计算 dmg（之前传 0f 防御 → 伤害无限大）
    ///   - 命中时主动附加元素印记（不让 EnemyBase.TakeDamage 去找 weapon 组件）
    ///   - 接入对象池（AGENTS.md §3.8-36）：实现 IPoolable，命中/超时归还池而非 Destroy
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class Projectile : MonoBehaviour, IPoolable
    {
        private float _baseAttack;
        private float _multiplier;
        private Vector2 _dir;
        private float _speed;
        private GameObject _attacker;
        private Data.EElementType _element;
        private int _pierceRemaining;
        private float _lifeTime;
        private const float MaxLife = 5f;

        /// <summary>归属对象池（由发射武器注入）。为 null 时退化为 Destroy，保证未接池也能跑。</summary>
        private ObjectPool<Projectile> _pool;

        /// <summary>注入归属池（AGENTS.md §3.8-36：弹道必须复用，不能 Instantiate/Destroy 来回创建）</summary>
        public void SetPool(ObjectPool<Projectile> pool) => _pool = pool;

        // ==================== IPoolable ====================

        /// <summary>从池取出时复位（其余字段由 Init 补齐）</summary>
        public void OnSpawn()
        {
            _lifeTime = 0f;
        }

        /// <summary>归还池时清理，避免下一发复用上一发的残留状态</summary>
        public void OnRecycle()
        {
            _lifeTime = 0f;
            _pierceRemaining = 0;
            _attacker = null;
            _dir = Vector2.zero;
        }

        /// <summary>
        /// 初始化（策划案 §3.1 长弓 + 法杖用）。
        /// </summary>
        /// <param name="baseAttack">武器基础攻击（命中时按敌人 Defense 实时算）</param>
        /// <param name="multiplier">武器倍率（含蓄力加成）</param>
        /// <param name="dir">飞行方向（已 normalize）</param>
        /// <param name="speed">飞行速度（世界单位/秒）</param>
        /// <param name="attacker">攻击方（玩家 GameObject）</param>
        /// <param name="element">元素类型（策划案 §3.2 印记附着）</param>
        /// <param name="pierce">穿透次数（策划案 §3.1 piercing 词条）</param>
        public void Init(float baseAttack, float multiplier, Vector2 dir, float speed,
                         GameObject attacker, Data.EElementType element, int pierce)
        {
            _baseAttack = baseAttack;
            _multiplier = multiplier;
            _dir = dir.normalized;
            _speed = speed;
            _attacker = attacker;
            _element = element;
            _pierceRemaining = pierce;
            _lifeTime = 0f;

            // 朝向：旋转 z 轴对准飞行方向
            float angle = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);

            // 物理：Kinematic 模式避免物理引擎推挤造成诡异弹道
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null) rb.bodyType = RigidbodyType2D.Kinematic;
        }

        private void Update()
        {
            transform.position += (Vector3)(_dir * _speed * Time.deltaTime);

            _lifeTime += Time.deltaTime;
            if (_lifeTime > MaxLife) Recycle();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 不打自己阵营
            if (_attacker != null && other.gameObject == _attacker) return;

            var enemy = other.GetComponent<CryptWalker.HotUpdate.Entity.Enemy.EnemyBase>();
            if (enemy == null) return;

            // 修复：命中时按敌人 Defense 实时算 dmg（之前用 0f 防御 → 伤害无限大）
            var snap = _attacker != null
                ? ModifierPipeline.Build(_attacker.GetComponent<CryptWalker.HotUpdate.Entity.Player.PlayerController>())
                : new ModifierSnapshot();
            float dmg = DamageCalculator.Calc(_baseAttack, _multiplier, enemy.Defense, snap);

            // 传 null 作为 weapon，让 EnemyBase.TakeDamage 不通过 weapon 自动附元素
            // （这里由 Projectile 自己控制元素印记附着，避免与 weapon.ElementType 冲突）
            enemy.TakeDamage(dmg, null);

            // 修复：Projectile 命中时主动附加元素印记（策划案 §3.2）
            if (_element != Data.EElementType.None)
            {
                var marks = enemy.GetComponent<EnemyElementMarks>();
                if (marks != null)
                {
                    marks.AddMark(_element);
                }
            }

            // 穿透逻辑（策划案 §3.1 piercing 词条）
            if (_pierceRemaining > 0)
            {
                _pierceRemaining--;
                return;
            }

            Recycle();
        }

        /// <summary>命中/超时后的收尾：优先归还对象池，无池时才销毁。</summary>
        private void Recycle()
        {
            if (_pool != null)
            {
                _pool.Return(this);   // 池内部会调 OnRecycle + 隐藏对象
                return;
            }
            Destroy(gameObject);
        }
    }
}