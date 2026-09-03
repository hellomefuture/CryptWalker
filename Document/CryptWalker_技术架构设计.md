# CryptWalker 技术框架架构设计（草案 v0.1）

> 技术栈：**YooAsset**（资源/热更下载）+ **HybridCLR / 华佗**（C# 代码热更）+ **SO 数据驱动**（ScriptableObject + JSON）
> 状态：**待用户确认**。未确认前不写实现代码。

---

## 0. 术语与前提确认

| 前提 | 说明 | 需确认 |
| :--- | :--- | :--- |
| 华佗热更 | 按 **HybridCLR（原 huatuo/华佗）** 理解：IL2CPP 下加载热更管程集 | ⚠️ 请确认 |
| YooAsset 版本 | 社区开源版（git Package），或商业版 | ⚠️ 需确认仓库来源 |
| 更新流程 | 正式接入 Web 版校验+下载？还是先本地 AssetBundle 跑通再接入 | ⚠️ 需确认 |
| 数据载体 | 纯 SO，还是 SO + 导出 JSON 双轨 | ⏳ 默认：SO 为主 + 可导出 JSON |
| 目标平台 | PC 主开发；WebGL / 微信 需注意 AB 与 .NET 差异 | ⚠️ 需确认首批平台 |

> 本设计按 **非商业化毕设 + 未来上线** 的合理默认展开，并留出升级路径。

---

## 1. 整体分层架构

三层核心 + 若干业务层，**最核心的一条纪律：主工程（AOT）只放「不会变、必须提前编译」的代码；一切会随版本更新的游戏逻辑全部进热更程序集。**

```
┌─────────────────────────────────────────────────────────┐
│ ① 主工程 (AOT / IL2CPP)  ← 冷区：稳定、承载热更能力       │
│   ├─ App Entry / Bootstrap           启动与生命周期        │
│   ├─ YooAssetService                 资源下载/加载封装      │
│   ├─ HotUpdateService                HybridCLR 加载热更     │
│   ├─ Framework (稳定基础设施)        日志/单例/GC工具/事件   │
│   └─ 启动壳 & 极少量 AOT 稳定逻辑                          │
├─────────────────────────────────────────────────────────┤
│ ② 热更程序集 HotUpdate.dll  ← 热区：全部游戏逻辑在这里      │
│   ├─ DataService                     SO/JSON 数据加载&缓存   │
│   ├─ GameFlow                        Boot→菜单→战斗→结算流程 FSM│
│   ├─ Entity / Combat / Roguelike / Buff / Inventory / Pool│
│   ├─ UISystem                        UIManager + Binder     │
│   └─ ManagerHub                       根入口/依赖注入         │
├─────────────────────────────────────────────────────────┤
│ ③ 数据资源层 (由 YooAsset 下发热更)                        │
│   ├─ SO 数据资产 (Weapon/Enemy/Buff/Relic/Trait...)        │
│   ├─ 美术资源 / 预制体 / 图集 / 场景                         │
│   └─ 可选的 JSON 导出（工具链/分析）                         │
└─────────────────────────────────────────────────────────┘
```

**为什么这样切？**
- 主工程是 IL2CPP AOT 编译的，**编译后不可改**，一旦被 *mis-loaded* 无法热更。
- 热更程序集由 HybridCLR 加载执行，**可热更**，承载业务最合适。
- SO 数据作为**资源**进 AssetBundle，走 YooAsset 增量下发 → 数据也能热更，不依赖改代码。

---

## 2. 程序集 / 命名空间划分

| 程序集 | 性质 | 用途 | 命名空间 |
| :--- | :--- | :--- | :--- |
| `CryptWalker.Main` | AOT（主工程） | 启动、YooAsset/HybridCLR 封装、稳定基建 | `CryptWalker.Main` |
| `CryptWalker.HotUpdate` | 热更 | 全部游戏逻辑 | `CryptWalker.HotUpdate.*` |

**跨程序集隔离纪律（重要）**：
- 热更程序集 → 主工程：**只能通过接口 / 约定**（如 `IAppBridge`），不允许热更代码直接 new 主工程业务类（避免主工程膨胀、失去热更意义）。
- 主工程 → 热更程序集：只在启动时装载 + 反射调入口，业务不反向依赖。

---

## 3. 模块职责设计

