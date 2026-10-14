# CryptWalker 项目 AI 代理规则（AGENTS.md）

> 本文件是 AI 编码代理（TraeCode、Claude Code、Copilot 等）必须遵循的项目级规则。
> 修改代码前请先读完整个文件，违反硬约束的改动会被拒绝。

---

## 1. 项目身份（Project Identity）

- **项目名称**：《地穴行者》(CryptWalker)
- **类型**：2D Roguelike 俯视角像素风游戏
- **引擎**：Unity 2022 LTS
- **渲染**：URP 2D
- **平台**：PC + Android（优先级：Android）
- **开发阶段**：毕业设计 + 简历作品（2026 秋招）
- **作者**：罗庆烽

---

## 2. 核心架构（Core Architecture）

### 2.1 双 Assembly 架构
- **AOT 程序集**（`Assets/Main/`）：启动入口、HybridCLR 桥接、YooAsset 初始化
- **HotUpdate 程序集**（`Assets/HotUpdate/`）：所有业务逻辑（Player、Enemy、UI、Map、FSM、Module）
- **跨程序集通信**：通过 `IAppBridge` 接口隔离，杜绝 HotUpdate 直接依赖 AOT API

### 2.2 框架选型
- **资源管理**：YooAsset 2.3.16（OpenUPM 安装）
- **热更新**：HybridCLR
- **架构模式**：轻量 MVC（Model 管数据 / View 管显示 / Controller 管逻辑），不使用 PureMVC
- **通信机制**：EventBus + GameEvent 枚举

### 2.3 目录结构
```
Assets/
├── Main/                     AOT 主程序集
│   └── Scripts/Boot/         启动相关
├── HotUpdate/                HotUpdate 程序集
│   └── Scripts/
│       ├── Core/             框架层（Log、Module、EventBus、Pool）
│       ├── Character/        Player / Enemy
│       ├── UI/               UI 系统
│       ├── Map/              地图与关卡生成
│       ├── Combat/           战斗系统
│       ├── Data/             ScriptableObject 配置
│       └── GameFlow/         主流程状态机
├── YooAsset/                 资源管理框架
├── Art/                      美术资源
├── Scenes/                   场景
├── Settings/                 URP / Input / Tag 设置
└── UI/                       UGUI 资源
```

---

## 3. 硬约束（Hard Constraints）

> 以下规则**不可违反**，违反前必须先与用户确认。

### 3.1 热更新相关
1. HotUpdate 程序集业务逻辑必须可热更，无需重打包
2. HotUpdate DLL 严禁直接调用 AOT API，必须通过 `IAppBridge` 接口
3. AOT 补充元数据必须在 HotUpdate DLL 加载**之前**完成
4. AOT 补充元数据目录：`HybridCLRData/AssembliesPostIl2CppPatch`（含 mscorlib.dll、System.dll、System.Core.dll）
5. HotUpdate 程序集入口类型：`HotUpdate.GameRoot`

### 3.2 启动相关
6. `MainLauncher` 是游戏唯一物理入口
7. `MainLauncher.Start()` 使用 `async void` + `try/catch`，启用容错降级
8. `MainLauncher.Update()` 通过 `_hotUpdateTick` 回调驱动 HotUpdate 端每帧逻辑
9. YooAsset 初始化使用 C# 标准 `async/await`，**不使用** UniTask
10. `MainLauncher.TryBootHotUpdate()` 失败时必须降级，不能让玩家卡启动

### 3.3 模块系统
11. 所有系统模块必须实现 `IModule` 接口（含 `Init()` 和 `Shutdown()` 方法）
12. 模块注册使用 `Modules.Register<T>()` 流式 API，**不使用反射**注册
13. 模块初始化按依赖顺序（`Modules.InitAll()`）
14. 模块注销按注册反序（`Modules.ShutdownAll()`）
15. `ObjectPool` 必须在 UI 和 Audio 模块**之前**初始化
16. `DataService` 必须作为第一个模块注册

### 3.4 UI 系统
18. UI 必须按 4 层根节点管理：`HUD(0) / Normal(100) / Popup(200) / Loading(300)`
19. UI 面板命名后缀统一：`*Panel / *Window / *View`
20. 数据驱动 UI 使用 ScriptableObject（`UIPanelConfig` / `ItemData` 等）
21. UI 加载异步化，避免主线程卡顿

