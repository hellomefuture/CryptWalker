using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CryptWalker.Main.UI
{
    /// <summary>
    /// 加载界面（**AOT 侧**）。
    ///
    /// ===== 为什么必须在 AOT 侧 =====
    /// 热更 dll 是运行时才加载的。加载界面如果放在热更层，就会出现
    /// "要显示的加载界面自己还没被加载出来"的循环 —— 所以它必须是 AOT 组件，
    /// 并且用【它自己的 Canvas】，不依赖热更侧 UIService 的四层 Canvas
    /// （那套也要等热更 dll 起来才有）。
    ///
    /// ===== 面板 =====
    /// 用美术给的 UI_Win_Loading.prefab。它里面只有 Image + TMP，
    /// **没有挂任何热更脚本** —— 这是它能被 AOT 用的前提：prefab 上若挂了热更脚本，
    /// 打出的包里脚本引用会失效（组件变 missing），而 Unity 内置 UI 组件永远绑得上。
    ///
    /// 结构（按名字找控件）：
    ///   UI_Win_Loading
    ///   ├─ Img_bakeground   背景
    ///   ├─ Img_blue         进度条填充 ← 注意不是 Filled，靠改宽度驱动
    ///   ├─ text_percentage  百分比文字
    ///   └─ text_Prompt      轮播提示
    ///
    /// ===== 时序（三条硬规则，都是"别一闪而过"）=====
    ///   1) 进度**必须走到 100%** 才允许淡出 —— 真实进度没到就补满（fillToFullSeconds 秒内补完）
    ///   2) 至少显示 minVisibleSeconds 秒（哪怕活儿一帧就干完了）
    ///   3) 到 100% 后再停 holdAtFullSeconds 秒
    ///   满足这三条，才做【手动 GC + 淡出】—— 把 GC 抖动藏在玩家看不到的时候。
    ///
    /// ⚠️ 以上三条只管【什么时候消失】。【出现】是瞬时的（不淡入，见 SetVisible）：
    ///    调用方是"先 Show、紧接着开始画地牢"，淡入会让这 0.35 秒变成
    ///    "隔着半透明遮罩看地牢长出来" ✗
    ///
    /// ===== 用法 =====
    ///   场景里放一个空物体 → 挂上本组件 → 把 UI_Win_Loading.prefab 拖到 panelPrefab
    ///   代码：LoadingScreen.Show() / SetProgress(0~1) / Hide()
    ///   热更侧不直接碰本类，统一走 IAppBridge（保持分层）。
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        public static LoadingScreen Instance { get; private set; }

        [Header("面板")]
        [Tooltip("UI_Win_Loading.prefab —— 只允许含 Unity 内置 UI 组件，不要挂热更脚本")]
        [SerializeField] private GameObject panelPrefab;

        [Header("提示文案")]
        [Tooltip("加载时轮播的提示（少于 2 条就不轮播）")]
        [SerializeField] private string[] tips =
        {
            "点燃火把，地牢正在生成…",
            "按住 Shift 冲刺会消耗体力",
            "空格闪避，闪避瞬间无敌",
            "近战武器消耗体力；远程与法术额外消耗魔法",
            "清空房间后，通往下一间的门会打开",
        };

        [Tooltip("提示多久换一条（秒）")]
        [SerializeField] private float tipInterval = 3f;

        [Header("时序（防一闪而过）")]
        [Tooltip("至少显示多久（秒）。哪怕活儿一瞬间就干完，也要让玩家看清这个界面")]
        [SerializeField] private float minVisibleSeconds = 1.5f;

        [Tooltip("真实进度没到 100% 时，补满到 100% 用多久（秒）")]
        [SerializeField] private float fillToFullSeconds = 0.4f;

        [Tooltip("到达 100% 后还要停留多久才淡出（秒）")]
        [SerializeField] private float holdAtFullSeconds = 0.4f;

        [Tooltip("淡出时长（秒）")]
        [SerializeField] private float fadeDuration = 0.35f;

        [Header("性能")]
        [Tooltip("开始淡出前做一次手动 GC（托管堆）—— 把 GC 抖动藏在加载界面期间")]
        [SerializeField] private bool collectGarbageOnHide = true;

        [Tooltip("顺带调用 Resources.UnloadUnusedAssets()（卸载 Unity 侧的未引用资源）。\n" +
                 "注意：它会卸掉【没有任何强引用】的资源 —— 如果战斗中用到的贴图恰好只被热更侧短暂引用过，\n" +
                 "就会被卸掉然后每帧重新加载，反而卡得厉害。所以默认关闭：先只做托管 GC，\n" +
                 "真需要压内存时再打开，并确认打开后没有变卡。")]
        [SerializeField] private bool unloadUnusedAssetsOnHide = false;

        [Header("表现")]
        [Tooltip("Canvas 排序值，要大过所有游戏内 UI")]
        [SerializeField] private int canvasSortOrder = 30000;

        // —— 运行时 ——
        private GameObject _canvasGo;     // 加载界面自己的 Canvas（隐藏时整个关掉）
        private int _lastPct = -1;        // 上次画过的百分比（避免每帧重复赋值）
        private float _lastBarWidth = -1f;// 上次画过的进度条宽度（同上）
        private CanvasGroup _group;
        private RectTransform _bar;
        private TextMeshProUGUI _percentText;
        private TextMeshProUGUI _tipText;
        private float _barFullWidth;      // 进度条满宽（初始化时记录）
        private Vector2 _barPivot;        // 进度条 pivot（用于锁定左边缘）
        private float _barLeftEdgeX;      // 进度条左边缘在父级里的 X（锁边用的基准）
        private float _shownAt;           // 本次显示的开始时刻（unscaled）

        private float _progress;          // 当前【显示】的进度
        private float _progressTarget;    // 目标进度（真实值）
        private float _fullAt = -1f;      // 首次到达 100% 的时刻（-1 = 还没到）
        private bool _hideRequested;
        private float _fadeOutAt = -1f;   // 开始淡出的时刻（-1 = 还没开始）

        private float _tipTimer;
        private int _tipIndex;
        private bool _visible;
        private float _targetAlpha;

        private static bool _warnedMissing;

        private void Awake()
        {
            Instance = this;
            Build();

            if (_group != null)
            {
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
            }
            _visible = false;
            _targetAlpha = 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>建 Canvas + 实例化面板 + 找控件（只做一次）</summary>
        private void Build()
        {
            if (panelPrefab == null)
            {
                Debug.LogError("[Loading] panelPrefab 没拖 —— 加载界面不可用。" +
                               "请在场景里找到挂 LoadingScreen 的物体，把 UI_Win_Loading.prefab 拖进 panelPrefab");
                return;
            }

            // 自己建一个最高层 Overlay Canvas（热更侧的 UIService 这时还没起来）
            var canvasGo = new GameObject("LoadingCanvas (runtime)",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvasGo = canvasGo;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = canvasSortOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // ⚠️ 参考分辨率必须与游戏 UI（UIService）一致 —— 两边都取 UIDesign 这一份权威值。
            //
            // 原来这里写的是 1920×1080，而美术稿是 4K（3840×2160）→
            //   scaleFactor 被算大 2 倍 → Img_bakeground（设计宽 2500）及其子物体全部放大 2 倍。
            //   在 1920 宽的屏上，2500 设计像素 × 1 = 2500 物理像素 → 直接超出屏幕右侧 ✗
            // 游戏 UI 那边早就因为同一个原因改成 4K 了（UIService 里有注释），
            // 本类是 AOT 侧另一套建 Canvas 的代码，漏改了 ✗ —— 所以现在收敛到 UIDesign，不再各写各的。
            scaler.referenceResolution = UIDesign.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = UIDesign.MatchWidthOrHeight;

            _group = canvasGo.AddComponent<CanvasGroup>();

            // ⚠️ 第三个参数 worldPositionStays 必须传 false（两个参数的重载默认是 true ✗）。
            //    传 true 时 Unity 会"保持世界变换不变"，于是给面板一个补偿用的
            //    localScale = 1 / Canvas.scaleFactor —— 而 UI 恰恰是【要跟着 Canvas 缩放走】的。
            //    结果面板大小变成"设计单位直接当物理像素"：不随分辨率变化，
            //    在高分辨率屏上偏小、在低分辨率屏上撑破屏幕 —— 和参考分辨率写错是同一类症状，
            //    两个毛病叠在一起更难查。UI 一定用 Instantiate(prefab, parent, false)。
            GameObject panel = Instantiate(panelPrefab, canvasGo.transform, false);
            panel.name = "UI_Win_Loading";

            // 铺满屏幕（美术的面板原本按某个分辨率摆，这里统一拉伸到全屏）
            if (panel.transform is RectTransform rt)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            // 按名字【递归】找控件。
            // ⚠️ 不能用 transform.Find —— 它只找直接子物体，
            //    而美术的面板里这几个控件是分层嵌套的（实测 text_Prompt 在外层，
            //    Img_blue / text_percentage 嵌在下一层），直接用 Find 会漏掉。
            _bar = FindDeep(panel.transform, "Img_blue") as RectTransform;
            _percentText = FindText(panel.transform, "text_percentage");
            _tipText = FindText(panel.transform, "text_Prompt");

            // 兜底：名字被改过时按关键字猜一个。总比整个加载界面不工作强 —— 但会打日志说明用了兜底，
            // 免得"猜中了但猜错了对象"变成下一个谜题。
            if (_bar == null)
            {
                _bar = FindDeepByKeyword(panel.transform, "blue") as RectTransform;
                if (_bar != null) Debug.Log($"[Loading] 没找到 \"Img_blue\"，按关键字 \"blue\" 兜底到：{_bar.name}");
            }
            if (_percentText == null)
            {
                _percentText = FindTextByKeyword(panel.transform, "percent", "pct");
                if (_percentText != null) Debug.Log($"[Loading] 没找到 \"text_percentage\"，兜底到：{_percentText.name}");
            }
            if (_tipText == null)
            {
                _tipText = FindTextByKeyword(panel.transform, "prompt", "tip");
                if (_tipText != null) Debug.Log($"[Loading] 没找到 \"text_Prompt\"，兜底到：{_tipText.name}");
            }

            if (_bar != null)
            {
                _barFullWidth = _bar.sizeDelta.x;
                _barPivot = _bar.pivot;

                // 记住"左边缘"在父级里的位置。
                // 之后每次改宽度都会按它反算 anchoredPosition.x ——
                // 这样不管美术把 pivot 设成左(0)/中(0.5)/右(1)，
                // 进度条都【恒从左边缘往右长】，不需要手动去调 pivot。
                _barLeftEdgeX = _bar.anchoredPosition.x - _bar.pivot.x * _bar.sizeDelta.x;

                if (Mathf.Abs(_bar.pivot.x) > 0.01f)
                    Debug.Log($"[Loading] Img_blue 的 pivot.x={_bar.pivot.x:F2}（不是 0）——" +
                              "代码按左边缘锁定驱动，方向依然正确，无需手动改");
            }

            if (_bar == null || _percentText == null || _tipText == null)
                Debug.LogWarning($"[Loading] 面板控件没找全：Img_blue={_bar != null}、" +
                                 $"text_percentage={_percentText != null}、text_Prompt={_tipText != null}。" +
                                 "下面是面板的真实层级 —— 照它改名字，或改 LoadingScreen 里的查找名：\n" +
                                 DumpHierarchy(panel.transform));
        }

        /// <summary>按名字【递归】查子物体（transform.Find 只查一层，不够用）</summary>
        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static TextMeshProUGUI FindText(Transform root, string name)
            => FindDeep(root, name)?.GetComponent<TextMeshProUGUI>();

        /// <summary>按名字关键字模糊找（美术改了名字时的兜底）</summary>
        private static Transform FindDeepByKeyword(Transform root, params string[] keywords)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                string lower = child.name.ToLowerInvariant();
                for (int k = 0; k < keywords.Length; k++)
                    if (lower.Contains(keywords[k])) return child;

                var hit = FindDeepByKeyword(child, keywords);
                if (hit != null) return hit;
            }
            return null;
        }

        private static TextMeshProUGUI FindTextByKeyword(Transform root, params string[] keywords)
            => FindDeepByKeyword(root, keywords)?.GetComponent<TextMeshProUGUI>();

        /// <summary>打印面板层级（控件没找到时用来一眼看清真实结构）</summary>
        private static string DumpHierarchy(Transform root)
        {
            var sb = new System.Text.StringBuilder(512);
            DumpRecursive(root, 0, sb);
            return sb.ToString();
        }

        private static void DumpRecursive(Transform t, int depth, System.Text.StringBuilder sb)
        {
            sb.Append(' ', depth * 2).Append("- ").Append(t.name);
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null) sb.Append("   (TMP:\"").Append(tmp.text).Append("\")");
            sb.Append('\n');
            for (int i = 0; i < t.childCount; i++) DumpRecursive(t.GetChild(i), depth + 1, sb);
        }

        /// <summary>把当前显示的进度画到面板上（每帧调，但只在数值真的变了才写 UI）</summary>
        private void RenderProgress()
        {
            // ⚠️ 性能关键：UGUI 里"赋同样的值"也会把元素标脏 → 触发 Canvas 重建。
            //    而 $"{...}%" 每帧还会分配一个字符串（GC 垃圾）。
            //    所以这里必须做变化检测：只有百分比整数变了才写文本，
            //    只有宽度变了才改 RectTransform —— 否则一个隐藏的加载界面每帧都在拖慢游戏。
            int pct = Mathf.RoundToInt(_progress * 100f);
            if (_percentText != null && pct != _lastPct)
            {
                _lastPct = pct;
                _percentText.text = pct + "%";
            }

            // Img_blue 的 Image Type 是 Sliced（不是 Filled），所以不能用 fillAmount，靠【改宽度】驱动。
            // ⚠️ 前提：它的锚点没被设成 stretch —— stretch 时 sizeDelta 是"偏移量"而不是宽度，
            //    那种情况要在 Inspector 里把锚点改成左对齐（或改用 localScale）。
            if (_bar != null)
            {
                float targetWidth = _barFullWidth * _progress;
                if (!Mathf.Approximately(targetWidth, _lastBarWidth))
                {
                    _lastBarWidth = targetWidth;

                    Vector2 size = _bar.sizeDelta;
                    size.x = targetWidth;
                    _bar.sizeDelta = size;

                    // 锁左边缘：pivot 不在 0 时，单纯改宽度会让它"从中间往两边"或"从右往左"长。
                    // 用 anchoredPosition 把左边缘按回去，方向就与 pivot 无关了。
                    Vector2 pos = _bar.anchoredPosition;
                    pos.x = _barLeftEdgeX + _barPivot.x * size.x;
                    _bar.anchoredPosition = pos;
                }
            }
        }

        /// <summary>
        /// 传入显示用的初始进度并确保界面可见。
        ///
        /// 旧实现是自己写 _progressTarget + 单独设一次提示文案，与 SetVisible 里的逻辑重复，
        /// 而且这条"设文案"根本没有承接后续轮播（_tipTimer/_tipIndex 都没动）——
        /// 表现就是提示文字一直停在美术的占位文字上。现在统一转发到 SetVisible：
        /// 进度与提示文案只有【一条】代码路径，不会再出现"看起来设了其实没设"的情况。
        /// </summary>
        public void Apply(float progress) => SetVisible(true, progress);

        /// <summary>
        /// 显示 / 请求隐藏。
        /// 注意：visible=false 只是【请求】隐藏 —— 真正的淡出要等三条时序规则满足（见类注释），
        /// 所以调用方不需要自己等，Hide() 之后界面会自己走完 100% 再消失。
        /// </summary>
        public void SetVisible(bool visible, float progress = -1f)
        {
            if (_group == null) return;

            if (progress >= 0f) _progressTarget = Mathf.Clamp01(progress);

            if (visible)
            {
                // 从"完全隐藏（Canvas 已关）"状态被唤醒：先把 Canvas 打开
                if (_canvasGo != null && !_canvasGo.activeSelf) _canvasGo.SetActive(true);

                if (!_visible)
                {
                    _shownAt = Time.unscaledTime;
                    _fullAt = -1f;
                    _tipTimer = 0f;

                    // ⚠️ 一显示就先写一条真实提示 —— 不能等轮播。
                    //    prefab 里 text_Prompt 的原内容是美术留的占位文字「提示词」（三个字）✗
                    //    而提示轮播要等 tipInterval（默认 3 秒）才第一次写入 ✗，
                    //    可一次加载总共只显示 2 秒左右 → 玩家【永远只看到「提示词」这三个占位字】。
                    //    所以在这里立刻覆盖一次；"先显示当前这条、再后移下标"的写法，
                    //    与 Update 里的轮播保持一致，接下去不会跳条。
                    if (_tipText != null && tips != null && tips.Length > 0)
                    {
                        _tipText.text = tips[_tipIndex % tips.Length];
                        _tipIndex = (_tipIndex + 1) % tips.Length;
                    }
                }
                _hideRequested = false;
                _fadeOutAt = -1f;
                _visible = true;
                _targetAlpha = 1f;

                // ⚠️【显示必须瞬时，不能淡入】—— 直接赋值，不靠 Update 插值。
                //
                // 原因：调用方（GameFlowMachine.GameState）是"先 ShowLoading、紧接着才开始画地牢"，
                //       所以"能看见游戏场景"只有一个可能 —— 加载界面当时是透明的。
                //
                // 如果只设 _targetAlpha 让 Update 淡入（0 ~ 1 要 fadeDuration=0.35s，约 21 帧）：
                //   · 这 21 帧里地牢正在【分帧生成】→ 玩家等于隔着半透明遮罩看房间一间间长出来
                //     （表现就是"点了开始游戏，先看到游戏场景，然后 loading 才盖上来"）
                //   · 更糟的是 LoadingScreen.Update 可能在本帧【晚于】调用方执行 →
                //     那一帧的渲染就是 alpha=0（完全透明），必然漏出游戏画面
                //
                // 淡出保留（fadeDuration）：淡出是"从加载画面平滑回到游戏"，那才有意义。
                if (_group.alpha < 1f) _group.alpha = 1f;
                _targetAlpha = 1f;

                // 加载时挡住点击：防止玩家连点"进入战斗"之类的按钮
                _group.blocksRaycasts = true;
            }
            else
            {
                // 关键：请求隐藏时把目标进度【顶到 100%】——
                // 真实活儿提前干完了（比如只到 40%），也要让进度条补满再走，
                // 不然玩家会觉得"没加载完就闪进去了"。
                _progressTarget = 1f;
                _hideRequested = true;
            }
        }

        private void Update()
        {
            if (_group == null) return;

            // 完全隐藏后：把 Canvas 整个关掉并直接返回。
            // ⚠️ 这一步是性能关键 —— 否则加载界面虽然看不见，UGUI 每帧仍然要处理它，
            //    下面的 RenderProgress 也会每帧跑（分配字符串 + 可能把元素标脏触发 Canvas 重建），
            //    结果就是"加载界面用完了，游戏却一直卡"。
            if (!_visible && _fadeOutAt < 0f)
            {
                if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
                return;
            }

            // unscaledDeltaTime：即使某处把 timeScale 设成 0（暂停），加载界面也要正常走动
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            // 1) 显示进度朝目标推进
            if (_progress < _progressTarget)
                _progress = Mathf.MoveTowards(_progress, _progressTarget, dt / Mathf.Max(0.01f, fillToFullSeconds));
            RenderProgress();

            // 2) 首次到 100% 记时刻（用于 holdAtFullSeconds）
            if (_fullAt < 0f && _progress >= 0.999f) _fullAt = now;

            // 3) 淡出（淡入不在这里 —— 显示时已在 SetVisible 里瞬时置成不透明，见那里的说明）
            _group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha, dt / Mathf.Max(0.01f, fadeDuration));

            // 4) 提示轮播（只在显示时走）
            if (_visible && _tipText != null && tips != null && tips.Length > 1)
            {
                _tipTimer += dt;
                if (_tipTimer >= tipInterval)
                {
                    _tipTimer = 0f;
                    // 与 SetVisible 里"显示时立刻写一条"用的是同一套顺序：
                    // 先显示 _tipIndex 这条，再把下标后移。两边一致 → 显示的那条和轮播的后续正好接上，不跳条。
                    _tipText.text = tips[_tipIndex % tips.Length];
                    _tipIndex = (_tipIndex + 1) % tips.Length;
                }
            }

            // 5) 收到隐藏请求后，三条时序规则满足才开始淡出
            if (_hideRequested && _fadeOutAt < 0f
                && now - _shownAt >= minVisibleSeconds                 // ① 至少显示够久
                && _fullAt >= 0f && now - _fullAt >= holdAtFullSeconds) // ② 进度已满并停够时间
            {
                _fadeOutAt = now;
                _targetAlpha = 0f;
                _group.blocksRaycasts = false;

                // ③ 手动 GC：放在这里，玩家看到的是加载画面，抖动量藏在画面里
                if (collectGarbageOnHide) CollectGarbage(unloadUnusedAssetsOnHide);
            }

            // 6) 淡出结束才算隐藏完成 —— 立刻关掉 Canvas，不等下一帧
            if (_fadeOutAt >= 0f && _group.alpha <= 0.001f)
            {
                _visible = false;
                _fadeOutAt = -1f;
                _progress = 0f;
                _progressTarget = 0f;
                _lastPct = -1;
                _lastBarWidth = -1f;
                if (_canvasGo != null) _canvasGo.SetActive(false);
            }
        }

        /// <summary>
        /// 手动 GC + 卸载无引用资源。
        ///
        /// 为什么放在加载界面里：GC 一定会发生，问题只是"什么时候抖"。
        /// 与其让它随机落在战斗中途造成掉帧，不如在加载画面盖着的时候主动收干净。
        /// </summary>
        private static void CollectGarbage(bool unloadAssets)
        {
            long before = System.GC.GetTotalMemory(false);

            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();   // 把终结器队列里的对象也清掉
            System.GC.Collect();

            long after = System.GC.GetTotalMemory(true);

            // Unity 侧的未引用资源（贴图/网格/AudioClip…）。
            // ⚠️ 默认不调用：它会卸掉"没有任何强引用"的资源 —— 万一战斗里要用的贴图
            //    恰好只被热更侧短暂引用过，就会被卸掉、然后每帧重新加载，比不卸载更卡。
            //    要压内存时再打开，并确认打开后没有变卡。
            if (unloadAssets) Resources.UnloadUnusedAssets();

            Debug.Log($"[Loading] 手动 GC{(unloadAssets ? " + UnloadUnusedAssets" : "")}：" +
                      $"{before / 1024 / 1024} MB → {after / 1024 / 1024} MB");
        }

        // ==================== 静态入口（给 AOT 其他代码用）====================

        public static void Show(float progress = 0f)
        {
            if (Instance == null) { WarnMissing(); return; }
            Instance.SetVisible(true, progress);
        }

        /// <summary>更新进度（0~1）。会顺带保证界面可见（进度来了就该显示）</summary>
        public static void SetProgress(float progress)
        {
            if (Instance == null) { WarnMissing(); return; }
            Instance.SetVisible(true, progress);
        }

        /// <summary>请求隐藏。真正消失会自动等到"100% + 最少显示时长 + 停留时长"都满足。</summary>
        public static void Hide() => Instance?.SetVisible(false);

        private static void WarnMissing()
        {
            if (_warnedMissing) return;      // 只提醒一次，别刷屏
            _warnedMissing = true;
            Debug.LogWarning("[Loading] 场景里没有 LoadingScreen 实例（或在它 Awake 之前就被调用了）——" +
                             "请确认启动场景里挂了 LoadingScreen 组件并拖了 panelPrefab");
        }
    }
}
