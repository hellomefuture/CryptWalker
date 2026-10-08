using UnityEngine;
using UnityEngine.UI;

namespace CryptWalker.HotUpdate.UI
{
    /// <summary>
    /// HUD 面板（策划案 §7 局内 HUD）。
    /// 血条 + 经验条 + 武器栏 + 遗物栏（金币/小地图后续补）。
    /// 事件驱动 + 组件式 Binder 模式（策划案 §7 性能绑定）。
    /// </summary>
    public class HUDPanel
    {
        private static GameObject _instance;
        private static Image _hpFill;
        private static Image _expFill;
        private static Text _levelText;
        private static Text _weaponText;
        private static Text _relicText;

        public static void Show(Transform parent)
        {
            if (_instance != null) return;
            _instance = UIFactory.CreatePanel(parent, "HUDPanel");

            // 左上：HP 条
            _hpFill = UIFactory.CreateProgressBar(_instance.transform, "HPBar", new Vector2(-800, 470), 300, 30, Color.red, new Color(0.3f, 0f, 0f));

            // 左上 EXP 条
            _expFill = UIFactory.CreateProgressBar(_instance.transform, "EXPBar", new Vector2(-800, 430), 300, 20, Color.yellow, new Color(0.3f, 0.3f, 0f));

            // 右上 等级文字
            _levelText = UIFactory.CreateText(_instance.transform, "LevelText", "Lv.1", 36, new Vector2(800, 470), TextAnchor.UpperRight);

            // 右上 武器栏
            _weaponText = UIFactory.CreateText(_instance.transform, "WeaponText", "武器：长剑 Lv.1", 28, new Vector2(800, 420), TextAnchor.UpperRight);

            // 右下 遗物栏
            _relicText = UIFactory.CreateText(_instance.transform, "RelicText", "遗物：0", 24, new Vector2(800, -470), TextAnchor.UpperRight);

            Core.Log.Info("[UI] HUDPanel 已显示");
        }

        public static void SetHP(float pct) { if (_hpFill != null) _hpFill.fillAmount = Mathf.Clamp01(pct); }
        public static void SetEXP(float pct) { if (_expFill != null) _expFill.fillAmount = Mathf.Clamp01(pct); }
        public static void SetLevel(int lv) { if (_levelText != null) _levelText.text = $"Lv.{lv}"; }
        public static void SetWeapon(string text) { if (_weaponText != null) _weaponText.text = text; }
        public static void SetRelics(int count) { if (_relicText != null) _relicText.text = $"遗物：{count}"; }

        public static void Close()
        {
            if (_instance != null) Object.Destroy(_instance);
            _instance = null;
            _hpFill = _expFill = null;
            _levelText = _weaponText = _relicText = null;
        }
    }
}