### 3.5 动画规则
22. **Player Animator 控制器由 FSM 直接 `Animator.Play(clipName)` 驱动**
23. Player Animator 控制器**不允许**设参数和 Transition
24. Player 动画片段名包含方向：`WalkSouth / WalkNorth / WalkLeft / WalkRight / AttackSouth / AttackNorth / AttackLeft / AttackRight / IdleSouth / IdleNorth / IdleLeft / IdleRight`
25. Player 转向时 FSM 必须**重播**当前动画以切换方向片段
26. Enemy Animator 控制器由 `EnemyAnimatorBuilder` 工具自动生成（24 个状态：6 动作 × 4 方向）
27. Enemy 动画 Idle / Walk / Run 设循环播放；Attack / Hurt / Death 单次播放

### 3.6 战斗规则
28. 连招通过新增状态类实现：`Attack1State → Attack2State → Attack3State`
29. 技能由 FSM 管理冷却（Cooldown）和硬直（Stun）状态
31. 玩家连击和技能扩展**必须**走 FSM 状态链

### 3.7 状态机
32. 通用骨架：`Fsm<T> + FsmState<T>`
33. 复用：`PlayerFSM / EnemyFSM / GameFlowMachine`
34. 状态切换通过 `ChangeState<T>()`
35. `GameFlowMachine` 状态：`Boot → MainMenu → Game → Settle`

### 3.8 对象池
36. 使用项目内 `ObjectPool<T>`，禁止 `Instantiate / Destroy` 来回创建
37. 池化对象必须实现 `IPoolable` 接口（含 `OnSpawn / OnRecycle`）
38. `ObjectPool.Get()` 在 `OnSpawn` 前激活 GameObject
39. `ObjectPool.Return()` 在 `OnRecycle` 后停用 GameObject
40. `ObjectPool` 必须有最大容量限制，防止无限增长
41. `ObjectPool` 构造函数接收 `Func<T>` 创建委托，支持依赖注入解耦

### 3.9 资源管理
42. 所有资源加载**必须**走 YooAsset，**禁止**用 `Resources.Load`
43. 资源地址格式统一：`asset://data/{folder}/{id}`
44. 资源加载/卸载必须成对，避免引用计数泄漏
45. `DataService.GetAsync<T>` 使用 address 缓存，create 委托为 null 必须 fail-fast
47. `YooAssetService` 封装 API，支持 `EditorSimulate / Offline / Host` 三模式

### 3.10 命名规范
48. 私有实例字段：`_camelCase`（`_moveSpeed`、`_currentState`）
49. 公开字段：`PascalCase`（`MoveSpeed`、`CurrentState`）
50. 静态字段：`s_camelCase`（`s_instance`、`s_pool`）
51. 常量：`SCREAMING_SNAKE_CASE`（`MAX_HEALTH`、`DEFAULT_PORT`）
52. 接口加 `I` 前缀：`IModule / IPoolable / IAppBridge`
53. 类命名 `PascalCase`：`PlayerController / EnemyFSM / GameFlowMachine`

### 3.11 日志和错误处理
54. 日志使用 `Core.Log.Info / Warn / Error`，**禁止**直接 `Debug.Log`
55. `bridge.Log` 仅作为内部桥接调用，业务代码必须用 `Core.Log`
56. 错误处理使用 `try/catch` + 日志 + 降级策略
57. 异步事件只能记录入站 / 出站日志，不记录中间过程

### 3.12 通信机制
58. `EventBus` 事件名使用 `GameEvent` 枚举，禁止裸字符串
59. `EventBus.Raise()` 使用反向迭代，允许广播中取消订阅
60. 模块通信统一走 `EventBus`，不要直接调用其他模块

### 3.13 工具使用
61. 涉及 Unity Editor 资源（场景、Prefab、Animator）修改必须用 `unity-cli`
62. C# 业务脚本（生命周期、协程、序列化）用 `unity-csharp-scripting`
63. UPM 包管理用 `unity-package-management`
64. Unity 后端服务（Passport / Save / Ranking）用 `build-live-game`
65. 修改 Animator 连接、检查场景、修复 Prefab 必须用 `unity-cli`（需 Unity Editor 运行）
66. Player 攻击判定用 `unity-csharp-scripting` 写脚本，用 `unity-cli` 在 Editor 中验证效果
67. 安装包用 `unity-package-management`

