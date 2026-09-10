namespace CryptWalker.HotUpdate.Core
{
    /// <summary>
    /// 池化对象契约（对应框架设计 3.4 / 策划案 8 章）。
    /// 任何想被对象池管理的 MonoBehaviour 都要实现此接口。
    /// 两个方法的调用时机由 ObjectPool 保证：
    ///   Get()   时 → OnSpawn()：复位状态、重置数值、恢复激活
    ///   Return()时 → OnRecycle()：清理状态、停掉效果，准备下次复用
    /// </summary>
    public interface IPoolable
    {
        /// <summary>从池取出时调用。这里做"激活 + 复位"（否则上一次的状态残留）</summary>
        void OnSpawn();

        /// <summary>归还池时调用。这里做"清理"，下次 OnSpawn 才能干净</summary>
        void OnRecycle();
    }
}
