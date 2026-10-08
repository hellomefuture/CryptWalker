using UnityEngine;
using UnityEngine.UI;

namespace CryptWalker.HotUpdate.UI
{
    /// <summary>
    /// UI 元素工厂（简化版：纯代码生成 UI，不依赖 prefab）。
    /// 创建 Panel / 文本 / 按钮 / 进度条等基础元素。
    /// </summary>
    public static class UIFactory
    {
        public static GameObject CreatePanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0);  // 默认透明
            return go;
        }

        public static void SetBackgroundColor(GameObject panel, Color color)
        {
            var img = panel.GetComponent<Image>();
            if (img != null) img.color = color;
        }

        public static Text CreateText(Transform parent, string name, string content, int fontSize, Vector2 anchoredPos, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(800, fontSize * 2f);
            rt.anchoredPosition = anchoredPos;
            var text = go.GetComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = align switch
            {
                TextAnchor.UpperLeft => TextAnchor.UpperLeft,
                TextAnchor.UpperRight => TextAnchor.UpperRight,
                TextAnchor.LowerLeft => TextAnchor.LowerLeft,
                TextAnchor.LowerRight => TextAnchor.LowerRight,
                _ => TextAnchor.MiddleCenter,
            };
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, float width = 240, float height = 80)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = anchoredPos;
            go.GetComponent<Image>().color = new Color(0.2f, 0.3f, 0.5f, 1f);

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.3f, 0.5f, 0.7f, 1f);
            colors.pressedColor = new Color(0.4f, 0.6f, 0.9f, 1f);
            btn.colors = colors;

            // 按钮文字
            var text = CreateText(go.transform, "Text", label, 28, Vector2.zero);
            text.alignment = TextAnchor.MiddleCenter;

            return btn;
        }

        /// <summary>创建进度条（策划案 §7 HP/EXP 条）</summary>
        public static Image CreateProgressBar(Transform parent, string name, Vector2 anchoredPos, float width, float height, Color fillColor, Color bgColor)
        {
            // 背景
            var bgGo = new GameObject(name + "_BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGo.transform.SetParent(parent, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.sizeDelta = new Vector2(width, height);
            bgRt.anchoredPosition = anchoredPos;
            bgGo.GetComponent<Image>().color = bgColor;

            // 填充（用 Image.fillAmount）
            var fillGo = new GameObject(name + "_Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillGo.transform.SetParent(bgGo.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.color = fillColor;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillAmount = 1f;
            return fillImg;
        }
    }
}