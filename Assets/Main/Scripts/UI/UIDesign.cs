using UnityEngine;

namespace CryptWalker.Main.UI
{
    /// <summary>
    /// UI 设计空间（**唯一权威**）。
    ///
    /// ===== 为什么要有这个类 =====
    /// 项目里有【两套各自建 Canvas】的代码：
    ///   · AOT 侧：LoadingScreen —— 它必须在热更 dll 加载之前就能显示，所以自己建 Canvas
    ///   · 热更侧：UIService     —— 游戏 UI 的四层 Canvas（HUD/Normal/Popup/Loading）
    ///
    /// 两边都要设 CanvasScaler 的参考分辨率。以前是各写各的字面量 →
    /// **同一个 bug 复发过一次**：
    ///   UIService 已经改成 4K（3840×2160），注释写着"若设成 1920×1080 会被放大 2 倍"，
    ///   而 AOT 的 LoadingScreen 漏改了，仍然是 1920×1080 ✗
    ///   → 加载界面的 Img_bakeground 及其子物体全被放大 2 倍，直接把屏幕撑破。
    ///
    /// 根治办法就是把"设计空间"收敛到一处 —— 本类。
    ///
    /// ===== 为什么放在 AOT 侧 =====
    /// 热更程序集引用了 CryptWalker.Main.Runtime，所以【热更侧能读到 AOT 的常量】✓；
    /// 反过来（AOT 读热更）做不到 ✗ —— 权威只能放在 AOT。
    ///
    /// ⚠️ 改美术稿尺寸时，**只改这里**，两套 Canvas 会同时生效。
    /// </summary>
    public static class UIDesign
    {
        /// <summary>
        /// 美术稿设计分辨率（4K：3840×2160）。
        ///
        /// 含义：美术在 3840×2160 的画布上摆的所有尺寸，都是"设计像素"。
        /// 屏幕上的实际像素 = 设计像素 × scaleFactor，
        /// 而 scaleFactor 由 CanvasScaler 按【实际屏幕分辨率 / 本值】算出。
        ///
        /// 所以本值写错一倍，整个 UI 的大小就错一倍：
        ///   写成 1920×1080 → scaleFactor 大 2 倍 → 4K 的美术稿被放大 2 倍 → 撑破屏幕 ✗
        /// </summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(3840f, 2160f);

        /// <summary>
        /// CanvasScaler 的宽高匹配权重：0 = 按宽匹配，1 = 按高匹配，0.5 = 折中。
        /// 取 0.5 表示宽高各让一半 —— 屏幕比例与 16:9 不同时，两边都不会偏得太狠。
        /// </summary>
        public const float MatchWidthOrHeight = 0.5f;
    }
}