### 3.1 主工程模块（AOT）
| 模块 | 职责 | 关键类 |
| :--- | :--- | :--- |
| 启动入口 | 游戏最开始的挂载点 | `Boot`, `MainLauncher` |
| 资源服务 | YooAsset 初始化、包管理、更新、加载 | `YooAssetService` |
| 热更服务 | HybridCLR 启停、程序集加载、入口调用 | `HybridCLRHelper` |
| 稳定基建 | 日志/单例/EventBus/GC 工具 | `Log`, `Singleton<T>`, `GlobalEventBus` |

### 3.2 热更程序集模块（HotUpdate）
| 模块 | 职责 |
| :--- | :--- |
| `DataService` | SO/JSON 数据统一加载、缓存、按 ID 查询（数据驱动核心） |
| `GameFlow` | 主流程状态机：Boot→MainMenu→Game→Settle→Restart |
| `CombatSystem` | 伤害计算 Modifier 管道、元素反应、武器/弹道 |
| `EntitySystem` | 玩家控制器、敌人 FSM AI |
| `RoguelikeSystem` | 房间两级生成、关卡流程、门切换 |
| `BuffSystem` | Buff 三选一、叠加、边际递减 |
| `InventorySystem` | 双武器槽、词条、遗物、消耗道具 |
| `PoolSystem` | 对象池 |
| `UISystem` | UIManager、Binder、HUD |

---

## 4. 启动初始化时序（Boot 流程）

```
[主工程] MainLauncher.OnSceneLoaded
   ├─ ① Log / Singleton / EventBus 初始化
   ├─ ② YooAssetService.Init()        → 初始化 Package, 读取版本
   │     ├─ 检查远端更新 (正式 env 开启)
   │     └─ 下载/解压 热更 bundle + 资源包 (YooAsset)
   ├─ ③ HybridCLRHelper.Install()     → 加载补充元数据 (AOT dlls)
   ├─ ④ 加载 hotupdate.dll + 依赖dll  (来自 YooAsset 下载的 bundle)
   ├─ ⑤ 通过反射调 HotUpdate 入口:  CryptWalker.HotUpdate.GameRoot
   │     └─ 静态方法 Run( IAppBridge bridge )
   ├─ ⑥ GameRoot 初始化 DataService / 各系统
   └─ ⑦ 进入 GameFlow → 加载主菜单场景 + Map
```

**桥接设计（Main↔HotUpdate）**：
```csharp
// 主工程 (CryptWalker.Main)
public interface IAppBridge {
    IYooAssetService Asset { get; }
    System.Action OnQuit { set; }
    string PersistentDataPath { get; }
    void Log(LogLevel lv, string msg);
}
// 热更入口 (CryptWalker.HotUpdate)
public static class GameRoot {
    public static void Run(IAppBridge bridge) { /* 初始化各系统 */ }
}
```

---

## 5. YooAsset 资源工作流

| 阶段 | 设计 |
| :--- | :--- |
| 初始化 | `YooAssets.Initialize()` → 创建 `DefaultPackage` |
| 版本校验 | `RequestPackageVersionAsync()`（正式 env 开，测试关） |
| 更新 | `UpdatePackageManifestAsync()` + `Downloader` 下载差异资源 |
| 热更 dll | 热更 bundle 走同一下载管线，解压到 `PersistentDataPath` |
| 加载 | `package.LoadAssetAsync<T>(address)` / `Instantiate`（预制体） |
| Address 约定 | `asset://{Module}/{Type}/{Name}`，如 `asset://data/weapon/longsword_001` |
| 缓存 | 常用 SO/预制体按需缓存，减少重复加载 |

---

## 6. HybridCLR(华佗) 热更流程

```
① Installer 准备   → 将热更 dll 预载到内存
② HybCLR.Init()    → 初始化运行时
③ LoadRuntimeMetadata  → 注册 AOT 补充元数据(主工程 dlls)
④ 加载热更程序集    → Assembly.Load / LoadFrom
⑤ 调入口           → 反射 GameRoot.Run(bridge)
```
> 注意点：IL2CPP 下 **AOT 泛型实例化需补充**（`System.Reflection.Emit` 不支持），需在 `HybridCLRSettings` 配置排除/裁剪清单；毕设阶段先保证 `PC` 跑通。

---

## 7. SO 数据驱动设计（结合 JSON）

