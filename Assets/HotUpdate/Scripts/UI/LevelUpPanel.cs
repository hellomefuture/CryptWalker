using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using CryptWalker.HotUpdate.Data;

namespace CryptWalker.HotUpdate.UI
{
    /// <summary>
    /// 升级三选一面板（策划案 §4.1）。
    /// 每级升级后弹窗，玩家从 3 个 Buff 中选 1 个。
    /// </summary>
    public class LevelUpPanel
    {
        private static GameObject _instance;

        public static void Show(Transform parent, List<BuffData> candidates, System.Action<BuffData> onPick)
        {
            if (_instance != null) return;
            _instance = UIFactory.CreatePanel(parent, "LevelUpPanel");
            UIFactory.SetBackgroundColor(_instance, new Color(0f, 0f, 0f, 0.7f));   // 半透明遮罩

            UIFactory.CreateText(_instance.transform, "Title", "升级！选一个 Buff", 60, new Vector2(0, 300));

            // 3 个 Buff 按钮（横向排列）
            for (int i = 0; i < candidates.Count && i < 3; i++)
            {
                var data = candidates[i];
                var btn = UIFactory.CreateButton(_instance.transform, $"BuffBtn_{i}", $"{data.displayName}\n({data.rarity})\n{data.description}", new Vector2((i - 1) * 350, 0), 320, 200);
                int captured = i;
                btn.onClick.AddListener(() =>
                {
                    Core.Log.Info($"[UI] 选择 Buff: {candidates[captured].displayName}");
                    onPick?.Invoke(candidates[captured]);
                    Close();
                });
            }

            Core.Log.Info("[UI] LevelUpPanel 已显示");
        }

        public static void Close()
        {
            if (_instance != null) Object.Destroy(_instance);
            _instance = null;
        }
    }
}