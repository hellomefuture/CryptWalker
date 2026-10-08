using CryptWalker.HotUpdate.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CryptWalker.HotUpdate.UI
{
    /// <summary>
    /// 主菜单面板 —— View 层「Binder」（AGENTS.md §5.3 按钮回调用 EventBus 解耦）。
    ///
    /// 职责边界（与旧版最大的区别）：
    ///   - ❌ 不再自己 Instantiate prefab（交给 UIManager，含异步加载）
    ///   - ❌ 不再持有面板生命周期（_instance / Close）
    ///   - ✅ 只做两件事：按命名约定找到按钮控件 + 把点击转成 EventBus 事件
    ///
    /// 为什么这样拆：
    ///   View 不知道"谁来处理点击"（不持有 GameFlowMachine 引用），Controller 也不知道"按钮叫什么名字"。
    ///   两边只通过 GameEvent 枚举耦合 → 面板可独立替换/复用到别的场景。
    ///
    /// prefab 子物体命名约定：btn_Begin / btn_Setting / btn_Quit
    /// </summary>
    public static class MainMenuPanel
    {
        /// <summary>面板 ID（与 UIPanelConfig.panelId 一致）</summary>
        public const string PanelId = "MainMenu";

        /// <summary>由 UIManager 实例化 prefab 后调用：查控件 + 绑按钮事件</summary>
        public static void Bind(GameObject panel)
        {
            if (panel == null)
            {
                Log.Error("[MainMenuPanel] Bind 收到 null 面板，跳过绑定");
                return;
            }

            BindButton(panel.transform, "btn_Begin",   GameEvent.MainMenuStartClicked);
            BindButton(panel.transform, "btn_Setting", GameEvent.MainMenuSettingClicked);
            BindButton(panel.transform, "btn_Quit",    GameEvent.MainMenuQuitClicked);

            Log.Info("[UI] MainMenuPanel 按钮已绑定（点击 → EventBus）");
        }

        /// <summary>找到按钮 → 清旧监听（防重复绑定）→ 绑"raise 事件"</summary>
        private static void BindButton(Transform root, string name, GameEvent evt)
        {
            var btn = FindButton(root, name);
            if (btn == null)
            {
                Log.Warn($"[MainMenuPanel] prefab 找不到按钮 '{name}'（检查子物体命名规范）");
                return;
            }
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => EventBus.Raise(evt));
        }

        /// <summary>递归查找指定名字的 Button（找不到返回 null，不抛异常）</summary>
        private static Button FindButton(Transform parent, string name)
        {
            var direct = parent.Find(name);
            if (direct != null)
            {
                var btn = direct.GetComponent<Button>();
                if (btn != null) return btn;
            }
            foreach (var b in parent.GetComponentsInChildren<Button>(true))
                if (b.gameObject.name == name) return b;
            return null;
        }
    }
}
