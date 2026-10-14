using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 关掉 A* Pathfinding Project 的"联网查版本" —— **编辑器本地噪音抑制**。
    ///
    /// ===== 为什么需要 =====
    /// 每次脚本重编译（域重载）插件都会重新挂上更新检查，而它的检查地址是 http://
    /// → Unity 默认禁止不安全下载 → 每次都抛：
    ///
    ///   InvalidOperationException: Insecure connection not allowed
    ///   UnityEngine.Networking.UnityWebRequest.SendWebRequest ()
    ///   Pathfinding.AstarUpdateChecker.DownloadVersionInfo (AstarUpdateChecker.cs:224)
    ///   Pathfinding.AstarUpdateChecker.CheckForUpdates (AstarUpdateChecker.cs:185)
    ///
    /// Console 里就永远挂着一串红字，会淹掉真正要看的报错。
    ///
    /// ===== 做法（不改插件一行代码，只动它存在 EditorPrefs 里的一个值）=====
    /// 插件这样判断"该不该查了"（AstarUpdateChecker.cs:183）：
    ///     minutesUntilUpdate = lastUpdateCheck + updateCheckRate - 现在
    ///     if (minutesUntilUpdate &lt; 0) 才去下载
    /// 把这个"上次检查时间"推到一个很远的未来 → 条件永远不成立 → 连请求都不会发。
    ///
    /// 为什么不会被它改回去：异常发生在 SendWebRequest()（第 224 行），
    /// 而"把当前时间写回去"的语句是它后面的第 228 行 → 永远执行不到。
    /// 所以这个设置是稳定的，不会下次又被覆盖。
    ///
    /// ===== 影响范围 =====
    ///   只影响【本机编辑器】✓  不影响运行时 ✓  不影响出包 ✓  不同步到别的机器 ✓
    ///   想恢复：删掉这个文件（或把 PrefKey 的值改回 1/1/1971）即可。
    /// </summary>
    [InitializeOnLoad]
    public static class DisableAstarUpdateCheck
    {
        private const string PrefKey = "AstarLastUpdateCheck";

        static DisableAstarUpdateCheck()
        {
            // 用 InvariantCulture 格式化 —— 插件就是这么解析回来的（AstarUpdateChecker.cs:46）
            string future = new System.DateTime(2099, 1, 1).ToString(CultureInfo.InvariantCulture);

            // 已经设过就静默返回：否则每次脚本重编译都会刷一条日志，反而又变成噪音
            if (EditorPrefs.GetString(PrefKey, "") == future) return;

            EditorPrefs.SetString(PrefKey, future);
            Debug.Log("[工具] 已关闭 A* 插件的联网更新检查（只影响本机编辑器，不影响运行与出包）。" +
                      "它每次脚本重编译都会重挂检查循环，而检查地址是 http:// 所以必抛异常 —— 与游戏本身无关。");
        }
    }
}
