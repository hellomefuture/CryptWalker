using CryptWalker.HotUpdate.Data;   // WeaponConfig（已搬进 AOT，命名空间沿用旧值 ✓）
using CryptWalker.Main.Data;
using UnityEngine;

namespace CryptWalker.Main.Bridge
{
    /// <summary>
    /// 武器的 **AOT 侧代理组件** —— 挂在 player.prefab 上，代替原来的热更 `MeleeWeapon` ✗。
    ///
    /// 与另外两个代理的区别 ✗：武器类型是**多态**的（Melee / Ranged / Spell ✓），
    /// 所以这里用**类型全名**而不是写死 ✗ —— 换武器只改这个字符串 ✓，不需要新写一个代理类 ✓。
    ///
    /// 数值从 `WeaponConfig`（SO ✓ 已移到 AOT ✓）读 ✓；
    /// `arcAngle` 这类"每种武器自己的参数"目前**仍留在热更类里用代码默认值** ✗ ——
    /// 因为当前 prefab 上它的值（90 ✓）恰好等于默认值 ✓，搬与不搬行为一样 ✓。
    /// 将来要按 prefab 调它 ✗，就把它加进 WeaponConfig ✓（那才是它该待的地方 ✓）。
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponProxy : MonoBehaviour
    {
        [Tooltip("热更武器组件类型全名 ✓（Melee 近战 / Ranged 远程 / Spell 法术 ✗）")]
        [SerializeField] private string hotTypeFullName = "CryptWalker.HotUpdate.Combat.MeleeWeapon";

        [Tooltip("武器配置（AOT 的 ScriptableObject，如 Weapon_Sword.asset ✓）")]
        [SerializeField] private WeaponConfig config;

        [Tooltip("弹道/特效发出点。为空则热更侧用 transform 兜底 ✓")]
        [SerializeField] private Transform firePoint;

        [Tooltip("武器槽序号（0 / 1 / 2 ✓）—— 对应选武器面板里 Button1 / Button2 / Button3 的顺序 ✓\n" +
                 "PlayerController 按这个序号给武器排序 ✓ 所以顺序由你在这里定 ✓\n" +
                 "（不依赖组件在 Inspector 里的先后 ✗ —— 多个代理的 Awake 顺序是不保证的 ✓）")]
        [SerializeField] private int slot = 0;

        public WeaponConfig Config => config;

        /// <summary>槽位序号（供热更侧的 PlayerController 读取 ✓）</summary>
        public int Slot => slot;

        /// <summary>
        /// 运行时创建出来的热更武器组件 ✓（MeleeWeapon / RangedWeapon / SpellWeapon ✓）。
        /// 热更侧拿不到"类型"，只能拿到 Component ✓ → 靠它把"槽位"和"武器实例"对上 ✓
        /// </summary>
        public Component HotComponent { get; private set; }

        private void Awake()
        {
            var hot = HotBehaviourFactory.AddHotComponent(gameObject, hotTypeFullName);
            if (hot == null) return;

            // 先暴露出去再判 config ✓ —— 这样即使配置漏拖 ✓ 热更侧仍能拿到武器实例 ✓
            HotComponent = hot;

            if (config == null)
            {
                Debug.LogWarning($"[WeaponProxy] {name} 的 config 为空 ✗ → 武器将没有数值（检查 prefab 上的 WeaponProxy ✓）");
                return;
            }

            HotBehaviourFactory.ApplyTuning(hot, new WeaponTuning
            {
                config = config,
                firePoint = firePoint,
            });
        }
    }
}
