using CryptWalker.Main;
using CryptWalker.Main.Data;
using CryptWalker.HotUpdate.Core;
using CryptWalker.HotUpdate.GameFlow.Dungeon;
using CryptWalker.HotUpdate.UI;
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

        /// <summary>
        /// 当前战斗场景的程序化地牢根节点（由 GameState.OnEnter 创建、OnExit 销毁）。
        ///
        /// 为什么用字段持有而不是 GameObject.Find(名字)：
        ///   DungeonGenerator 生成的所有内容（房间/走廊 Tilemap、玩家、敌人）都是带 parent 的 Instantiate，
        ///   实例名会变成 "player(Clone)" / "Orc1(Clone)"，按原名 Find 永远匹配不到 → 清理必然失效。
        ///   持有根节点引用后，删根即可连带回收整棵子树。
        /// </summary>
        internal GameObject DungeonRoot { get; set; }

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

    
        // 内建状态：开发期骨架，业务可自由替换
        

        /// <summary>启动态：链路验证后自动进入主菜单</summary>
        public class BootState : FlowState
        {
            public BootState(GameFlowMachine owner) : base(owner) { }

            public override void OnEnter()
            {
                Log.Info("[GameFlow] 进入 Boot 状态；热更链路已打通，自动进入主菜单");
                Owner.ChangeTo<MainMenuState>();
            }
        }

        /// <summary>
        /// 主菜单（Controller 角色）。
        /// 只做两件事：订阅 UI 事件（处理点击）+ 异步让 UIManager 开面板。
        /// 不直接引用任何 Button / prefab —— 与 View 完全解耦（AGENTS.md §5.3）。
        /// </summary>
        public class MainMenuState : FlowState
        {
            // 竞态保护：异步开面板期间可能已经切走状态
            private bool _active;

            public MainMenuState(GameFlowMachine owner) : base(owner) { }

            public override void OnEnter()
            {
                _active = true;
                Log.Info("[GameFlow] 进入主菜单 | 点击按钮进入战斗/设置/退出");

                // Controller 订阅事件（View 侧在 MainMenuPanel.Bind 里 Raise）
                EventBus.Subscribe(GameEvent.MainMenuStartClicked,   OnStartClicked);
                EventBus.Subscribe(GameEvent.MainMenuSettingClicked, OnSettingClicked);
                EventBus.Subscribe(GameEvent.MainMenuQuitClicked,    OnQuitClicked);
                // 设置面板的保存/取消/继续 → 由 Controller 负责关面板（AGENTS.md §5.3：面板开关走 UIManager）
                EventBus.Subscribe(GameEvent.SettingSaveClicked,     OnSettingClosed);
                EventBus.Subscribe(GameEvent.SettingCancelClicked,   OnSettingClosed);
                EventBus.Subscribe(GameEvent.SettingContinueClicked, OnSettingClosed);
                EventBus.Subscribe(GameEvent.SettingBackHomeClicked, OnSettingBackHome);

                // 异步开面板：用弃元避免 async void（异常在方法内被 catch）
                _ = OpenPanelAsync();
            }

            /// <summary>UIManager 异步拿 prefab（走 YooAsset 或直引用）→ 实例化 → 绑按钮</summary>
            private async System.Threading.Tasks.Task OpenPanelAsync()
            {
                try
                {
                    var panel = await UIManager.Instance.OpenPanelAsync(MainMenuPanel.PanelId);
                    if (panel == null) return;

                    MainMenuPanel.Bind(panel);

                    // 竞态：await 期间已经切走状态 → 立刻关掉刚开的面板
                    if (!_active) UIManager.Instance.ClosePanel(MainMenuPanel.PanelId);
                }
                catch (System.Exception e)
                {
                    Log.Error($"[GameFlow] 主菜单异步打开失败：{e.Message}");
                }
            }

            private void OnStartClicked(object _)
            {
                Log.Info("[GameFlow] 主菜单：开始游戏");
                Owner.ChangeTo<GameState>();
            }

            /// <summary>主菜单「设置」→ 异步打开设置弹窗（Popup 层）</summary>
            private void OnSettingClicked(object _)
            {
                Log.Info("[GameFlow] 主菜单：打开设置面板");
                _ = OpenSettingAsync();
            }

            /// <summary>异步开设置面板 + 绑定控件（与主菜单面板同一套写法）</summary>
            private async System.Threading.Tasks.Task OpenSettingAsync()
            {
                try
                {
                    var panel = await UIManager.Instance.OpenPanelAsync(SettingPanel.PanelId);
                    if (panel == null) return;

                    SettingPanel.Bind(panel);

                    // 竞态：await 期间已经切走状态 → 立刻关掉刚开的面板
                    if (!_active) UIManager.Instance.ClosePanel(SettingPanel.PanelId);
                }
                catch (System.Exception e)
                {
                    Log.Error($"[GameFlow] 设置面板异步打开失败：{e.Message}");
                }
            }

            /// <summary>设置面板「保存 / 取消 / 继续游戏」→ 关闭面板
            /// （应用/回滚的逻辑在 SettingPanel 里，Controller 只管面板生命周期）</summary>
            private void OnSettingClosed(object _)
            {
                UIManager.Instance?.ClosePanel(SettingPanel.PanelId);
            }

            /// <summary>设置面板「返回主页面」→ 关面板，并确保流程回到主菜单状态</summary>
            private void OnSettingBackHome(object _)
            {
                UIManager.Instance?.ClosePanel(SettingPanel.PanelId);

                // 目前设置面板只从主菜单打开，所以这里通常已经在 MainMenuState 上、不需要切。
                // 将来从"暂停菜单"打开时，这个判断会负责把流程切回主菜单。
                // 用条件判断而不是无条件 ChangeTo，是为了避免在主菜单上重复进出状态
                // （OnExit/OnEnter 会把主菜单面板关掉再重开，视觉上会闪一下）。
                if (!(Owner.CurrentState is MainMenuState))
                    Owner.ChangeTo<MainMenuState>();
            }

            private void OnQuitClicked(object _)
            {
                Log.Info("[GameFlow] 主菜单：退出游戏");
                GameRoot.Bridge?.Quit();
            }

            public override void OnUpdate()
            {
                // 开始界面动态效果：按鼠标位置刷新分层视差
                // （这里驱动而不是面板里写 MonoBehaviour，是为了走项目统一的 tick 链路，AGENTS.md §3.2-8）
                MainMenuPanel.TickParallax();

                // 兼容旧键盘输入（开发期，按 1 进入战斗）
                if (Input.GetKeyDown(KeyCode.Alpha1))
                {
                    Log.Info("[GameFlow] 主菜单：[1] 进入战斗（键盘兼容）");
                    Owner.ChangeTo<GameState>();
                }
                else if (Input.GetKeyDown(KeyCode.Alpha2))
                {
                    Log.Info("[GameFlow] 主菜单：[2] 模拟一局结束（键盘兼容）");
                    Owner.ChangeTo<SettleState>();
                }
            }

            public override void OnExit()
            {
                _active = false;
                EventBus.Unsubscribe(GameEvent.MainMenuStartClicked,   OnStartClicked);
                EventBus.Unsubscribe(GameEvent.MainMenuSettingClicked, OnSettingClicked);
                EventBus.Unsubscribe(GameEvent.MainMenuQuitClicked,    OnQuitClicked);
                EventBus.Unsubscribe(GameEvent.SettingSaveClicked,     OnSettingClosed);
                EventBus.Unsubscribe(GameEvent.SettingCancelClicked,   OnSettingClosed);
                EventBus.Unsubscribe(GameEvent.SettingContinueClicked, OnSettingClosed);
                EventBus.Unsubscribe(GameEvent.SettingBackHomeClicked, OnSettingBackHome);
                UIManager.Instance?.ClosePanel(MainMenuPanel.PanelId);
                UIManager.Instance?.ClosePanel(SettingPanel.PanelId);   // 设置面板可能还开着

                // 面板马上被销毁：清掉视差缓存，别继续持有已销毁的 Transform
                MainMenuPanel.ClearParallax();
            }
        }

        /// <summary>
        /// 战斗态。ESC = 暂停菜单（打开设置面板），不再直接退回主菜单；
        /// 退出战斗的唯一入口是设置面板里的「返回主页面」（Button_BackHome）。
        /// </summary>
        public class GameState : FlowState
        {
            private float _elapsed;

            /// <summary>异步开面板期间挡重复请求（否则连按 ESC 会重复请求/刷警告）</summary>
            private bool _settingOpening;

            public GameState(GameFlowMachine owner) : base(owner) { }

            public override void OnEnter()
            {
                _elapsed = 0f;
                _settingOpening = false;
                Log.Info("[GameFlow] 进入战斗场景 | 按 [ESC] 打开设置面板（退出战斗走面板里的「返回主页面」）");

                // 设置面板事件：保存 / 取消 / 继续 / 关闭 → 只关面板（留在战斗）；
                // 「返回主页面」→ 关面板 + 切回主菜单。
                // 与 MainMenuState 订阅同一批事件不会冲突：两个状态不会同时存活 ——
                // GameState.OnExit 先退订，MainMenuState.OnEnter 才订阅。
                EventBus.Subscribe(GameEvent.WeaponSlotClicked,      OnWeaponSlotClicked);
                EventBus.Subscribe(GameEvent.SettingSaveClicked,     OnSettingClosed);
                EventBus.Subscribe(GameEvent.SettingCancelClicked,   OnSettingClosed);
                EventBus.Subscribe(GameEvent.SettingContinueClicked, OnSettingClosed);
                EventBus.Subscribe(GameEvent.SettingBackHomeClicked, OnSettingBackHome);

                // 兜底：清掉可能残留的上一次地牢（异常路径下 OnExit 没跑到时）
                DestroyDungeonRoot();

                // 开新一局：金币清零并广播 CoinChanged（金币是局内货币，不跨局）
                Modules.Wallet.ResetRun();

                // 开战斗 HUD（血条 / 体力 / 魔法 / 金币），与主菜单同一套 UIManager 写法
                _ = OpenHudAsync();

                // 通过 IAppBridge 拿场景里 LevelConfigHolder 注入的 LevelConfig
                // （prefab 直接引用 Art/Prefabs/，不走 Resources.Load / YooAsset address）
                LevelConfig levelConfig = GameRoot.Bridge != null ? GameRoot.Bridge.GetLevelConfig() : null;
                if (levelConfig == null)
                {
                    Log.Error("[GameFlow] 战斗场景：LevelConfig 为空！请在场景里挂 LevelConfigHolder 并拖一个 LevelConfig.asset");
                    return;
                }

                // 临时入口：生成肉鸽关卡（房间 + 走廊 + NavGrid + 玩家 + 敌人）
                // 分帧生成：不再卡住主线程；生成途中按 ESC 会被内部的 Aborted 检查安全终止
                var root = new GameObject("DungeonRoot");
                Owner.DungeonRoot = root;   // 记下根节点，退出战斗时整棵回收
                _ = GenerateDungeonAsync(root.transform, levelConfig);
            }

            /// <summary>
            /// 异步生成地牢（分帧），并全程给加载界面喂进度。
            /// 异常只记日志，不让它从 fire-and-forget 的任务里逃逸。
            ///
            /// 为什么这里要显示加载界面：地牢生成（几十帧）+ A* 扫图（实测 ~230ms 主线程卡顿）
            /// 加起来有明显一顿 —— 用加载界面盖住它，比让玩家看着空场景发呆好。
            /// </summary>
            private async System.Threading.Tasks.Task GenerateDungeonAsync(Transform parent, LevelConfig config)
            {
                var bridge = GameRoot.Bridge;

                // 从 0.6 起步：前面的 0~0.6 是"点进战斗 / 切场景 / 初始化"（AOT 侧已经走过一遍）
                bridge?.ShowLoading(0.6f);
                try
                {
                    await DungeonGenerator.GenerateAsync(parent, config,
                        p => bridge?.SetLoadingProgress(Mathf.Lerp(0.6f, 1f, p)));
                }
                catch (System.Exception e)
                {
                    Log.Error($"[GameFlow] 生成关卡异常：{e}");
                }
                finally
                {
                    // 无论成败都要收掉，否则生成失败时会永远卡在加载画面上
                    bridge?.HideLoading();
                }
            }

            public override void OnUpdate()
            {
                _elapsed += Time.deltaTime;

                // 开局 0.5 秒内的 ESC 不响应：从主菜单点「开始游戏」的那次输入要落在主菜单侧，
                // 否则会出现「刚进战斗就弹出设置面板」。（沿用原来的防抖时长）
                if (_elapsed <= 0.5f) return;
                if (!Input.GetKeyDown(KeyCode.Escape)) return;

                // ESC 语义：没开设置 → 开；已经开着 → 关（暂停菜单的通用习惯）。
                // 不想要「再按一次关闭」就删掉下面这个 if 分支，ESC 只负责打开。
                if (UIManager.Instance?.GetPanel(SettingPanel.PanelId) != null)
                {
                    Log.Info("[GameFlow] 战斗：ESC 关闭设置面板");
                    CloseSetting();
                }
                else
                {
                    Log.Info("[GameFlow] 战斗：ESC 打开设置面板");
                    _ = OpenSettingAsync();
                }
            }

            public override void OnExit()
            {
                // 先退订设置事件 + 关掉可能还开着的设置面板：
                // 异常路径或强制切状态时，面板不会残留、事件也不会重复处理
                EventBus.Unsubscribe(GameEvent.WeaponSlotClicked,      OnWeaponSlotClicked);
                EventBus.Unsubscribe(GameEvent.SettingSaveClicked,     OnSettingClosed);
                EventBus.Unsubscribe(GameEvent.SettingCancelClicked,   OnSettingClosed);
                EventBus.Unsubscribe(GameEvent.SettingContinueClicked, OnSettingClosed);
                EventBus.Unsubscribe(GameEvent.SettingBackHomeClicked, OnSettingBackHome);
                CloseSetting();

                Log.Info($"[GameFlow] 退出战斗，存活 {_elapsed:F1} 秒");
                HUDPanel.Close();   // 收 HUD（含金币控件）并退订事件
                // 修复：销毁地牢根节点 —— 房间/走廊 Tilemap、玩家、敌人都是它的子物体，随之一起回收。
                // 原实现按名字 Find("player")/Find("DungeonMap") 清理，但实例名是 "player(Clone)"，
                // 且 DungeonRoot 从未被销毁 → 每进一次战斗就泄漏一整套地牢。
                DestroyDungeonRoot();
            }

            /// <summary>异步开 HUD + 绑定控件（与 MainMenuState.OpenPanelAsync 同一套写法）</summary>
            private async System.Threading.Tasks.Task OpenHudAsync()
            {
                try
                {
                    await HUDPanel.ShowAsync();
                }
                catch (System.Exception e)
                {
                    Log.Error($"[GameFlow] HUD 打开失败：{e.Message}");
                }
            }

            /// <summary>销毁地牢根节点（用 Destroy 而非 DestroyImmediate：运行时销毁应走正常回收流程）。</summary>
            private void DestroyDungeonRoot()
            {
                if (Owner == null || Owner.DungeonRoot == null) return;
                Object.Destroy(Owner.DungeonRoot);
                Owner.DungeonRoot = null;
            }

            // ==================== 设置面板（战斗内 = 暂停菜单） ====================

            /// <summary>异步开设置面板 + 绑定控件（与 MainMenuState.OpenSettingAsync 同一套写法）</summary>
            private async System.Threading.Tasks.Task OpenSettingAsync()
            {
                if (_settingOpening) return;   // 连按 ESC 只算一次
                _settingOpening = true;
                try
                {
                    var panel = await UIManager.Instance.OpenPanelAsync(SettingPanel.PanelId);
                    if (panel == null) return;

                    SettingPanel.Bind(panel);
                }
                catch (System.Exception e)
                {
                    Log.Error($"[GameFlow] 战斗内设置面板打开失败：{e.Message}");
                }
                finally
                {
                    _settingOpening = false;
                }
            }

            /// <summary>关设置面板（没开也安全：UIManager 内部会忽略）</summary>
            private void CloseSetting()
            {
                UIManager.Instance?.ClosePanel(SettingPanel.PanelId);
            }

            /// <summary>设置面板「保存 / 取消 / 继续 / 关闭」→ 只关面板，留在战斗中</summary>
            private void OnSettingClosed(object _)
            {
                CloseSetting();
            }

            /// <summary>
            /// 选武器面板点了某个槽位 → 切武器 → 广播结果 ✓
            ///
            /// 分工（AGENTS.md §5.3 ✓）：
            ///   View（HUDPanel ✗）：只 Raise(WeaponSlotClicked) ✓ 不碰 PlayerController ✓
            ///   Controller（本类 ✓）：收到后调玩家 ✓，再把【事实】广播回去 ✓
            ///     → HUD 靠 WeaponSwitched 刷新图标 ✓ 两边都不持有对方引用 ✓
            ///
            /// 注意 ✓：切失败了（同槽位 / 切换硬直内 ✓）就**不发** WeaponSwitched ✓
            /// —— 否则 UI 会以为换了 ✓ 高亮和实际不一致 ✓
            /// </summary>
            private void OnWeaponSlotClicked(object payload)
            {
                if (!(payload is int slot)) return;

                var pc = CryptWalker.HotUpdate.Entity.Player.PlayerController.Instance;
                if (pc == null)
                {
                    Log.Warn("[GameFlow] 切换武器失败：找不到 PlayerController.Instance");
                    return;
                }

                if (pc.SwitchWeaponBySlot(slot))
                    EventBus.Raise(GameEvent.WeaponSwitched, slot);
            }

            /// <summary>设置面板「返回主页面」→ 关面板 + 切回主菜单（战斗的唯一退出入口）</summary>
            private void OnSettingBackHome(object _)
            {
                CloseSetting();
                Owner.ChangeTo<MainMenuState>();
            }
        }

        /// <summary>结算态：开发期占位（按 0 键回主菜单）</summary>
        public class SettleState : FlowState
        {
            public SettleState(GameFlowMachine owner) : base(owner) { }

            public override void OnEnter()
            {
                Log.Info("[GameFlow] 进入结算界面 | 点按钮或按 [0] 返回主菜单");
                EventBus.Subscribe(GameEvent.SettleBackToMenuClicked, OnBackToMenuClicked);
            }

            private void OnBackToMenuClicked(object _)
            {
                Log.Info("[GameFlow] 结算：返回主菜单");
                Owner.ChangeTo<MainMenuState>();
            }

            public override void OnUpdate()
            {
                if (Input.GetKeyDown(KeyCode.Alpha0)) Owner.ChangeTo<MainMenuState>();
            }

            public override void OnExit()
            {
                EventBus.Unsubscribe(GameEvent.SettleBackToMenuClicked, OnBackToMenuClicked);
            }
        }
    }
}