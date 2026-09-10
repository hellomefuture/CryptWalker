namespace CryptWalker.HotUpdate.Entity.Common
{
    /// <summary>
    /// 状态基类（通用 FSM 骨架，玩家/敌人共用）。
    /// 生命周期三个钩子，由 Fsm&lt;T&gt; 驱动：
    ///   OnEnter  ：进入状态时（播动画 / 复位计时）
    ///   OnUpdate ：每帧（读输入 / 判断能否转移）
    ///   OnExit   ：离开状态时（清理 / 停效果）
    /// Owner 用泛型 T：玩家状态 T=PlayerFSM，敌人状态 T=EnemyFSM，
    /// 状态通过 Owner 访问实体数据（朝向/生命/速度...）。
    /// </summary>
    public abstract class FsmState<T> where T : class
    {
        /// <summary>持有者（由子类状态访问：Owner.xxx）</summary>
        protected readonly T Owner;

        protected FsmState(T owner)
        {
            Owner = owner;
        }

        public virtual void OnEnter() { }
        public virtual void OnUpdate() { }
        public virtual void OnExit() { }
    }
}
