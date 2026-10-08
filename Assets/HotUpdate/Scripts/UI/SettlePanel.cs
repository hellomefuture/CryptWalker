using CryptWalker.HotUpdate.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CryptWalker.HotUpdate.UI
{
    /// <summary>
    /// 结算面板（策划案 §7 结算界面）。
    /// 显示存活时间 + 等级 + 击杀数 + 返回主菜单按钮。
    /// </summary>
    public class SettlePanel
    {
        private static GameObject _instance;

        public static void Show(Transform parent, float surviveSec, int level, int killCount)
        {
            if (_instance != null) return;
            _instance = UIFactory.CreatePanel(parent, "SettlePanel");
            UIFactory.SetBackgroundColor(_instance, new Color(0.05f, 0.05f, 0.1f, 0.95f));

            UIFactory.CreateText(_instance.transform, "Title", "战 报", 80, new Vector2(0, 300));

            // 数据行
            UIFactory.CreateText(_instance.transform, "TimeText", $"存活时间：{surviveSec:F1} 秒", 40, new Vector2(0, 100));
            UIFactory.CreateText(_instance.transform, "LevelText", $"到达等级：Lv.{level}", 40, new Vector2(0, 30));
            UIFactory.CreateText(_instance.transform, "KillText", $"击杀敌人：{killCount}", 40, new Vector2(0, -40));

            // 返回主菜单：关闭结算 → Raise 事件（Controller 侧 SettleState 订阅处理，AGENTS.md §5.3）
            var backBtn = UIFactory.CreateButton(_instance.transform, "BtnBack", "返回主菜单", new Vector2(0, -200), 280, 80);
            backBtn.onClick.AddListener(() =>
            {
                Close();
                EventBus.Raise(GameEvent.SettleBackToMenuClicked);
            });

            Core.Log.Info("[UI] SettlePanel 已显示");
        }

        public static void Close()
        {
            if (_instance != null) Object.Destroy(_instance);
            _instance = null;
        }
    }
}