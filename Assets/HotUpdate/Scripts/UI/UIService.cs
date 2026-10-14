using CryptWalker.Main.Data;
using CryptWalker.HotUpdate.Core;
using UnityEngine;

namespace CryptWalker.HotUpdate.UI
{
    /// <summary>
    /// UI 服务（策划案 §7 + AGENTS.md 3.4）。
    /// 职责：
    ///   1) 创建 UI 四层根节点（HUD/Normal/Popup/Loading）
    ///   2) 提供 ShowPanel/PanelType/ClosePanel 接口
    ///   3) EventBus 解耦业务事件
    ///
    /// 简化版：本阶段只搭框架 + 4 个基础 Panel（主菜单/HUD/升级/结算）
    /// 后续阶段加 商店/背包/小地图 时复用 ShowPanel 接口。
    /// </summary>
    public class UIService : IModule
    {
        // 层级枚举用 AOT 端的 CryptWalker.Main.Data.UILayer（枚举值 = SortingOrder 0/100/200/300），
        // 与 UIPanelConfig.layer 共用同一个枚举，避免两端各定义一份导致不一致。

        // 四层 Canvas 根节点（运行期建）
        private Canvas _hudCanvas;
        private Canvas _normalCanvas;
        private Canvas _popupCanvas;
        private Canvas _loadingCanvas;

        // 独立 UI 相机（复用场景里手放的 'UI_Camera'，不运行时创建、也不 Destroy）
        private Camera _uiCamera;

        public static UIService Instance { get; private set; }

        // 便捷访问：各层 Canvas transform（外部代码不用记 UILayer 枚举）
        public Transform HUDCanvasTransform     => _hudCanvas != null ? _hudCanvas.transform : null;
        public Transform NormalCanvasTransform  => _normalCanvas != null ? _normalCanvas.transform : null;
        public Transform PopupCanvasTransform   => _popupCanvas != null ? _popupCanvas.transform : null;
        public Transform LoadingCanvasTransform => _loadingCanvas != null ? _loadingCanvas.transform : null;

        public void Init()
        {
            Instance = this;

            // 复用场景里手放的 UI 相机（命名约定："UI_Camera"）
            // 找不到时 warn 并 fallback 到 Camera.main（UI 会渲染但可能分层异常，由策划/美术加相机）
            _uiCamera = FindUICameraInScene();
            if (_uiCamera == null)
            {
                Core.Log.Warn("[UIService] 场景里没找到名为 'UI_Camera' 的相机，fallback 到 Camera.main（请在场景里加一个名为 'UI_Camera' 的独立相机）");
                _uiCamera = Camera.main;
            }

            // 用 ScreenSpaceCamera 模式创建 4 层 Canvas
            _hudCanvas     = CreateRootCanvas(UILayer.HUD, "Canvas_HUD");
            _normalCanvas  = CreateRootCanvas(UILayer.Normal, "Canvas_Normal");
            _popupCanvas   = CreateRootCanvas(UILayer.Popup, "Canvas_Popup");
            _loadingCanvas = CreateRootCanvas(UILayer.Loading, "Canvas_Loading");

            // 修复：不在 Init 阶段自动 ShowMainMenu——菜单由 GameFlowMachine.MainMenuState 显式触发
            // 原因：UIService.Init 在 Modules.InitAll 阶段触发，比 GameFlowMachine 早，
            //       此时没有游戏流程上下文，按钮点击后无法正确切换到 GameState
            Core.Log.Info($"[UIService] 初始化完成，4 层 UI 根节点已建（UI 相机={(_uiCamera != null ? _uiCamera.gameObject.name : "null")}）");
        }

        public void Shutdown()
        {
            // Destroy Canvas（UI 相机是场景手放的，不 Destroy）
            if (_hudCanvas != null) Object.Destroy(_hudCanvas.gameObject);
            if (_normalCanvas != null) Object.Destroy(_normalCanvas.gameObject);
            if (_popupCanvas != null) Object.Destroy(_popupCanvas.gameObject);
            if (_loadingCanvas != null) Object.Destroy(_loadingCanvas.gameObject);
            Instance = null;
        }

