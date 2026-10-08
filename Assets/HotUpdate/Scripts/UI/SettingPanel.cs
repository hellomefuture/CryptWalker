using CryptWalker.HotUpdate.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CryptWalker.HotUpdate.UI
{
    /// <summary>
    /// 设置面板 —— View 层「Binder」（AGENTS.md §5.3：按钮回调走 EventBus 解耦）。
    ///
    /// 职责边界（与 MainMenuPanel 一致）：
    ///   - ❌ 不自己 Instantiate prefab、不持有面板生命周期（交给 UIManager）
    ///   - ✅ 按命名约定查控件 → 把交互转成 EventBus 事件 / 纯视觉行为
    ///
    /// prefab 控件命名契约（改了名字要同步改下面的常量，否则 Bind 会逐条打警告）：
    ///   Button_Save            「保存」：应用设置 + 广播 SettingSaveClicked
    ///   Button_Decline         「取消」：回滚本次改动 + 广播 SettingCancelClicked
    ///   Button_Continue        「继续游戏」：应用 + 广播 SettingContinueClicked（关面板，保持当前流程）
    ///   Button_BackHome        「返回主页面」：应用 + 广播 SettingBackHomeClicked
    ///   Button_SureFullScreen  「全屏模式」：选中高亮 + 切全屏
    ///   Button_SureWindowMode  「窗口模式」：选中高亮 + 切窗口
    ///   Slider_Music           音乐音量 0~1
    ///   Slider_Volume          主音量   0~1
    ///   img_Select / img_Select2
    ///                          **每个模式按钮下面各一张**的「选中高光图」（两个按钮下命名不一致，
    ///                          见 ImgSelectPrefix 注释）；切换选中状态时**只控制它的显隐**
    ///                          （它会盖住 img_unSelect / img_unSelect2，代码完全不用碰 unSelect）
    /// </summary>
    public static class SettingPanel
    {
        /// <summary>面板 ID（与 UIPanelConfig.panelId 一致）</summary>
        public const string PanelId = "Setting";

        // ==================== 控件命名契约 ====================
        private const string BtnSave       = "Button_Save";
        private const string BtnDecline    = "Button_Decline";
        private const string BtnContinue   = "Button_Continue";
        private const string BtnBackHome   = "Button_BackHome";
        private const string BtnFullScreen = "Button_SureFullScreen";
        private const string BtnWindowMode = "Button_SureWindowMode";
        private const string SliderMusic   = "Slider_Music";
        private const string SliderVolume  = "Slider_Volume";

        /// <summary>
        /// 「选中高光图」的命名前缀。
        ///
        /// 为什么用前缀而不是精确名字：两个模式按钮下的高光图命名并不一致 ——
        ///   Button_SureFullScreen  → img_Select
        ///   Button_SureWindowMode  → img_Select2
        /// 前缀匹配能同时命中 img_Select / img_Select2；
        /// 而 img_unSelect / img_unSelect2 以 "img_un" 开头，不会被误命中。
        /// </summary>
        private const string ImgSelectPrefix = "img_Select";

        // ==================== 运行时状态 ====================
        // 面板销毁后仍要保留，所以放静态字段。
        // 有了 AudioService 之后这些应该挪进 Service（现在项目里还没有音频系统，见 ApplyAudio 注释）。
        private static float s_musicVolume  = 0.5f;
        private static float s_masterVolume = 0.5f;

        // 打开面板时的快照，供「取消」回滚
        private static float s_musicOnOpen;
        private static float s_masterOnOpen;

        /// <summary>由 UIManager 实例化 prefab 后调用：查控件 + 绑事件 + 同步初始状态</summary>
        public static void Bind(GameObject panel)
        {
            if (panel == null)
            {
                Log.Error("[SettingPanel] Bind 收到 null 面板，跳过绑定");
                return;
            }

            var root = panel.transform;

            // 打开面板时先记快照，「取消」时回滚到这里
            s_musicOnOpen  = s_musicVolume;
            s_masterOnOpen = s_masterVolume;

            // —— 音量滑条：拖到哪就实时应用到哪（取消时再回滚）——
            BindSlider(root, SliderMusic, s_musicVolume, v =>
            {
                s_musicVolume = v;
                ApplyAudio();
                EventBus.Raise(GameEvent.MusicVolumeChanged, v);
            });

            BindSlider(root, SliderVolume, s_masterVolume, v =>
            {
                s_masterVolume = v;
                ApplyAudio();
                EventBus.Raise(GameEvent.MasterVolumeChanged, v);
            });

            // —— 保存：把当前值定下来，然后交给 Controller 关面板 ——
            BindButton(root, BtnSave, () =>
            {
                ApplyAudio();
                EventBus.Raise(GameEvent.SettingSaveClicked);
                Log.Info($"[SettingPanel] 保存设置：音乐={s_musicVolume:F2} 主音量={s_masterVolume:F2}");
            });

            // —— 取消：回滚滑条 + 重新应用，然后交给 Controller 关面板 ——
            BindButton(root, BtnDecline, () =>
            {
                s_musicVolume  = s_musicOnOpen;
                s_masterVolume = s_masterOnOpen;
                ApplyAudio();
                SetSliderValue(root, SliderMusic, s_musicVolume);
                SetSliderValue(root, SliderVolume, s_masterVolume);
                EventBus.Raise(GameEvent.SettingCancelClicked);
                Log.Info("[SettingPanel] 取消设置：已回滚到打开面板时的值");
            });

            // —— 继续游戏 / 返回主页面 ——
            // 两者都"应用当前设置然后关面板"，区别在 Controller 侧：
            //   继续游戏 = 保持当前流程；返回主页面 = 确保回到主菜单状态。
            BindButton(root, BtnContinue, () =>
            {
                ApplyAudio();
                EventBus.Raise(GameEvent.SettingContinueClicked);
            });

            BindButton(root, BtnBackHome, () =>
            {
                ApplyAudio();
                EventBus.Raise(GameEvent.SettingBackHomeClicked);
            });

            // —— 显示模式：只切选中高光图的显隐（+ 真正切屏幕模式）——
            BindButton(root, BtnFullScreen, () => SetDisplayMode(root, true));
            BindButton(root, BtnWindowMode, () => SetDisplayMode(root, false));

            // 初始高亮按【当前真实的屏幕模式】决定，而不是靠记住上次点了谁
            RefreshDisplayModeHighlight(root, Screen.fullScreen);

            Log.Info("[UI] SettingPanel 控件已绑定（点击 → EventBus / 纯视觉）");
        }

        // ==================== 显示模式 ====================

        /// <summary>
        /// 切换显示模式。
        /// 高亮机制：每个模式按钮下各有一张 img_Select，切换时只 SetActive 它
        /// （未选中的按钮上 img_Select 关闭后，下面的 img_unSelect 就露出来了）。
        /// </summary>
        private static void SetDisplayMode(Transform root, bool fullScreen)
        {
            Screen.fullScreen = fullScreen;
            RefreshDisplayModeHighlight(root, fullScreen);
            Log.Info($"[SettingPanel] 显示模式 → {(fullScreen ? "全屏" : "窗口")}");
        }

        /// <summary>按目标状态刷新两个模式按钮的 img_Select 显隐</summary>
        private static void RefreshDisplayModeHighlight(Transform root, bool fullScreen)
        {
            SetModeSelected(root, BtnFullScreen, fullScreen);
            SetModeSelected(root, BtnWindowMode, !fullScreen);
        }

        /// <summary>把某个模式按钮下面的「选中高光图」设为显示/隐藏</summary>
        private static void SetModeSelected(Transform root, string buttonName, bool selected)
        {
            var btn = FindButton(root, buttonName);
            if (btn == null) return;   // FindButton 内部已经打过警告

            // 注意：必须在这个按钮**子树内**找高光图。
            // 两个模式按钮下各有一张高光图（img_Select / img_Select2），
            // 从 panel 根节点递归找会永远命中第一张，导致两个按钮的高亮联动。
            var img = FindSelectedImage(btn.transform);
            if (img == null)
            {
                Log.Warn($"[SettingPanel] '{buttonName}' 下面找不到 '{ImgSelectPrefix}' 开头的高光图（选中态靠它控制）");
                return;
            }
            img.gameObject.SetActive(selected);
        }

        /// <summary>
        /// 在按钮子树内找「选中高光图」：先精确匹配 img_Select，再退化为前缀匹配（命中 img_Select2）。
        /// 前缀 img_Select 不会误命中 img_unSelect / img_unSelect2（它们以 img_un 开头）。
        /// </summary>
        private static Transform FindSelectedImage(Transform buttonRoot)
        {
            var direct = FindDirect(buttonRoot, ImgSelectPrefix);
            if (direct != null) return direct;

            foreach (var t in buttonRoot.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.name.StartsWith(ImgSelectPrefix)) return t;
            return null;
        }

        // ==================== 音量 ====================

        /// <summary>
        /// 应用音量。
        ///
        /// ⚠️ 项目里目前**没有任何音频系统**：全工程搜 AudioSource / AudioListener / AudioMixer
        ///    是 0 命中，Modules.cs 里 AudioManager 也还是注释状态。
        ///    所以这里临时直接作用于 Unity 内置的 AudioListener.volume（主音量立刻生效），
        ///    音乐音量先只存进静态字段 s_musicVolume。
        ///    等 AudioService 落地后，把本方法换成 audioService.SetMusicVolume / SetMasterVolume 即可，
        ///    调用方（Bind 里的回调）不用动。
        /// </summary>
        private static void ApplyAudio()
        {
            AudioListener.volume = Mathf.Clamp01(s_masterVolume);
        }

        // ==================== 查找 / 绑定工具 ====================

        private static void BindButton(Transform root, string name, UnityEngine.Events.UnityAction action)
        {
            var btn = FindButton(root, name);
            if (btn == null) return;   // FindButton 内部已经打过警告

            btn.onClick.RemoveAllListeners();   // 防重复绑定
            btn.onClick.AddListener(action);
        }

        private static void BindSlider(Transform root, string name, float initialValue, UnityEngine.Events.UnityAction<float> onChanged)
        {
            var slider = FindSlider(root, name);
            if (slider == null)
            {
                Log.Warn($"[SettingPanel] prefab 找不到滑条 '{name}'（检查子物体命名）");
                return;
            }

            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.SetValueWithoutNotify(Mathf.Clamp01(initialValue));   // 初始化不触发回调

            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(onChanged);
        }

        private static void SetSliderValue(Transform root, string name, float value)
        {
            var slider = FindSlider(root, name);
            if (slider != null) slider.SetValueWithoutNotify(Mathf.Clamp01(value));
        }

        private static Button FindButton(Transform parent, string name)
        {
            var direct = FindDirect(parent, name);
            if (direct != null)
            {
                var btn = direct.GetComponent<Button>();
                if (btn != null) return btn;
            }
            foreach (var b in parent.GetComponentsInChildren<Button>(true))
                if (b.gameObject.name == name) return b;

            Log.Warn($"[SettingPanel] prefab 找不到按钮 '{name}'（检查子物体命名规范）");
            return null;
        }

        private static Slider FindSlider(Transform parent, string name)
        {
            var direct = FindDirect(parent, name);
            if (direct != null)
            {
                var s = direct.GetComponent<Slider>();
                if (s != null) return s;
            }
            foreach (var s in parent.GetComponentsInChildren<Slider>(true))
                if (s.gameObject.name == name) return s;
            return null;
        }

        /// <summary>只在直接子节点里找（先走这一层，避免全树遍历）</summary>
        private static Transform FindDirect(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.gameObject.name == name) return child;
            }
            return null;
        }
    }
}
