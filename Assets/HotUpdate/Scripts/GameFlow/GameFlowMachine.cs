using CryptWalker.Main;
using UnityEngine;

namespace CryptWalker.HotUpdate.GameFlow
{
    /// <summary>主流程状态基类。玩家游戏阶段 = 状态子类，支持切换。</summary>
    public abstract class FlowState
    {
        protected GameFlowMachine Owner;
        protected FlowState(GameFlowMachine owner) { Owner = owner; }

        public virtual void OnEnter() { }
        public virtual void OnUpdate() { }
        public virtual void OnExit() { }
    }

    /// <summary>
    /// 主流程状态机：Boot → MainMenu → Game(InGame) → Settle → ...
    /// 骨架内置 Boot/MainMenu/Game/Settle；后续将策划案的具体业务替换/扩展。
    /// </summary>
    public class GameFlowMachine
    {
        private FlowState _current;

        public void Start()
        {
            ChangeState(new BootState(this));
        }

        public void Tick()
        {
            _current?.OnUpdate();
        }

        public void ChangeState(FlowState next)
        {
            if (next == null) return;
            _current?.OnExit();
            _current = next;
            next.OnEnter();
        }

        /// <summary>业务侧推荐 API：用类型创建新状态</summary>
        public void ChangeTo<T>() where T : FlowState
        {
            ChangeState((T)System.Activator.CreateInstance(typeof(T), this));
        }

        public T GetCurrentState<T>() where T : FlowState => _current as T;
        public FlowState CurrentState => _current;

        // ============================================================
        // 内建状态：开发期骨架，业务可自由替换
        // ============================================================

        /// <summary>启动态：链路验证后自动进入主菜单</summary>
        public class BootState : FlowState
        {
            public BootState(GameFlowMachine owner) : base(owner) { }

            public override void OnEnter()
            {
                Debug.Log("[GameFlow] 进入 Boot 状态；热更链路已打通，自动进入主菜单");
                Owner.ChangeTo<MainMenuState>();
            }
        }

        /// <summary>主菜单：开发期占位（按 1 键进入游戏）</summary>
        public class MainMenuState : FlowState
        {
            public MainMenuState(GameFlowMachine owner) : base(owner) { }

            public override void OnEnter()
            {
                Debug.Log("[GameFlow] 进入主菜单 | 按 [1] 进入战斗，按 [2] 模拟结算");
            }

            public override void OnUpdate()
            {
                if (Input.GetKeyDown(KeyCode.Alpha1))
                {
                    Debug.Log("[GameFlow] 主菜单：进入战斗");
                    Owner.ChangeTo<GameState>();
                }
                else if (Input.GetKeyDown(KeyCode.Alpha2))
                {
                    Debug.Log("[GameFlow] 主菜单：模拟一局结束");
                    Owner.ChangeTo<SettleState>();
                }
            }
        }

        /// <summary>战斗态：开发期占位（按 ESC 返回菜单）</summary>
        public class GameState : FlowState
        {
            private float _elapsed;

            public GameState(GameFlowMachine owner) : base(owner) { }

            public override void OnEnter()
            {
                _elapsed = 0f;
                Debug.Log("[GameFlow] 进入战斗场景 | 按 [ESC] 强制退出到主菜单");
            }

            public override void OnUpdate()
            {
                _elapsed += Time.deltaTime;
                if (_elapsed > 0.5f && Input.GetKeyDown(KeyCode.Escape))
                {
                    Debug.Log("[GameFlow] 战斗：返回主菜单");
                    Owner.ChangeTo<MainMenuState>();
                }
            }

            public override void OnExit()
            {
                Debug.Log($"[GameFlow] 退出战斗，存活 {_elapsed:F1} 秒");
            }
        }

        /// <summary>结算态：开发期占位（按 0 键回主菜单）</summary>
        public class SettleState : FlowState
        {
            public SettleState(GameFlowMachine owner) : base(owner) { }

            public override void OnEnter()
            {
                Debug.Log("[GameFlow] 进入结算界面 | 按 [0] 返回主菜单");
            }

            public override void OnUpdate()
            {
                if (Input.GetKeyDown(KeyCode.Alpha0))
                {
                    Owner.ChangeTo<MainMenuState>();
                }
            }
        }
    }
}