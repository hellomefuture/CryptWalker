using System;
using System.Collections.Generic;

namespace CryptWalker.HotUpdate.Core
{
    /// <summary>
    /// 全局事件类型枚举（对应框架设计 3.2）。
    /// 用枚举代替字符串：编译器检查，写错直接编译报错，不会静默失效。
    /// 新增事件 = 在这里加一行，再在业务代码里 Subscribe/Raise。
    /// </summary>
    public enum GameEvent
    {
        PlayerDamaged,    // 玩家受伤（payload: DamageInfo）
        PlayerDied,       // 玩家死亡
        EnemyDied,        // 敌人死亡（payload: 敌人实例）
        LevelUp,          // 玩家升级
        BuffChosen,       // 三选一选中 Buff
        WeaponSwitched,   // 切换武器（payload: 武器槽索引）
        CoinChanged,      // 金币变化（payload: int）
        RoomEntered,      // 进入房间（payload: RoomData）
        GameOver,         // 本局结束
    }

    /// <summary>
    /// 轻量事件中心（对应框架设计 3.2）。
    /// 职责：MVC 三层（Model / View / Controller）之间唯一的通信通道，模块不互持引用。
    ///
    /// 用法：
    ///   EventBus.Raise(GameEvent.EnemyDied, enemy);         // 任意地方发事件
    ///   EventBus.Subscribe(GameEvent.EnemyDied, OnEnemyDied); // 任意地方订阅
    ///   EventBus.Unsubscribe(GameEvent.EnemyDied, OnEnemyDied); // 不用了记得退订（防泄漏）
    ///
    /// 为什么用 Action&lt;object&gt;：
    ///   1. payload 可以装任意类型（数据类 / 敌人实例 / int）
    ///   2. 单机规模不需要泛型派发，简单够用
    /// </summary>
    public static class EventBus
    {
        // 事件类型 -> 订阅者列表
        private static readonly Dictionary<GameEvent, List<Action<object>>> s_handlers
            = new Dictionary<GameEvent, List<Action<object>>>();

        /// <summary>订阅事件。同一 handler 订阅多次会重复收到，注意不要重复订阅。</summary>
        public static void Subscribe(GameEvent type, Action<object> handler)
        {
            if (!s_handlers.TryGetValue(type, out var list))
            {
                list = new List<Action<object>>(4);   // 初始容量 4，减少扩容
                s_handlers[type] = list;
            }
            list.Add(handler);
        }

        /// <summary>退订事件。UI 关闭/对象销毁时必须退订，否则泄漏（对象被列表持有）。</summary>
        public static void Unsubscribe(GameEvent type, Action<object> handler)
        {
            if (s_handlers.TryGetValue(type, out var list))
                list.Remove(handler);
        }

        /// <summary>广播事件。无订阅者时快速返回（不分配）。</summary>
        public static void Raise(GameEvent type, object payload = null)
        {
            if (!s_handlers.TryGetValue(type, out var list)) return;

            // 倒序遍历：允许处理函数内部调用 Unsubscribe 而不破坏遍历索引。
            // 正序遍历时 Remove 会让 list.Count 变化，可能跳过或越界。
            for (int i = list.Count - 1; i >= 0; i--)
                list[i]?.Invoke(payload);
        }
    }
}
