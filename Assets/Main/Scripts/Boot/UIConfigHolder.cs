using UnityEngine;
using CryptWalker.Main.Data;

namespace CryptWalker.Main.Boot
{
    /// <summary>
    /// 场景里的 MonoBehaviour：SerializeField 持有 UIConfig 引用，
    /// Start 时把 SO 注入到 AppBridge，供 HotUpdate 端 UI 面板通过 IAppBridge 拿。
    ///
    /// 使用步骤：
    ///   1. 在场景里建一个 GameObject（如 _Boot），挂本脚本
    ///   2. 在 Inspector 里创建一个 UIConfig.asset（或右键 Create → CryptWalker → UIConfig）
    ///   3. 把 UI prefab 引用从 Assets/Art/Prefabs/UI_prefabs/ 拖到 SO 字段
    ///   4. 把 UIConfig.asset 拖到本脚本的 Config 字段
    ///
    /// 时序说明：用 Start 是为了等 MainLauncher.Start() 创建 AppBridge 后再注入
    /// （与 LevelConfigHolder 模式一致）。
    /// </summary>
    public class UIConfigHolder : MonoBehaviour
    {
        [Tooltip("UI 配置（ScriptableObject）。拖一个 UIConfig.asset 进来。")]
        [SerializeField] private UIConfig config;

        private void Start()
        {
            var bridge = AppBridge.Instance;
            if (bridge != null)
            {
                bridge.SetUIConfig(config);
                if (config == null)
                {
                    Debug.LogWarning("[UIConfigHolder] config 字段为空 — 请在 Inspector 拖一个 UIConfig.asset 进来");
                }
            }
            else
            {
                Debug.LogError("[UIConfigHolder] AppBridge 未初始化——检查 MainLauncher 启动流程");
            }
        }

        private void Reset()
        {
#if UNITY_EDITOR
            if (config == null)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets("t:UIConfig");
                if (guids.Length > 0)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    config = UnityEditor.AssetDatabase.LoadAssetAtPath<UIConfig>(path);
                }
            }
#endif
        }
    }
}