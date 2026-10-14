using CryptWalker.HotUpdate.Data;   // WeaponConfig —— 已搬进 AOT，命名空间沿用旧值（见 WeaponConfig.cs 顶部注释 ✓）
using CryptWalker.Main.Data;        // PlayerConfig（PlayerTuning 里引用 ✓）
using UnityEngine;

namespace CryptWalker.Main.Bridge
{
    /// <summary>
    /// 热更侧组件的"可注入配置"接口（**AOT 侧接口** ✗）。
    ///
    /// 为什么需要它：AOT 的代理组件要在运行时把配置注给**热更**组件 ✗，
    /// 但 AOT 代码**不能引用**热更类型 ✗（编译期看不到 ✓）。
    /// 解法：定义一个 AOT 接口 ✓ → 热更类实现它 ✓ → AOT 侧只认接口 ✓✓
    ///   （接口在 AOT ✓、实现类在热更 ✓，方向正确 ✓：热更能看见 AOT ✗ 反过来不行 ✓）
    ///
    /// 参数用 object 是**故意的** ✗：接口在 AOT ✓，没法声明"某个热更类型"的参数 ✗。
    /// 实现方直接强转成自己约定的 AOT 配置类型即可 ✓（只有对应代理会调它 ✓ 类型一定对 ✓）。
    /// </summary>
    public interface IHotTunable
    {
        /// <summary>注入配置。tuning 的实际类型由实现方约定（见各 Proxy ✗）。</summary>
        void ApplyTuning(object tuning);
    }

    /// <summary>
    /// 武器相关的注入载荷（AOT ✗）—— 给 WeaponBase.ApplyTuning 用 ✓
    /// </summary>
    public class WeaponTuning
    {
        /// <summary>武器配置 SO（AOT ✓，可安全被资产引用 ✓）</summary>
        public WeaponConfig config;
        /// <summary>弹道/特效发出点（prefab 内部对象 ✗ 由 WeaponProxy 直接序列化 ✓）</summary>
        public Transform firePoint;
    }

    /// <summary>
    /// 玩家相关的注入载荷（AOT ✗）—— 给 PlayerController.ApplyTuning 用 ✓
    /// 数值在 PlayerConfig ✓；对象引用只能来自代理组件 ✓（见 PlayerConfig 的注释 ✓）
    /// </summary>
    public class PlayerTuning
    {
        public PlayerConfig config;
        public Transform firePoint;
        public Transform weaponPivot;
        public SpriteRenderer weaponRenderer;
    }

    /// <summary>
    /// 运行时创建热更组件的工厂（AOT ✗）。
    ///
    /// 为什么不得不走"字符串 + 反射" ✗：
    ///   AOT 代码在**编译期**看不见热更程序集 ✗（它还没被编译进 player ✓ 是运行时 Assembly.Load 进来的 ✓）
    ///   → 只能用**类型全名**在运行时查 ✗
    ///
    /// 代价与保障：
    ///   · 每个 prefab 实例化时查一次 ✓（Type.GetType 有内部缓存 ✓ 开销可接受 ✓）
    ///   · 查不到时**只报错一次并降级** ✗（不让一个配置错误把整局打崩 ✗）
    /// </summary>
    public static class HotBehaviourFactory
    {
        /// <summary>热更程序集的简单名（= CryptWalker.HotUpdate.dll ✓）</summary>
        private const string HotAssemblyName = "CryptWalker.HotUpdate";

        private static bool s_warnedNoAssembly;

        /// <summary>
        /// 按类型全名给 go 挂上热更组件。失败返回 null（并只报错一次 ✗）。
        /// </summary>
        /// <param name="hotTypeFullName">如 "CryptWalker.HotUpdate.Entity.Enemy.EnemyBase"</param>
        public static Component AddHotComponent(GameObject go, string hotTypeFullName)
        {
            if (go == null || string.IsNullOrEmpty(hotTypeFullName)) return null;

            // 程序集限定名：热更 dll 是运行时加载的 ✗ 所以必须带上程序集名 ✓
            var type = System.Type.GetType(hotTypeFullName + ", " + HotAssemblyName, false);
            if (type == null)
            {
                if (!s_warnedNoAssembly)
                {
                    s_warnedNoAssembly = true;
                    Debug.LogError($"[HotProxy] 找不到热更类型「{hotTypeFullName}」✗ —— " +
                                   "通常是热更程序集还没加载（检查 StreamingAssets/HybridCLR/HotUpdateDlls ✗）" +
                                   "或类型全名写错 ✗。已跳过挂载，游戏会降级运行。");
                }
                return null;
            }

            return go.AddComponent(type);
        }

        /// <summary>注入配置（组件没实现 IHotTunable 时静默跳过 ✗ 并提示一次 ✓）。</summary>
        public static void ApplyTuning(Component hotComponent, object tuning)
        {
            if (hotComponent == null || tuning == null) return;

            if (hotComponent is IHotTunable t)
            {
                t.ApplyTuning(tuning);
            }
            else
            {
                Debug.LogWarning($"[HotProxy] {hotComponent.GetType().Name} 没有实现 IHotTunable ✗，" +
                                 "配置未注入 → 它会退化成代码里的默认数值。");
            }
        }
    }
}
