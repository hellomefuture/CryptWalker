  using CryptWalker.HotUpdate.Core;
using System.Collections.Generic;
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
    /// prefab 子物体命名约定：
    ///   按钮    btn_Begin / btn_Setting / btn_Quit
    ///   视差层  Img_Character（角色）/ Img_name（标题）/ 上面三个按钮
    ///           —— 背景不在这张名单里：它是根节点 UI_Win_Main 自身的 Image，属于「背景层」
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

            // 开始界面动态效果：缓存各景深层的位置（每帧由 MainMenuState.OnUpdate 驱动）
            BindParallax(panel);

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

        // ==================== 鼠标视差（开始界面动态效果） ====================

        // 思路：把开始界面按「景深」分层，鼠标一动各层位移不同 → 视差。
        //   1) 背景挂在根节点 UI_Win_Main 自己的 Image 上（StartGameBackGround），
        //      所以「背景层」就是根节点。动根节点会连带整盘 UI 一起走，
        //      因此子层的实际位移 = 自身强度 - 背景强度（Extra），让最终位移正好等于各自强度。
        //   2) 防露边：背景位移多少，就把根节点 rect 放大 2×位移量。
        //      子物体锚点全是 (0.5,0.5) 居中（美术已核对），rect 变大不会移动它们；
        //      背景是 Simple 类型跟着 rect 拉伸 → 无论怎么移都铺满，看不到边。
        //   3) 强度单位是画布设计像素（项目 UI 参考分辨率 3840×2160，见 UIService.CreateRootCanvas）。
        //   4) 每帧由 MainMenuState.OnUpdate 驱动（AGENTS.md §3.2-8：统一 tick，不新增 MonoBehaviour）。

        /// <summary>背景层（= 面板根节点）位移强度，单位设计像素。改成 0 = 背景不动、只让前景浮动</summary>
        private const float BgDepth = 20f;

        /// <summary>跟手方向：+1 = 元素朝鼠标同方向漂移；-1 = 朝反方向（"镜头转动"感）</summary>
        private const float FollowSign = 1f;

        /// <summary>平滑速度：越大越跟手，越小越飘（指数平滑，与帧率无关）</summary>
        private const float FollowSpeed = 6f;

        /// <summary>一层视差的配置：prefab 子物体名 + 位移强度（越"近"的层给越大）</summary>
        private sealed class ParallaxCfg
        {
            public readonly string Name;
            public readonly float  Depth;
            public ParallaxCfg(string name, float depth) { Name = name; Depth = depth; }
        }

        /// <summary>
        /// 景深表。数值是设计像素：UI 稿是 4K（3840×2160），在 1920×1080 屏上 1 设计像素≈0.5 物理像素，
        /// 所以 56 大约等于屏幕上 28 物理像素 —— "看得见但不晕"的量级。
        /// 想加层（比如以后美术把背景挪成子节点叫 img_bg）直接在这里加一行即可。
        /// </summary>
        private static readonly ParallaxCfg[] ParallaxConfig =
        {
            new ParallaxCfg("Img_Character", 56f),   // 最近：角色立绘，动得最多
            new ParallaxCfg("Img_name",      34f),   // 次近：标题
            new ParallaxCfg("btn_Begin",     14f),   // 最远：按钮组（三块同强度，保持行距不变形）
            new ParallaxCfg("btn_Setting",   14f),
            new ParallaxCfg("btn_Quit",      14f),
        };

        /// <summary>一层视差的运行期数据（struct：每帧遍历不产生 GC）</summary>
        private struct ParallaxLayer
        {
            public RectTransform Rect;
            public Vector2       BasePos;   // prefab 里的原始位置，每帧在它基础上加偏移
            public float         Extra;     // = Depth - BgDepth（父节点已经带了背景位移）
        }

        private static RectTransform   s_root;          // 根节点（承载背景图）
        private static Vector2         s_rootBasePos;
        private static Vector2         s_rootBaseSize;
        private static ParallaxLayer[] s_layers;
        private static Vector2         s_mouse;         // 平滑后的鼠标位置（-1~1）

        /// <summary>面板打开时调用：缓存根节点与各景深层（位置只在这里读一次）</summary>
        private static void BindParallax(GameObject panel)
        {
            var root = panel.GetComponent<RectTransform>();
            if (root == null)
            {
                s_root = null;
                s_layers = null;
                Log.Warn("[MainMenuPanel] 面板没有 RectTransform，鼠标视差跳过");
                return;
            }

            s_root         = root;
            s_rootBasePos  = root.anchoredPosition;
            s_rootBaseSize = root.sizeDelta;
            s_mouse        = Vector2.zero;

            var list = new List<ParallaxLayer>(ParallaxConfig.Length);
            foreach (var cfg in ParallaxConfig)
            {
                var rect = FindRect(panel.transform, cfg.Name);
                if (rect == null)
                {
                    // 少一层不影响整体效果，只警告不报错（美术改名字时容易漏配）
                    Log.Warn($"[MainMenuPanel] 视差层找不到 '{cfg.Name}'（这一层不动）");
                    continue;
                }

                list.Add(new ParallaxLayer
                {
                    Rect    = rect,
                    BasePos = rect.anchoredPosition,
                    Extra   = cfg.Depth - BgDepth,
                });
            }
            s_layers = list.ToArray();

            // 背景会位移 → 根节点先放大对应的量，保证任何方向都不露出画面外
            if (BgDepth > 0.01f)
                root.sizeDelta = s_rootBaseSize + new Vector2(BgDepth * 2f, BgDepth * 2f);

            Log.Info($"[MainMenuPanel] 鼠标视差已就绪：背景强度={BgDepth}，前景 {s_layers.Length} 层");
        }

        /// <summary>
        /// 每帧按鼠标位置刷新视差（由 MainMenuState.OnUpdate 调用）。
        /// 无分配：数组下标遍历 + struct 拷贝，不 new、不 foreach、不 LINQ。
        /// </summary>
        public static void TickParallax()
        {
            if (s_root == null || s_layers == null) return;

            // 鼠标归一化到 -1~1：屏幕左下 = (-1,-1)，右上 = (1,1)
            float tx = 0f, ty = 0f;
            if (Screen.width > 0 && Screen.height > 0)
            {
                var m = Input.mousePosition;
                // Clamp：鼠标移出窗口时（Windows 下 mousePosition 可能越界）不让元素飞出界
                tx = Mathf.Clamp(m.x / Screen.width  * 2f - 1f, -1f, 1f);
                ty = Mathf.Clamp(m.y / Screen.height * 2f - 1f, -1f, 1f);
            }

            // 指数平滑：鼠标瞬移 / 从窗口外移回来时，元素是"飘"过去而不是"跳"过去
            float k = 1f - Mathf.Exp(-FollowSpeed * Time.deltaTime);
            s_mouse.x = Mathf.Lerp(s_mouse.x, tx, k);
            s_mouse.y = Mathf.Lerp(s_mouse.y, ty, k);

            // 背景层（根节点）：位移最小，且 rect 已放大过，不会露边
            s_root.anchoredPosition = s_rootBasePos + s_mouse * (BgDepth * FollowSign);

            // 前景层：Extra 已扣掉父节点那段，最终净位移 = 各自配置的强度
            for (int i = 0; i < s_layers.Length; i++)
            {
                var layer = s_layers[i];
                if (layer.Rect == null) continue;   // 面板已销毁（Unity 的 null 重载能识别）
                layer.Rect.anchoredPosition = layer.BasePos + s_mouse * (layer.Extra * FollowSign);
            }
        }

        /// <summary>面板/状态关闭时清引用，避免继续持有已销毁的 Transform</summary>
        public static void ClearParallax()
        {
            s_root   = null;
            s_layers = null;
        }

        /// <summary>查找指定名字的 RectTransform（先查直接子节点，再退化为递归；找不到返回 null）</summary>
        private static RectTransform FindRect(Transform parent, string name)
        {
            var direct = parent.Find(name);
            if (direct != null) return direct as RectTransform;

            foreach (var rt in parent.GetComponentsInChildren<RectTransform>(true))
                if (rt.gameObject.name == name) return rt;
            return null;
        }
    }
}
 