### 3.14 分层边界表（什么走哪条路）
> 边界**写成表**，不靠人记。只有一条判断标准：**这是「业务」还是「引擎设施」？业务走总线，设施直接调。**
> 这张表的意义是终结这类反复出现的问题：「HUDPanel 自己 new GameObject」「业务直接 Instantiate 面板」。

| 场景 | 走什么 | 禁止 |
| --- | --- | --- |
| 业务数据变化（HP / 体力 / 魔法 / 金币 / 升级 / 关卡进度） | `EventBus.Raise(GameEvent.X)` | ❌ 直接找到 HUD 去刷 UI |
| UI 面板的开关 | `UIManager.OpenPanelAsync / ClosePanel` | ❌ 业务自己 `new GameObject` / 自己 `Instantiate` 面板 |
| UI 渲染设施（四层 Canvas、UI 相机） | `UIService` | ❌ 面板 prefab 里自带 Canvas / 自己建层 |
| 资源加载 | `IAppBridge.LoadAssetAsync`（内部走 YooAsset） | ❌ `Resources.Load`；❌ 热更侧直接引用 `YooAssetService` |
| 高频对象的生成/回收 | `PoolManager` | ❌ 热更侧直接 `Instantiate/Destroy`（子弹、飘字、列表项） |
| 状态机内部切换（Idle→Walk→Attack） | 直接调 FSM 方法 | ❌ 用 `EventBus` 传状态切换 |
| 模块自己的内部实现 | 私有方法 | ❌ 为了"解耦"再造一个只有自己收的事件 |
| 跨模块调用 | ① 优先 `EventBus` ② 其次 `Modules.Xxx` 的公开方法 | ❌ 模块 A 持有模块 B 的 UI / 实例引用 |
| 每帧变化的数据（玩家坐标、瞄准方向、拖拽中） | `Update` 里直接读属性 | ❌ 每帧 `Raise` 事件（EventBus 是低频总线） |
| 日志 | `Core.Log.Info / Warn / Error` | ❌ `Debug.Log`；❌ 在 `Update` 热路径里打日志 |
| 数值配置（武器/敌人/Buff/面板） | `DataService.GetAsync<T>` 读 ScriptableObject | ❌ 把可调数值硬编码散在代码里当"配置" |

**拿不准时套这三条判据：**

1. **方向**：事件是"通知"，不是"命令"。
   想让别人干活 → **直接调**；只是想告诉别人"我变了" → **Raise**。
   （这就是为什么 `GameState` 直接调 `DungeonGenerator` 而 Publish `EnemyDied`）
2. **频率**：每帧都发生的 → 直接读/拉，**不进总线**（否则一帧几十次字典查找 + 委托调用）。
3. **归属**：同一个模块内部的事 → 私有方法；**跨模块**才考虑总线。

---

### 3.15 资产上只能挂 AOT 类型（热更脚本一律走「代理组件」）⚠️
> 这条是**真机出包**最容易翻车的地方 ✗ —— 表现是「编辑器里一切正常，打包后一片 `missing script`」。
> 已经因此踩过一次大的 ✗：39 条 missing script、玩家和敌人在包里完全不工作。

**规则**：`prefab` / `scene` / `.asset` 里**序列化的脚本引用，必须指向 AOT 程序集的类型** ✗。
热更类型（`CryptWalker.HotUpdate.*`）**不能**直接挂在资产上 ✗。

**为什么**：Unity 解析序列化里的脚本引用，靠的是**打包时那份脚本表** ✗。
热更程序集是运行时 `Assembly.Load` 进来的 ✗，不在那份表里 ✗ → 引用**必然**解析失败 ✓

⚠️ **加载顺序救不了** ✗：实测过 —— 日志里 `热更程序集加载成功` **先于** prefab 实例化 ✓，
组件照样是 missing ✓。这不是时序问题，是**结构问题** ✓。

