using UnityEngine;
using CryptWalker.Main.Data;

namespace CryptWalker.Main.Boot
{
    /// <summary>
    /// 场景里的 MonoBehaviour：SerializeField 持有 LevelConfig 引用，
    /// Start 时把 SO 注入到 AppBridge，供 HotUpdate 端 GameFlowMachine 通过 IAppBridge 拿。
    ///
    /// 使用步骤：
    ///   1. 在场景里建一个 GameObject（如 _Boot），挂本脚本
    ///   2. 在 Inspector 里创建一个 LevelConfig.asset（或用 [CreateAssetMenu] 生成）
    ///   3. 把 prefab 引用（playerPrefab/orc1Prefab/room prefabs）从 Assets/Art/Prefabs 拖到 SO 字段
    ///   4. 把 LevelConfig.asset 拖到本脚本的 Config 字段
    ///
    /// 时序说明：用 Start 而非 Awake 是为了等 MainLauncher.Start() 创建 AppBridge 后再注入。
    /// Awake 在所有 Start 之前触发，AppBridge 实例此时还不存在。
    /// </summary>
    public class LevelConfigHolder : MonoBehaviour
    {
        [Tooltip("关卡配置（ScriptableObject）。拖一个 LevelConfig.asset 进来。")]
        [SerializeField] private LevelConfig config;

        private void Start()
        {
            // 注入到 AppBridge，供 HotUpdate 端 IAppBridge.GetLevelConfig() 读取
            var bridge = AppBridge.Instance;
            if (bridge != null)
            {
                bridge.SetLevelConfig(config);
                if (config == null)
                {
                    Debug.LogWarning("[LevelConfigHolder] config 字段为空 — 请在 Inspector 拖一个 LevelConfig.asset 进来");
                }
            }
            else
            {
                Debug.LogError("[LevelConfigHolder] AppBridge 未初始化——检查 MainLauncher 启动流程");
            }
        }

        private void Reset()
        {
            // 仅在编辑器里：自动找到第一个 LevelConfig.asset 引用（如果存在）
#if UNITY_EDITOR
            if (config == null)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets("t:LevelConfig");
                if (guids.Length > 0)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    config = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelConfig>(path);
                }
            }
#endif
        }
    }
}