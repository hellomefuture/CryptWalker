using CryptWalker.HotUpdate.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CryptWalker.HotUpdate.UI
{
    /// <summary>
    /// HUD 面板（策划案 §7 局内 HUD）。
    ///
    /// ===== 用的是美术 prefab，不是代码画的 =====
    ///   ui_prefabs_Character/UI_Win_Character.prefab（整条都是图片，没有 Slider）
    ///     img_red    ← 生命（GameEvent.PlayerDamaged）
    ///     img_green  ← 体力（GameEvent.StaminaChanged）
    ///     img_blue   ← 魔法（GameEvent.ManaChanged）
    ///     img_bakeground1/2/3 ← 三条各自的底槽（是填充图的父节点，代码不碰）
    ///     imgicon1/2/3        ← 三条各自的图标（暂未接）
    ///   ui_prefabs_commom/UI_Coin.prefab（单独注册成 "Coin" 面板，同样挂 HUD 层）
    ///     Img_Coin     ← 金币图标
    ///     （数字）      ← prefab 里没有，运行时补一个；若美术以后做进 prefab，这里会自动复用
    ///
    /// 为什么单独注册成 "Coin" 面板：UIPanelConfig 一个资产只带一个 prefab，
    /// 而美术把金币和角色条拆成了两套 prefab。用面板机制复用同一套加载/挂载/回收流程，
    /// 比为一个小控件另开一条加载通道（改 AOT 的 UIConfig 结构）划算得多。
    ///
    /// 为什么从 Slider 改成 Image（2026-10 美术改版）：
    ///   美术把三条从 UGUI Slider 换成了一整张图 —— 底槽 img_bakeground* 当父节点、
    ///   填充 img_red/img_green/img_blue 当子节点，prefab 里已经没有 Slider 组件了。
    ///   好在 Image.fillAmount 与 Slider.value 语义一样是 0~1，所以 EventBus 的 payload
    ///   约定完全不用动，只换绑定与赋值方式。
    ///   ⚠️ 填充图必须是 Image.Type.Filled + Horizontal + Left 才认 fillAmount，
    ///      美术在 prefab 里漏设也不要紧：SetupFill 会在绑定时就地补上。
    ///
    /// 写法与 MainMenuPanel 对齐（AGENTS.md §5.3）：
    ///   ❌ 旧版自己 new GameObject + UIFactory 画色块 —— 美术 prefab 一个都没被引用
    ///   ❌ 不持有面板生命周期（交给 UIManager）
    ///   ✅ 只做两件事：按命名约定找控件 + 订阅 EventBus 改控件
    /// </summary>
    public static class HUDPanel
    {
        /// <summary>面板 ID（与 UIPanelConfig.panelId 一致）</summary>
        public const string PanelId = "HUD";

        /// <summary>金币控件 ID（同挂 HUD 层，单独一个面板配置）</summary>
        public const string CoinPanelId = "Coin";

        // ==================== 控件命名契约（与美术 prefab 的子物体名一致，改 prefab 要同步改这里）====================
        private const string ImgHp      = "img_red";
        private const string ImgStamina = "img_green";
        private const string ImgMana    = "img_blue";

        private static Image _hp;
        private static Image _stamina;
        private static Image _mana;
        private static Text _coinText;

        private static bool _subscribed;

        /// <summary>开 HUD（由 GameFlowMachine.GameState 调用）</summary>
        public static async System.Threading.Tasks.Task ShowAsync()
        {
            if (UIManager.Instance == null)
            {
                Log.Error("[HUD] UIManager 未初始化，HUD 开不出来");
                return;
            }

            var panel = await UIManager.Instance.OpenPanelAsync(PanelId);
            if (panel == null)
            {
                Log.Error("[HUD] 面板开不出来（检查 UIConfig.panels 里有没有 HUD 配置、prefab 有没有拖上）");
                return;
            }

            Bind(panel);
            await BuildCoinAsync();
        }

        /// <summary>由 UIManager 实例化 prefab 后调用：查控件 + 订阅事件</summary>
        private static void Bind(GameObject panel)
        {
            _hp      = FindImage(panel.transform, ImgHp);
            _stamina = FindImage(panel.transform, ImgStamina);
            _mana    = FindImage(panel.transform, ImgMana);

            // 三条统一成「横向填充」：事件里直接塞占比 0~1 即可，不用管美术怎么画的
            SetupFill(_hp);
            SetupFill(_stamina);
            SetupFill(_mana);

            Subscribe();

            Log.Info($"[HUD] 已绑定美术 prefab：{ImgHp}={(_hp != null ? "✓" : "【找不到 ✗】")} "
                   + $"{ImgStamina}={(_stamina != null ? "✓" : "【找不到 ✗】")} "
                   + $"{ImgMana}={(_mana != null ? "✓" : "【找不到 ✗】")}");
        }

        // ==================== 金币 ====================

        /// <summary>
        /// 金币显示：美术的 UI_Coin 只有图标没有数字，这里在图标右侧补一个文本。
        /// 位置先摆在屏幕左下角（常见位置）；锚点是写死的，按美术规范调下面这几行即可。
        /// </summary>
        private static async System.Threading.Tasks.Task BuildCoinAsync()
        {
            var coin = await UIManager.Instance.OpenPanelAsync(CoinPanelId);
            if (coin == null)
            {
                Log.Warn("[HUD] 金币面板开不出来（检查 UIConfig.panels 里的 Coin 配置）");
                return;
            }

            // UI_Coin 根节点是全屏拉伸的，图标默认落在屏幕正中 → 先把整块挪到左下角
            var rt = coin.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(62.5f, 62.5f);
                rt.anchoredPosition = new Vector2(80f, 80f);
            }

            // 复用 prefab 里已有的 Text（美术以后把数字做进 prefab 时不会重复加）
            _coinText = coin.GetComponentInChildren<Text>(true);
            if (_coinText == null)
            {
                _coinText = UIFactory.CreateText(coin.transform, "CoinText", "0", 40, Vector2.zero, TextAnchor.MiddleLeft);
                var trt = _coinText.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(0f, 0.5f);
                trt.pivot = new Vector2(0f, 0.5f);
                trt.sizeDelta = new Vector2(240f, 50f);
                trt.anchoredPosition = new Vector2(72f, 0f);   // 62.5 宽的图标右边
                _coinText.alignment = TextAnchor.MiddleLeft;
            }

            SetCoin(_coinText != null && int.TryParse(_coinText.text, out var n) ? n : 0);
            Log.Info("[HUD] 金币显示已就绪（数值来源：GameEvent.CoinChanged）");
        }

        // ==================== 事件绑定 ====================

        private static void Subscribe()
        {
            // 先退订再订阅：EventBus 没有 RemoveAll，重复开面板时不清会重复绑定
            if (_subscribed)
            {
                EventBus.Unsubscribe(GameEvent.PlayerDamaged,  OnHp);
                EventBus.Unsubscribe(GameEvent.StaminaChanged, OnStamina);
                EventBus.Unsubscribe(GameEvent.ManaChanged,    OnMana);
                EventBus.Unsubscribe(GameEvent.CoinChanged,    OnCoin);
            }

            EventBus.Subscribe(GameEvent.PlayerDamaged,  OnHp);
            EventBus.Subscribe(GameEvent.StaminaChanged, OnStamina);
            EventBus.Subscribe(GameEvent.ManaChanged,    OnMana);
            EventBus.Subscribe(GameEvent.CoinChanged,    OnCoin);
            _subscribed = true;
        }

        private static void OnHp(object payload)      { SetFill(_hp, payload); }
        private static void OnStamina(object payload) { SetFill(_stamina, payload); }
        private static void OnMana(object payload)    { SetFill(_mana, payload); }
        private static void OnCoin(object payload)    { SetCoin(payload is int n ? n : 0); }

        // ==================== 控件操作 ====================

        private static void SetFill(Image image, object payload)
        {
            if (image == null) return;
            if (payload is float pct) image.fillAmount = Mathf.Clamp01(pct);
        }

        private static void SetCoin(int value)
        {
            if (_coinText != null) _coinText.text = value.ToString();
        }

        /// <summary>
        /// 把填充图就地配成「从左往右的横向填充」。
        /// 为什么在代码里设而不是只靠美术：Image 只有 Filled 类型才认 fillAmount，
        /// prefab 里若是 Simple，fillAmount 完全无效（血条看着永远是满的）——
        /// 这种静默失效最难查，所以绑定时统一兜底一次。
        /// </summary>
        private static void SetupFill(Image image)
        {
            if (image == null) return;
            image.type       = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;   // 从左边开始扣
            image.fillAmount = 1f;
        }

        /// <summary>递归查找指定名字的 Image（找不到返回 null，不抛异常）</summary>
        private static Image FindImage(Transform root, string name)
        {
            var direct = root.Find(name);
            if (direct != null)
            {
                var img = direct.GetComponent<Image>();
                if (img != null) return img;
            }
            foreach (var img in root.GetComponentsInChildren<Image>(true))
                if (img.gameObject.name == name) return img;
            return null;
        }

        /// <summary>关 HUD（含金币控件），并退订事件</summary>
        public static void Close()
        {
            if (_subscribed)
            {
                EventBus.Unsubscribe(GameEvent.PlayerDamaged,  OnHp);
                EventBus.Unsubscribe(GameEvent.StaminaChanged, OnStamina);
                EventBus.Unsubscribe(GameEvent.ManaChanged,    OnMana);
                EventBus.Unsubscribe(GameEvent.CoinChanged,    OnCoin);
                _subscribed = false;
            }

            UIManager.Instance?.ClosePanel(PanelId);
            UIManager.Instance?.ClosePanel(CoinPanelId);

            _hp = _stamina = _mana = null;
            _coinText = null;
        }
    }
}