**症状**（真机 `Player.log`）：
```
The referenced script (CryptWalker.HotUpdate.Entity.Enemy.EnemyBase) on this Behaviour is missing!
```

**正确做法**：资产上挂 **AOT 代理组件** ✓，热更组件由它运行时创建 ✓：

| 资产上挂什么（AOT ✓） | 运行时创建 + 注入 |
| --- | --- |
| `EnemyProxy` | `EnemyBase` ✓ ← 注入 `EnemyConfig` |
| `PlayerProxy` | `PlayerController` ✓ ← 注入 `PlayerConfig` |
| `WeaponProxy` | `MeleeWeapon` / `RangedWeapon` / `SpellWeapon` ✓ ← 注入 `WeaponConfig` |

**两条配套约束**（都是踩出来的 ✗）：
1. **数值放 AOT 的 SO** ✓（`EnemyConfig` / `PlayerConfig` / `WeaponConfig` ✓），代理只持有 SO 引用 ✓
2. **注入后必须重新派生** ✗：热更组件的 `Awake` 会**先**跑（`AddComponent` 是同步的 ✓），
   那一刻字段还是代码默认值 ✗ → 凡 `Awake` 里"用过"的字段（如 `Hp = maxHp`），
   `ApplyTuning` 里都要**重算一次** ✓，否则血条会停在上限的旧值 ✓

**加字段 / 改数值的区别** ✓：
- 改**数值** → 改 SO 资产 ✓（不写代码 ✓，但仍需重新出包才生效 ✓）
- 加**字段** → 在 AOT 的 SO 里加 ✓（**必须重新出包** ✓）

**新增敌人 / 武器的完整步骤**：见 **§16** ✓

---

## 4. 模块系统规范（Module Rules）

### 4.1 IModule 接口
```csharp
public interface IModule
{
    void Init();     // 模块初始化
    void Shutdown(); // 模块注销（清理资源）
}
```

### 4.2 注册与初始化顺序
```csharp
Modules.Register<DataService>()      // 第 1 个：数据服务
         .Register<ObjectPool>()     // 第 2 个：对象池（必须在 UI / Audio 前）
         .Register<AudioService>()   // 第 3 个：音频
         .Register<UIService>();     // 第 4 个：UI

Modules.InitAll();    // 按注册顺序初始化
Modules.ShutdownAll(); // 按注册反序注销
```

### 4.3 注册禁忌
- 不用反射注册模块
- 不在 Init() 里调其他模块的方法（用 EventBus 通信）
- 不在 Init() 里创建 GameObject

---

## 5. UI 系统规范（UI Rules）

### 5.1 四层根节点
| 层级 | SortingOrder | 用途 |
|---|---|---|
| HUD  | 0   | 战斗 HUD（血条、技能、伤害数字）|
| Normal | 100 | 普通面板（背包、装备）|
| Popup | 200 | 弹窗（设置、提示）|
| Loading | 300 | 加载界面（全屏）|

### 5.2 UI 面板脚本结构
```
Scripts/UI/Panels/
├── MainMenuPanel.cs       继承 UIPanelBase
├── InventoryPanel.cs
├── EquipmentPanel.cs
├── SettlePanel.cs
└── ...
```

### 5.3 数据驱动 UI
- UI 面板配置：**统一放在一个** `UIConfig.asset` 里（`panels` 数组 ✓），
  由场景 `_Boot` 上的 `UIConfigHolder` 注入 ✓（不再是一面板一个 asset ✓）
- 面板打开/关闭用 `UIManager.OpenPanel<T>()` / `ClosePanel<T>()`
- 按钮回调用 EventBus 解耦

---

## 6. 资源管理规范（Resource Rules）

### 6.1 YooAsset 使用
```csharp
// 加载资源
var handle = await YooAssetService.LoadAsync<GameObject>("asset://data/rooms/Start_Room");
var prefab = handle.Result;
Instantiate(prefab, position, Quaternion.identity);

// 用完释放
handle.Release();
```

### 6.2 资源地址规范
| 类型 | 格式 | 示例 |
|---|---|---|
| Prefab | `asset://data/{folder}/{id}` | `asset://data/rooms/Start_Room` |
| ScriptableObject | `asset://data/{folder}/{id}` | `asset://data/config/GameConfig` |
| Sprite | `asset://data/{folder}/{id}` | `asset://data/decor/Torch` |

