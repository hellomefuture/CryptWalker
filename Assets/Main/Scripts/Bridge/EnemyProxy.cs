using CryptWalker.Main.Data;
using UnityEngine;

namespace CryptWalker.Main.Bridge
{
    /// <summary>
    /// 敌人的 **AOT 侧代理组件** —— 挂在 enemy prefab 上，代替原来的热更 `EnemyBase` 组件 ✗。
    ///
    /// ===== 为什么必须这么绕 =====
    /// Unity 解析 prefab 里的脚本引用，靠的是**打包时那份脚本表** ✗。
    /// 热更程序集是运行时 Assembly.Load 进来的 ✗，不在那份表里 ✗
    /// → prefab 上直接挂热更脚本 → 真机包里组件变 `missing script` ✗（实测 39 条 ✓）
    ///
    /// 所以改成：
    ///   1. prefab 上挂**本组件**（AOT ✓ 打包后永远绑得上 ✓）
    ///   2. 它持有一个 AOT 的 EnemyConfig（SO ✓ 资产引用安全 ✓）
    ///   3. 运行时（Awake）由它创建真正的热更 `EnemyBase` 并把数值注入进去 ✓
    ///
    /// 时序（很重要 ✓）：
    ///   Instantiate → 本组件 Awake → AddComponent(EnemyBase)
    ///   → **EnemyBase 自己的 Awake 立刻执行**（此时字段还是代码默认值 ✗）
    ///   → ApplyTuning 覆盖所有数值 ✗ → **并在热更侧重新派生** Hp 等
    ///     （否则 Hp = Awake 那一刻的 maxHp ✗ 见 EnemyBase.ApplyTuning 的注释 ✓）
    ///   → 之后 DungeonGenerator 再注入 Target / NavGrid ✓（那时组件已存在 ✓ GetComponent 拿得到 ✓）
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyProxy : MonoBehaviour
    {
        private const string HotTypeName = "CryptWalker.HotUpdate.Entity.Enemy.EnemyBase";

        [Tooltip("敌人数值配置（AOT 的 ScriptableObject）。留空 → 用 EnemyBase 的代码默认数值，敌人会变成「木桩」✗")]
        [SerializeField] private EnemyConfig config;

        /// <summary>供热更侧读取（DungeonGenerator 想拿 config 时用 ✓）</summary>
        public EnemyConfig Config => config;

        private void Awake()
        {
            var hot = HotBehaviourFactory.AddHotComponent(gameObject, HotTypeName);
            if (hot == null) return;

            if (config == null)
            {
                Debug.LogWarning($"[EnemyProxy] {name} 的 config 为空 ✗ → 敌人将使用代码默认数值（检查 prefab 上的 EnemyProxy ✓）");
                return;
            }

            HotBehaviourFactory.ApplyTuning(hot, config);
        }
    }
}