**设计目标**：策划用 SO 在编辑器可视化配置，运行时统一走数据服务，数据可随热更包更新、不依赖改代码。

### 7.1 数据形态（SO 为主，可导出 JSON）
```csharp
// 热更程序集里定义 (CryptWalker.HotUpdate.Data)
[CreateAssetMenu(fileName="Weapon_xxx", menuName="CryptWalker/Weapon")]
public class WeaponConfig : ScriptableObject {
    public string weaponID;          // 全局唯一, 作 SO 文件名与 Address
    public WeaponType weaponType;
    public float baseAttack;
    public float attackSpeed;
    public float range;
    public float specialSkillCD;
    public EElementType elementType;
    public int elementDamage;
    public List<TraitEntry> traits;  // 词条
    // ...
}
[Serializable] public class TraitEntry {
    public string traitID; public float value;
}
```

### 7.2 数据层职责
| 能力 | 实现 |
| :--- | :--- |
| 统一入口 | `DataService.Get<T>(address)` / `GetAll<T>()`（按 SO 基类类型索引） |
| 缓存 | 字典按 ID 缓存，避免重复加载 |
| 数据热更 | DataPreload 通过 YooAsset 加载数据 bundle |
| 双轨 | 编辑器读 SO；运行时可选读 SO 或反序列化导出 JSON（版本兼容） |

### 7.3 数据→资源流程
```
编辑器配置 SO (Weapon/Enemy/Buff/Relic/Trait...)
        │  build
创建数据 bundle (含所有 SO 资产)
        │  YooAsset
下发/更新数据 bundle
        │  运行时
DataService 加载 → 缓存 → 业务查询
```

---

## 8. 目录结构（主工程 + 热更分账）

```
CryptWalker/
├─ Assets/
│  ├─ Main/                       # AOT 主工程
│  │  ├─ Scripts/
│  │  │  ├─ Boot/                 # 启动入口
│  │  │  ├─ Service/              # YooAssetService, HybridCLRHelper
│  │  │  └─ Framework/            # Log, Singleton, EventBus ...
│  ├─ HotUpdate/                  # 热更程序集 (HybridCLR)
│  │  ├─ Scripts/
│  │  │  ├─ Data/                 # SO 定义 + DataService
│  │  │  ├─ GameFlow/             # 流程状态机
│  │  │  ├─ Combat/  Entity/  Roguelike/  Buff/  Inventory/  Pool/
│  │  │  └─ UI/
│  │  └─ ...（热更 dll 生成物见下）
│  ├─ Data/                       # SO 资产 (Weapon/Enemy/Buff/...) → 数据 bundle
│  ├─ Art/  Prefabs/  Scenes/  Resources/
│  └─ YooAsset/                   # 包配置、构建输出
├─ Packages/                      # YooAsset(或其 git) + HybridCLR uninstall/package
└─ Document/
```

---

## 9. 遵循既有策划案定稿（冲突时以本设计实现方式为准）

- 元素印记（上限2/顶替规则/反应独立结算）——见 `CombatSystem/ElementReaction`
- 两级关卡生成 + BFS 连通——`RoguelikeSystem/RoomGen`
- Modifier 聚合伤害管道——`CombatSystem/DamageCalc`
- FSM `EnemyState<T>` 对象化 + JSON 配置化——`EntitySystem/FSM`
- 对象池防膨胀（超上限 Destory）——`PoolSystem`
- 俯视角渲染/遮挡——`Main` 侧渲染适配 + 热更侧逻辑

---

## 10. 待你确认的关键决策（拍板后我再动代码）

1. **华佗 = HybridCLR** 是否正确？
2. **YooAsset 版本/来源**：git 社区版最新，还是指定商用版？
3. **首批平台**：先 PC 跑通，还是需要尽早兼容 WebGL/微信（影响 AB 与 update 流程取舍）？
4. **更新流程阶段**：先本地 AssetBundle（关闭在线校验）把玩法跑通，后接正式下载校验？
5. **数据双轨**：纯 SO，还是 SO + 导出 JSON？毕设建议 SO 为主即可。
6. **程序集方案**：接受「主工程 AOT 极薄 + 热更承载全部业务」的切分吗？（推荐）

> 你只需回答这 6 点（或只答不同意的点），我据此把方案定稿，再开始搭代码。