### 6.3 加载/卸载原则
- 一次加载对应一次释放（除非缓存需求）
- 异步加载必须 `await`，避免野回调
- Handle 必须缓存到字段，不能丢失

---

## 7. 动画规范（Animation Rules）

### 7.1 Player 动画驱动流程
```
Input → PlayerController → PlayerFSM.ChangeState<>()
  → 当前状态调用 Owner.PlayAction("Walk", facing)
    → 拼接 "Walk" + Direction（South/North/Left/Right）
      → Animator.Play(clipName)
```

### 7.2 Enemy 动画生成流程
- 用 `EnemyAnimatorBuilder` 工具自动生成 24 状态控制器
- 不写参数 / Transition / SubStateMachine
- 直接 `Animator.Play(clipName)`

### 7.3 动画片段命名
| 类别 | 命名 | 示例 |
|---|---|---|
| Player Idle | `Idle{Direction}` | `IdleSouth` |
| Player Walk | `Walk{Direction}` | `WalkSouth` |
| Player Attack | `Attack{Direction}` | `AttackSouth` |
| Enemy Idle | `Idle{Direction}` | `Orc1_IdleSouth` |
| Enemy Walk | `Walk{Direction}` | `Orc1_WalkSouth` |
| Enemy Attack | `Attack{Direction}` | `Orc1_AttackSouth` |

---

## 8. 对象池规范（Object Pool Rules）

### 8.1 IPoolable 接口
```csharp
public interface IPoolable
{
    void OnSpawn();    // 从池中取出时调用
    void OnRecycle();  // 放回池中时调用
}
```

### 8.2 使用示例
```csharp
public class Bullet : MonoBehaviour, IPoolable
{
    public void OnSpawn() { gameObject.SetActive(true); }
    public void OnRecycle() { gameObject.SetActive(false); }
}

// 注入到池
ObjectPool<Bullet> bulletPool = new ObjectPool<Bullet>(
    createFunc: () => Instantiate(bulletPrefab),
    maxSize: 100
);
```

---

## 9. 状态机规范（FSM Rules）

### 9.1 通用骨架
```csharp
public abstract class Fsm<T> where T : class
{
    public T Owner { get; }
    public FsmState<T> CurrentState { get; private set; }
    public void ChangeState<TState>() where TickerState : FsmState<T>;
    public void OnUpdate(float deltaTime);
}

public abstract class FsmState<T> where T : class
{
    public abstract void OnEnter(T owner);
    public abstract void OnUpdate(T owner, float deltaTime);
    public abstract void OnExit(T owner);
}
```

### 9.2 FSM 类型
- `PlayerFSM`：3 状态（Idle / Walk / Attack）
- `EnemyFSM`：6 状态（Idle / Walk / Run / Attack / Hurt / Death）
- `GameFlowMachine`：4 状态（Boot / MainMenu / Game / Settle）

---

## 10. 数据驱动规范（Data Driven Rules）

### 10.1 ScriptableObject 配置

| 用途 | 实际文件（截至 2026-10） | 层 |
| --- | --- | --- |
| 玩家数值 | `Assets/Art/Data/Player_Swordsman.asset`（`PlayerConfig`） | **AOT** ✓ |
| 敌人数值 | `Assets/Art/Data/Enemy_Orc1.asset` / `Enemy_Orc2` / `Enemy_Orc3`（`EnemyConfig`） | **AOT** ✓ |
| 武器数据 | `Assets/Art/Data/Weapon_Sword.asset`（`WeaponConfig`） | **AOT** ✓ |
| 关卡数据 | `Assets/Art/Data/Levels/LevelConfig.asset` | **AOT** ✓ |
| 地块集 | `Assets/Art/Data/RoomTileset1.asset` | **AOT** ✓ |
| UI 面板配置 | `UIConfig.asset`（引用挂在场景 `_Boot` 上） | **AOT** ✓ |

> ⚠️ **为什么配置 SO 都在 AOT** ✗：它们被 prefab 上的**代理组件**引用 ✓ ——
> 凡"被资产引用"的类型都必须是 AOT 的 ✓（原因与后果见 **§3.15** ✓）。
>
> 新增敌人 / 武器：见 **§16** ✓

