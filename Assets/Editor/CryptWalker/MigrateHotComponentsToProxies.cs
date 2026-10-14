using System;
using System.Collections.Generic;
using System.Reflection;
using CryptWalker.HotUpdate.Combat;
using CryptWalker.HotUpdate.Data;      // WeaponConfig（已搬进 AOT，命名空间沿用旧值 ✓）
using CryptWalker.HotUpdate.Entity.Enemy;
using CryptWalker.HotUpdate.Entity.Player;
using CryptWalker.Main.Bridge;
using CryptWalker.Main.Data;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 一次性迁移工具：把 prefab 上的**热更组件**换成 **AOT 代理组件** ✗。
    ///
    /// ===== 为什么必须做这一步 =====
    /// Unity 解析 prefab 里的脚本引用靠"打包时那份脚本表" ✗，热更程序集不在其中 ✗
    /// → 真机包里这些组件全变 `missing script` ✗（实测 39 条 ✓）
    /// → 换成 AOT 代理后永远绑得上 ✓，热更组件由代理在运行时创建 ✓
    ///
    /// ===== 它怎么保证不丢数值 =====
    /// 靠【字段名一致】✓：代理侧配置 SO 的字段名 = 热更组件的序列化字段名 ✓
    /// 用反射逐字段按名搬迁 ✓，搬不动的（名字/类型对不上 ✓）会**逐条打印出来** ✗
    /// —— 静默丢值是最难查的 bug ✗ 所以这里宁可啰嗦 ✓
    ///
    /// ===== 幂等 =====
    /// 已经迁过的 prefab 里找不到热更组件了 ✓ → 直接跳过并提示 ✓，可以放心重复点 ✓
    ///
    /// ===== 顺序 =====
    /// 只改 prefab 与新建 SO ✓ → 跑完它再跑 `CryptWalker/构建/①` ✓ 然后 `②` ✓
    /// </summary>
    public static class MigrateHotComponentsToProxies
    {
        private const string DataDir = "Assets/Art/Data";
        private const string PrefabDir = "Assets/Art/Prefabs";

        [MenuItem("CryptWalker/迁移/把 prefab 上的热更组件搬到 AOT 代理", priority = 200)]
        public static void Run()
        {
            if (!EditorUtility.DisplayDialog("迁移热更组件",
                "将把 player / Orc1 / Orc2 / Orc3 上的热更脚本组件\n" +
                "换成 AOT 代理组件（数值会自动搬进 ScriptableObject）。\n\n" +
                "只会修改这 4 个 prefab 并新建/复用几个 .asset。\n" +
                "建议先提交一次版本，便于对比。",
                "开始迁移", "取消"))
                return;

            int changed = 0;
            changed += MigrateEnemy("Orc1") ? 1 : 0;
            changed += MigrateEnemy("Orc2") ? 1 : 0;
            changed += MigrateEnemy("Orc3") ? 1 : 0;
            changed += MigratePlayer() ? 1 : 0;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Migrate] 完成：改了 {changed} 个 prefab ✓\n" +
                      "        下一步：CryptWalker/构建/① （重建热更 dll + link.xml），再 ② 出包 ✓\n" +
                      "        复验判据：真机 Player.log 里应【0 条】missing script ✓");
        }

        // ==================== 敌人 ====================

        /// <summary>把 OrcX.prefab 上的 EnemyBase 换成 EnemyProxy + EnemyConfig ✓。</summary>
        private static bool MigrateEnemy(string enemyName)
        {
            string prefabPath = $"{PrefabDir}/{enemyName}.prefab";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath))
            {
                Debug.LogWarning($"[Migrate] 找不到 {prefabPath} ✓ 跳过");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var old = root.GetComponentInChildren<EnemyBase>(true);
                if (old == null)
                {
                    Debug.Log($"[Migrate] {enemyName}.prefab 上没有 EnemyBase ✓（可能已迁移过 ✓）跳过");
                    return false;
                }

                // 1) 建/复用配置 SO，并把旧组件的数值按名搬进去 ✓
                string assetPath = $"{DataDir}/Enemy_{enemyName}.asset";
                var cfg = AssetDatabase.LoadAssetAtPath<EnemyConfig>(assetPath);
                if (cfg == null)
                {
                    cfg = ScriptableObject.CreateInstance<EnemyConfig>();
                    AssetDatabase.CreateAsset(cfg, assetPath);
                    Debug.Log($"[Migrate] 新建配置 {assetPath} ✓");
                }
                cfg.configID = $"Enemy_{enemyName}";

                int copied = CopyFieldsByName(old, cfg, out var skipped);
                LogCopy("EnemyBase → EnemyConfig", enemyName, copied, skipped);

                // 2) 挂代理并把 config 指过去 ✓
                var proxy = root.GetComponent<EnemyProxy>();
                if (proxy == null) proxy = root.AddComponent<EnemyProxy>();
                SetPrivateField(proxy, "config", cfg);

                // 3) 摘掉热更组件 ✗（用 DestroyImmediate：prefab 内容必须当场改完 ✓）
                UnityEngine.Object.DestroyImmediate(old, true);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[Migrate] {enemyName}.prefab 迁移完成 ✓（EnemyBase ✗ → EnemyProxy ✓）");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ==================== 玩家（含武器）====================

        /// <summary>把 player.prefab 上的 PlayerController + MeleeWeapon 换成 PlayerProxy + WeaponProxy ✓。</summary>
        private static bool MigratePlayer()
        {
            string prefabPath = $"{PrefabDir}/player.prefab";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath))
            {
                Debug.LogWarning($"[Migrate] 找不到 {prefabPath} ✓ 跳过");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var oldPc = root.GetComponentInChildren<PlayerController>(true);
                var oldWp = root.GetComponentInChildren<WeaponBase>(true);
                if (oldPc == null && oldWp == null)
                {
                    Debug.Log("[Migrate] player.prefab 上既没有 PlayerController 也没有武器组件 ✓（已迁移过 ✓）跳过");
                    return false;
                }

                bool didSomething = false;

                // ---------- 玩家 ----------
                if (oldPc != null)
                {
                    string assetPath = $"{DataDir}/Player_Swordsman.asset";
                    var cfg = AssetDatabase.LoadAssetAtPath<PlayerConfig>(assetPath);
                    if (cfg == null)
                    {
                        cfg = ScriptableObject.CreateInstance<PlayerConfig>();
                        AssetDatabase.CreateAsset(cfg, assetPath);
                        Debug.Log($"[Migrate] 新建配置 {assetPath} ✓");
                    }
                    cfg.configID = "Player_Swordsman";

                    int copied = CopyFieldsByName(oldPc, cfg, out var skipped);
                    LogCopy("PlayerController → PlayerConfig", "player", copied, skipped);

                    var proxy = root.GetComponent<PlayerProxy>();
                    if (proxy == null) proxy = root.AddComponent<PlayerProxy>();
                    SetPrivateField(proxy, "config", cfg);

                    // 三个对象引用留在 prefab 上 ✓（它们是 prefab 内部节点 ✓ 见 PlayerConfig 注释 ✓）
                    SetPrivateField(proxy, "firePoint", GetPrivateField(oldPc, "firePoint"));
                    SetPrivateField(proxy, "weaponPivot", GetPrivateField(oldPc, "weaponPivot"));
                    SetPrivateField(proxy, "weaponRenderer", GetPrivateField(oldPc, "weaponRenderer"));

                    UnityEngine.Object.DestroyImmediate(oldPc, true);
                    didSomething = true;
                    Debug.Log("[Migrate] player.prefab：PlayerController ✗ → PlayerProxy ✓");
                }

                // ---------- 武器 ----------
                if (oldWp != null)
                {
                    var proxy = root.GetComponent<WeaponProxy>();
                    if (proxy == null) proxy = root.AddComponent<WeaponProxy>();

                    // 武器类型按旧组件的实际类型写进代理的字符串 ✓（MeleeWeapon / RangedWeapon / SpellWeapon ✓）
                    SetPrivateField(proxy, "hotTypeFullName",
                        $"CryptWalker.HotUpdate.Combat.{oldWp.GetType().Name}");

                    // 武器配置 SO 已经存在（如 Weapon_Sword.asset ✓ 已移到 AOT ✓）→ 直接沿用旧引用 ✓，不新建 ✓
                    var wpCfg = GetPrivateField(oldWp, "config") as WeaponConfig;
                    if (wpCfg != null) SetPrivateField(proxy, "config", wpCfg);
                    else Debug.LogWarning($"[Migrate] {oldWp.GetType().Name} 的 config 为空 ✗ → WeaponProxy 的 config 也留空 ✓（武器将没有数值 ✓）");

                    SetPrivateField(proxy, "firePoint", GetPrivateField(oldWp, "firePoint"));

                    UnityEngine.Object.DestroyImmediate(oldWp, true);
                    didSomething = true;
                    Debug.Log($"[Migrate] player.prefab：{oldWp.GetType().Name} ✗ → WeaponProxy ✓（config={(wpCfg != null ? wpCfg.name : "空")} ✓）");
                }

                if (didSomething)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log("[Migrate] player.prefab 迁移完成 ✓");
                }
                return didSomething;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ==================== 工具 ====================

        /// <summary>
        /// 按【字段名 + 类型】把 from 的序列化字段搬到 to ✓。
        /// 搬不动（to 上没有同名字段 / 类型不同 ✓）的会收集到 skipped ✗ 并打印 ✓。
        /// 为什么用反射而不是 SerializedObject ✗：SerializedProperty 没有逐属性复制 ✗，
        /// 手写类型分发容易漏（数组/枚举/引用 ✗）；而两边字段是我们自己定义的 ✓ 反射最直接 ✓
        /// </summary>
        private static int CopyFieldsByName(object from, object to, out List<string> skipped)
        {
            skipped = new List<string>();
            int copied = 0;
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var dstType = to.GetType();
            foreach (var f in from.GetType().GetFields(All))
            {
                if (f.IsStatic || f.IsInitOnly) continue;

                var d = dstType.GetField(f.Name, All);
                if (d == null) { skipped.Add($"{f.Name}（AOT 侧无同名字段 ✓）"); continue; }
                if (d.FieldType != f.FieldType)
                { skipped.Add($"{f.Name}（类型不一致：{f.FieldType.Name} ✗ vs {d.FieldType.Name} ✓）"); continue; }

                d.SetValue(to, f.GetValue(from));
                copied++;
            }
            return copied;
        }

        private static void LogCopy(string what, string who, int copied, List<string> skipped)
        {
            string skipText = skipped.Count == 0 ? "无 ✓" : string.Join(" / ", skipped);
            Debug.Log($"[Migrate] {who}：{what} 搬运 {copied} 个字段 ✓；未搬：{skipText}");
        }

        private static object GetPrivateField(object obj, string name)
        {
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return obj.GetType().GetField(name, All)?.GetValue(obj);
        }

        private static void SetPrivateField(object obj, string name, object value)
        {
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var f = obj.GetType().GetField(name, All);
            if (f == null)
            {
                Debug.LogError($"[Migrate] {obj.GetType().Name} 上找不到字段「{name}」✗ —— 代理组件改过名？迁移不完整 ✓");
                return;
            }
            f.SetValue(obj, value);
            EditorUtility.SetDirty(obj as UnityEngine.Object);
        }
    }
}