        /// <summary>
        /// 在场景里找已存在的 UI 相机（按命名约定 "UI_Camera"）。
        /// UI 相机必须由美术在场景里手放（深度/Culling Mask 等美术可调），
        /// 代码不再运行时创建，避免重复创建导致 UI 渲染两次。
        /// </summary>
        private static Camera FindUICameraInScene()
        {
            var all = Object.FindObjectsOfType<Camera>(true);
            foreach (var c in all)
                if (c.gameObject.name == "UI_Camera") return c;
            return null;
        }

        private Canvas CreateRootCanvas(UILayer layer, string name)
        {
            var go = new GameObject(name);

            // 修复：Canvas 根物体必须设为 UI 层
            // 原因：Screen Space-Camera 模式下，UI 相机剔除的是【Canvas 根物体】的 Layer，
            //       而非各个 Image/TMP 子物体的 Layer。根物体留在 Default 会导致
            //       UI 相机 Culling Mask=UI 时整棵 Canvas 子树被剔除（UI 完全不显示）。
            go.layer = LayerMask.NameToLayer("UI");

            Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();

            // ScreenSpace-Camera 模式 → Canvas 渲染到 UI 相机，而非直接画到屏幕
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _uiCamera;
            canvas.planeDistance = 1f;   // 距 UI 相机的距离（UI 相机前面 1 单位）
            canvas.sortingOrder = (int)layer;

            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // 参考分辨率 / 匹配权重统一取 AOT 侧的 CryptWalker.Main.UI.UIDesign ——
            // 加载界面（AOT 的 LoadingScreen）用的是同一份值。
            //
            // 以前两边各写字面量：这里改成了 4K（若设 1920×1080，3840×2160 的面板会被放大 2 倍，
            // 屏幕只显示中间一块），但 LoadingScreen 漏改，于是加载界面又被放大 2 倍 ✗。
            // 现在只有一个权威值，不会再漂移。
            //
            // 全限定名是故意的：本文件所在命名空间是 CryptWalker.HotUpdate.UI，
            // 直接 using CryptWalker.Main.UI 会让两个 UI 命名空间里的类型名产生歧义风险。
            scaler.referenceResolution = CryptWalker.Main.UI.UIDesign.ReferenceResolution;
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = CryptWalker.Main.UI.UIDesign.MatchWidthOrHeight;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            return canvas;
        }

        public Transform GetLayerRoot(UILayer layer)
        {
            switch (layer)
            {
                case UILayer.HUD: return _hudCanvas.transform;
                case UILayer.Normal: return _normalCanvas.transform;
                case UILayer.Popup: return _popupCanvas.transform;
                case UILayer.Loading: return _loadingCanvas.transform;
            }
            return null;
        }

        /// <summary>
        /// 主菜单的打开/关闭已移到 UIManager（AGENTS.md §5.3「面板打开/关闭用 UIManager」）。
        /// UIService 只保留【渲染基础设施】职责（4 层 Canvas + UI 相机 + 层级查询）。
        /// </summary>

        /// <summary>
        /// 显示 HUD（血条 / 体力 / 魔法 / 金币）。
        /// 面板开关走 UIManager（AGENTS.md §5.3），HUD 用美术 prefab（不再是代码画的色块）。
        /// </summary>
        public void ShowHUD()
        {
            _ = HUDPanel.ShowAsync();
        }

        public void CloseHUD()
        {
            HUDPanel.Close();
        }

        /// <summary>显示升级三选一弹窗</summary>
        public void ShowLevelUp(System.Collections.Generic.List<Data.BuffData> candidates, System.Action<Data.BuffData> onPick)
        {
            LevelUpPanel.Show(GetLayerRoot(UILayer.Popup), candidates, onPick);
        }

        /// <summary>显示结算</summary>
        public void ShowSettle(float surviveSec, int level, int killCount)
        {
            SettlePanel.Show(GetLayerRoot(UILayer.Popup), surviveSec, level, killCount);
        }

        public void CloseSettle()
        {
            SettlePanel.Close();
        }
    }
}