### 10.2 配置项设计原则
- 一个 SO 文件只负责一个数据类型
- 不在 SO 里写逻辑方法，只存数据
- 通过 SerializeField 暴露字段供 Inspector 编辑

---

## 11. 日志规范（Logging Rules）

### 11.1 日志 API
```csharp
Core.Log.Info("玩家进入战斗");          // 普通信息（Release 包被编译期剔除）
Core.Log.Warn("玩家血量过低");          // 警告（Release 包保留）
Core.Log.Error("加载资源失败");         // 错误（Release 包保留）
```

**Info 是编译期剔除的**（挂 `[Conditional("DEBUG")]` + `[Conditional("UNITY_EDITOR")]`）：
- 编辑器和 Development Build → 保留
- **Release 包 → 连 `$"..."` 字符串拼接一起消失，零 GC**

这正是"运行时 if 过滤"做不到的：`Log.Info($"[X] {a}{b}")` 里的 `$""` 在调用**之前**就算完了，
所以哪怕 Release 不输出，热路径照样在产垃圾。`[Conditional]` 是编译期行为，整句一起删。

**Warn / Error 故意不加 Conditional**：它们频率低，而且通常意味着"配置错了 / 出问题了"——
恰恰是 Release 包里最需要看到的线索，剔除等于让线上排查变瞎。

### 11.2 禁忌
- 不要 `Debug.Log("...")` 直接输出
- 不要 `bridge.Log("...")` 在业务代码中调用
- 不要在循环里频繁打日志
- 不要打印敏感信息（账号、密码、Token）
- **不要在日志参数里写有副作用的表达式**（Release 下 Info 不会执行 → 语义变了）
  - ✅ `Log.Info($"[X] 数量={list.Count}")` —— 读属性无副作用
  - ❌ `Log.Info($"[X] id={NextId()}")` —— `NextId()` 在 Release 下不会执行
- 不要用 `Warn` / `Error` 打"每帧都会发生"的正常信息（它们不会被剔除，会造成真实的性能开销和日志洪水）
- 想在 Release 包里临时看 Info：把 `Core/Log.cs` 里那两个 `[Conditional]` 注释掉、
  重跑 `CryptWalker/构建/①`（只重编热更 dll，几秒），不用重出整包

---

## 12. 沟通规范（Communication Rules）

### 12.1 语言
- 与用户沟通使用**中文**
- 代码注释使用**中文**
- 提交信息使用**中文**

### 12.2 改代码流程
1. **先解释**："为什么这样改"先说清
2. **再给代码**：完整代码块或 diff
3. **最后给验证**：怎么测试改动是否生效

### 12.3 不要做的事
- 不要无脑重写整个文件
- 不要在没询问的情况下改写用户已有代码
- 不要生成用户没要求的功能
- 不要创建用户没要求的文件
- 不要假设需求，先问清楚

### 12.4 必须做的事
- 修改前先读相关文件（避免覆盖用户已有改动）
- 复杂改动先列计划再实施
- 给代码时同时给"为什么这样写"的解释

---

## 13. 工程约束（Engineering Constraints）

### 13.1 Git 提交
- 提交信息格式：`type(scope): subject`
- 示例：`feat(player): 添加 WalkSouth 状态`、`fix(ui): 修复主菜单按钮不响应`
- 不要把 `.meta` 文件单独提交

### 13.2 包结构
- 主程序集（`Main`）保持轻量，第三方依赖最小化
- 业务逻辑集中在 HotUpdate 程序集

### 13.3 性能底线
- 单场景同屏动态光源 ≤ 16
- DrawCall ≤ 100（战斗场景）
- 帧率目标：移动端 ≥ 30fps，PC ≥ 60fps

### 13.4 字符串字面量里，中文内部一律用「」
> **注释里怎么写都行** ✓（本项目注释习惯用半角 `"…"` ✓ 无害 ✓）。
> 本条规定的是**字符串字面量内部** ✗（`Tooltip(...)` / `Debug.Log(...)` / 面板文案 ✓）。

