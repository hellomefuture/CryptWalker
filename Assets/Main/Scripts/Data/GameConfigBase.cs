using UnityEngine;

namespace CryptWalker.HotUpdate.Data
{
    /// <summary>
    /// 所有游戏配置(ScriptableObject)的基类。
    /// 约定：configID 全局唯一，且与 YooAsset 资源的 Address/文件名保持一致，
    /// 便于 DataService 按统一规则加载与缓存。
    /// </summary>
    public abstract class GameConfigBase : ScriptableObject
    {
        [Header("基础标识")]
        [Tooltip("全局唯一ID，与 Address(asset://data/.../ID) 及资源文件名一致")]
        public string configID;
    }
}