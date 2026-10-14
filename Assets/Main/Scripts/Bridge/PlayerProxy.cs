using CryptWalker.Main.Data;
using UnityEngine;

namespace CryptWalker.Main.Bridge
{
    /// <summary>
    /// 玩家的 **AOT 侧代理组件** —— 挂在 player.prefab 上，代替原来的热更 `PlayerController` ✗。
    /// 存在理由与 EnemyProxy 完全相同（见那里的详细注释 ✓）。
    ///
    /// 它比 EnemyProxy 多带 3 个**对象引用** ✗：
    ///   firePoint / weaponPivot / weaponRenderer
    /// 为什么这些不放 SO ✗：它们指向的是 **player.prefab 内部的子节点** ✗，
    /// 而 SO 是独立资产 ✓ 让它去引用某个 prefab 的实例内节点既不可靠也不该做 ✗。
    /// 放在 prefab 自己的组件上才是正确的归宿 ✓，运行时通过 PlayerTuning 一起交给热更侧 ✓。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerProxy : MonoBehaviour
    {
        private const string HotTypeName = "CryptWalker.HotUpdate.Entity.Player.PlayerController";

        [Tooltip("玩家数值配置（AOT 的 ScriptableObject）。留空 → 用代码默认值 ✗")]
        [SerializeField] private PlayerConfig config;

        [Header("对象引用（prefab 内部节点 ✓ 必须留在 prefab 上 ✗）")]
        [Tooltip("弹道/特效发出点（手部/枪口）。为空则热更侧用 transform 兜底 ✓")]
        [SerializeField] private Transform firePoint;

        [Tooltip("武器挂点（子节点，位置=握把）。留空则热更侧按名字 WeaponPivot 找 ✓")]
        [SerializeField] private Transform weaponPivot;

        [Tooltip("武器贴图的 SpriteRenderer（WeaponPivot 的子节点）。留空则热更侧自动找 ✓")]
        [SerializeField] private SpriteRenderer weaponRenderer;

        public PlayerConfig Config => config;

        private void Awake()
        {
            var hot = HotBehaviourFactory.AddHotComponent(gameObject, HotTypeName);
            if (hot == null) return;

            if (config == null)
            {
                Debug.LogWarning($"[PlayerProxy] {name} 的 config 为空 ✗ → 玩家将使用代码默认数值（检查 player.prefab 上的 PlayerProxy ✓）");
                return;
            }

            HotBehaviourFactory.ApplyTuning(hot, new PlayerTuning
            {
                config = config,
                firePoint = firePoint,
                weaponPivot = weaponPivot,
                weaponRenderer = weaponRenderer,
            });
        }
    }
}