在字符串里夹半角引号 ✗ → 会把 C# 字符串**截断** ✓，而编译器给的是完全看不懂的报错 ✗：

```
error CS1003: 语法错误，应输入“,”
error CS0103: 当前上下文中不存在名称“木桩”        ← 掉到字符串外面的那几个字
error CS1729: “TooltipAttribute”不包含采用 3 个参数的构造函数
```

| | |
| --- | --- |
| ❌ | `[Tooltip("留空 → 敌人会变"木桩"")]` |
| ✅ | `[Tooltip("留空 → 敌人会变成「木桩」")]` |

**这条已经踩过两次**（`EnemyProxy` / `WeaponConfig` ✓）——
排查口诀 ✓：看到 `CS1003` + 「找不到名称「几个中文字」」✗ → **先回去数那一行的引号** ✓✓

---

## 14. 反例（Anti-Patterns）

### ❌ 不要这样做
```csharp
// ❌ 用 Resources.Load
var prefab = Resources.Load<GameObject>("Players/Player");

// ❌ 用 Instantiate / Destroy 来回创建子弹
var bullet = Instantiate(bulletPrefab);
Destroy(bullet, 2f);

// ❌ Animator 设参数控制
animator.SetInteger("FaceDir", 3);
animator.SetBool("IsMoving", true);

// ❌ 跨程序集直接调用 AOT 类型
HotUpdatePlayer player = new HotUpdatePlayer();
MainLauncher.SomeAOTMethod();  // 报错：HotUpdate 不能直接依赖 AOT

// ❌ Debug.Log 打日志
Debug.Log("进入战斗");
```

### ✅ 应该这样做
```csharp
// ✅ 用 YooAsset 加载
var handle = await YooAssetService.LoadAsync<GameObject>("asset://data/player/Player");
var prefab = handle.Result;

// ✅ 用对象池
var bullet = bulletPool.Get();
bulletPool.Return(bullet);  // 不销毁，放回池中

// ✅ Animator 直接 Play 片段
animator.Play("WalkSouth");

// ✅ 通过 IAppBridge 调 AOT
IAppBridge bridge = AppBridge.Instance;
bridge.Log("进入战斗");  // 业务日志走 Core.Log，不直接走 bridge

// ✅ Core.Log 打日志
Core.Log.Info("进入战斗");
```

---

## 15. AI 行为准则（AI Behavior Guidelines）

### 15.1 必须遵守
- 改任何代码前先读现有代码
- 保持代码风格一致（命名、缩进、注释）
- 改完代码告诉用户改了哪里、为什么改
- 不确定时先问用户，不要假设

### 15.2 禁止行为
- 不要替用户做重大决策（架构调整、删除模块）
- 不要修改用户明确说"先不要动"的代码
- 不要添加用户没要求的功能
- 不要删除用户代码（除非用户明确要求）
- 不要绕过硬约束

### 15.3 推荐行为
- 给代码时同时给注释解释"为什么"
- 复杂改动先列 plan（哪几个文件、每文件改什么）
- 提供测试方法（用户怎么验证）
- 主动指出潜在风险

---

## 16. 新增内容流程（Recipes）

> 前提：先读 **§3.15**（资产只能挂 AOT 类型 ✓）—— 这两份流程都是按它写的 ✓

### 16.1 新增敌人

**A. 只要一个新类型（推荐 ✓，以 `Orc1.prefab` 为模板，5 分钟）**

1. `Project` 里复制 `Assets/Art/Prefabs/Orc1.prefab`（Ctrl+D ✓）→ 改名 = 敌人名（如 `Vampires1` ✓）
2. 新建配置：`Assets/Art/Data/` 右键 → `Create → CryptWalker → Data → EnemyConfig` ✓ → 命名 `Enemy_Vampires1` ✓
   - `configID` **必须 = 文件名** ✓
   - **`enemyName` 必须 = 动画 clip 前缀** ✗✓（如 `Vampires1` ✓）—— 找错就**一动不动** ✓
   - 填 `maxHp` / `moveSpeed` / `attackRange` / `attackInterval` / `coinDrop` … ✓
