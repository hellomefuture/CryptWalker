# CryptWalker · YooAsset + HybridCLR(华佗) 接入手册

> 目标：让「主工程 AOT 极薄 + 热更程序集承载业务 + SO 数据驱动走 YooAsset」这条链路在 PC 上先跑通。
> 适用 Unity 2022.3 LTS + URP 2D。

---

## 1. 依赖包接入

`Packages/manifest.json` 已预置两个 git 依赖：

```json
"com.tuyoogame.yooasset": "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset",
"com.code-philosophy.hybridclr": "https://github.com/focus-creative-games/hybridclr_unity.git",
```

**操作**：回到 Unity，让它自动拉取。若企拉取失败或想换方式：

- **YooAsset（OpenUPM 备用）**：打开 `Window → Package Manager → + → Add package by name`，填 `com.tuyoogame.yooasset`。
- **HybridCLR（GUI 备用）**：`Window → Package Manager → + → Add package from git URL`，粘贴 `https://github.com/focus-creative-games/hybridclr_unity.git`。

> 提示：国内网络拉 GitHub 可能慢/失败。如失败，方案二选一：开代理，或用 OpenUPM（推荐 `Add package by name`）。若仍不通，可手动下载 zip 后解压进 `Packages/`（YooAsset 移至 `Assets/Plugins`、HybridCLR 按其 README 放置）。

---

## 2. HybridCLR(华佗) 初始化（一次性）

装好包、能编译后，执行：

1. 菜单 `HybridCLR → Installer…` → 点 **Install**（安装 IL2CPP 补丁，会重启 Unity）。
2. 菜单 `HybridCLR → Generate → All`（生成链接器裁剪 + AOT 补充元数据）。
3. 若改过代码或新增 AOT 引用，**每次 Release 前重跑 Generate/All**。
4. 真机构建时用 `HybridCLR → Build` 产出热更 dll 到 `BuildOutput/`。

> `MainLauncher.cs` 里的 `_aotAssemblyNames` 会读取 `HybridCLRData/AssembliesPostIl2CppPatch/` 下的补充元数据 dll。名字要对得上 Generate 的输出，缺哪个就补到数组。

---

## 3. YooAsset 资源构建与热更 dll 供给

**开发期（本阶段够用）**：热更 dll 走本地构建产物，见 `HotUpdateProvider`：
- 读取优先级：`BuildOutput/HotUpdate/.../CryptWalker.HotUpdate.dll` → `Library/ScriptAssemblies/`。
- 只要热更程序集编译通过，即可直接跑通热更链路，无需先做完 YooAsset 构建。

**正式接入（真机/正式环境）**：把 `CryptWalker.HotUpdate.dll` 及 AOT 补充元数据打入 YooAsset bundle，运行前下载、解压到 `PersistentDataPath`，再由 `HybridCLRHelper` 加载。

> 骨架当前：`MainLauncher` 先初始化 YooAsset（失败不阻断），随后走本地 dll 启动热更。把「dll 来源」从本地换成 YooAsset 下载，是后续第 6 步。

---

## 4. 跑通验证

1. 把 `MainLauncher` 挂到启动场景（或启动场景的一个 GameObject）上。
2. Play：Console 应依次打印：
   - `[Main] CryptWalker 启动中 (AOT)`
   - `[HotUpdate] GameRoot 启动`
   - `[GameFlow] 进入 Boot 状态…`
3. 出现以上三行 = **热更链路已完整打通**（AOT 主工程 → 华佗加载热更程序集 → 业务入口执行）。

---

## 5. 已搭骨架清单（本轮交付）

```
Assets/
├─ Main/                         # AOT 主工程
│  ├─ CryptWalker.Main.Runtime.asmdef
│  └─ Scripts/
│     ├─ Boot/MainLauncher.cs        启动入口，串起热更链路
│     ├─ Bridge/IAppBridge.cs        热更→主工程 唯一通道
│     ├─ Bridge/AppBridge.cs         桥接实现
│     ├─ Framework/SingletonBehaviour.cs
│     └─ Service/
│        ├─ HotUpdateProvider.cs     dll 字节来源(本地/正式)
│        ├─ HybridCLRHelper.cs       元数据注册+程序集加载
│        └─ YooAssetService.cs       资源服务封装
└─ HotUpdate/                    # 热更程序集
   ├─ CryptWalker.HotUpdate.asmdef
   └─ Scripts/
      ├─ GameRoot.cs                热更入口 Run(bridge)
      ├─ Data/DataService.cs        SO 数据加载/缓存
      └─ GameFlow/GameFlowMachine.cs 主流程状态机(骨架)
```

---

## 6. 已知待办（后续迭代）

- [ ] 热更 dll 改为经 YooAsset 下载加载（正式链路）。
- [ ] `DataService` 接上真实验证：在 YooAsset 数据 bundle 放一个 `WeaponConfig` SO，跑通「策划配置 → 运行时读取」。
- [ ] 挂接策划案业务：Combat/Entity/Roguelike/Buff/Inventory/Pool/UI 各系统。
- [ ] AOT 泛型裁剪清单按业务逐步补齐（`HybridCLR Generate` 时会提示缺失泛型，往 `aotAssemblyNames` / link 配置里加）。

---

## 7. 常见坑

| 现象 | 原因 | 处理 |
| :--- | :--- | :--- |
| 找不到 `CryptWalker.HotUpdate.dll` | GCObject 还没执行 HybridCLR Build | 开发期读 `Library/ScriptAssemblies`，先确认热更 asmdef 编译通过 |
| AOT 元数据加载返回非0 | Generate 未执行或 assembly 名单不全 | 重跑 `HybridCLR → Generate → All`，补齐 `_aotAssemblyNames` |
| YooAsset API 编译报错 | 装的版本与骨架 API 有出入（社区版 2.x） | 对照你装的版本调整 `YooAssetService` 调用，或告知我版本号我来改 |
| 泛型方法 ExecuteWithException/AOT 报错 | IL2CPP 裁剪了泛型 | 在 `aotAssemblyNames` 加对应程序集，重跑 Generate |