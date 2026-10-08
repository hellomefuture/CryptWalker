namespace CryptWalker.HotUpdate.Entity.Common
{
    /// <summary>
    /// 通用有限状态机（玩家/敌人共用）。
    /// 职责：持有当前状态，统一驱动 Enter/Update/Exit。
    /// 与 GameFlowMachine 同模式，但面向"实体"（玩家/敌人），泛型化复用于双方。
    ///
    /// 用法：
    ///   var fsm = new Fsm<PlayerFSM>();
    ///   fsm.ChangeState(new IdleState(playerFsm));   // 进入
    ///   fsm.Tick();                                  // 每帧驱动
    /// </summary>
    public class Fsm<T> where T : class
    {
        private FsmState<T> _current;

        /// <summary>当前状态（供外部查询，如 IsAttacking 判断）</summary>
        public FsmState<T> Current => _current;  

        /// <summary>切换到新状态：旧状态 OnExit → 新状态 OnEnter</summary>
        public void ChangeState(FsmState<T> next)
        {
            if (next == null || next == _current) return;   // 防御：空或同状态跳过
            _current?.OnExit();
            _current = next;
            _current.OnEnter(); 
        }

        /// <summary>每帧驱动当前状态（由实体的 Update 调用）</summary>
        public void Tick()
        {
            _current?.OnUpdate();
        }
    }
}