3. 新 prefab 的 `EnemyProxy.config` → 拖成刚建的 `Enemy_Vampires1` ✓
4. 核对 prefab 四件套 ✓：
   - `Animator.Controller` → `Assets/Art/EnemyAnimator/{名字}Animation/{名字}.controller` ✓
   - `Rigidbody2D`：**Dynamic** ✓ / **Gravity = 0** ✓ / **Freeze Rotation Z** ✓
   - `BoxCollider2D` ✓ + 排序层 = `Character` ✓
5. 填进 `Assets/Art/Data/Levels/LevelConfig.asset` 的 `orc2Prefab` / `orc3Prefab` ✓
6. ⚠️ **`orc2/orc3` 目前刷怪代码没用到** ✗（`SpawnEnemies` 只读 `orc1Prefab` ✓）→
   想看到它：要么填到 `orc1Prefab`（替换 Orc1 ✓），要么改 `SpawnEnemies` 按房间随机挑 ✓

**B. 全新素材（图集还没处理过）**

按顺序跑 4 个菜单 ✓，再回到 A 的第 2~5 步 ✓：
```
Tools/CryptWalker/敌人图集自动切片          ← Assets/Art/Character/Enemy/{名字}/
Tools/CryptWalker/生成敌人动画Clip           ← 产出 Assets/Art/EnemyAnimator/{名字}Animation/*.anim
Tools/CryptWalker/生成敌人AnimatorController ← 产出 {名字}.controller
Tools/CryptWalker/修正敌人动画循环           ← 循环不自然的修补
```
（`Vampires1/2/3` 的 controller **已存在** ✓ → 用它们时 A 步里跳过第 4 步的换 controller ✓）

### 16.2 新增武器

数据在哪 ✓：数值 → `WeaponConfig`（AOT ✓）｜逻辑 → 热更的 `MeleeWeapon`/`RangedWeapon`/`SpellWeapon` ✓
｜桥接 → `player.prefab` 上的 `WeaponProxy` ✓

**A. 调现有的剑（不写代码 ✓）**
1. 改 `Assets/Art/Data/Weapon_Sword.asset` 的 `baseAttack` / `range` / `attackSpeed` / `gripOffset` ✓
2. 换贴图：跑 `Tools/CryptWalker/① 导出武器图（从 UI_Weapon 图集）` ✓（输出 PPU=16 的 png ✓ 并刷进 SO ✓）

**B. 换一把别的类型的武器（也不写代码 ✓）**
1. 新建 `WeaponConfig`（如 `Weapon_Bow.asset` ✓）→ `weaponType = Ranged` ✓ + `weaponSprite` ✓ + `gripOffset` ✓
2. 改 `player.prefab` 上 `WeaponProxy` 的两个字段 ✓：
   - `Config` → 新的 SO ✓
   - `Hot Type Full Name` → `CryptWalker.HotUpdate.Combat.RangedWeapon` ✓
     （`RangedWeapon` / `SpellWeapon` 代码已存在 ✓ → 换类型不需要写代码 ✓）

**C. 想同时带两把（切武器）** ✗ **目前做不到** ✓
`PlayerController` 是**单武器** ✓（注释里写着"第二档加 `Inventory[2]`"✓）；
一个物体上放两个 `WeaponProxy` 会互相打架 ✗（运行时 `GetComponent<WeaponBase>()` 顺序不确定 ✓）
→ 这属于**功能开发** ✓

### 16.3 改完必须做的事
1. 改了 prefab / **AOT 配置类字段** → 重跑 `CryptWalker/构建/①` ✓
2. 出包 → `CryptWalker/构建/②` ✓（它内部会自动先跑 ① ✓）
3. 真机复验 → `Player.log` 里 **0 条 `missing script`** ✓（见 §3.15 ✓）

> ⚠️ **已知失效工具** ✗：`Tools/CryptWalker/② 装配独立武器到 player.prefab`
> —— 它按热更类型找组件（`GetComponent<PlayerController>()` ✓），而 prefab 上现在只有 `PlayerProxy` ✓
> → 它只会打印「装不了」并**安全退出** ✓（不会破坏 prefab ✓），但也不再能装武器 ✓
> → 装武器请按 §16.2 手动改 `WeaponProxy` 的两个字段 ✓

---

**最后更新**：2026-10-14
**版本**：v1.0
**维护者**：罗庆烽