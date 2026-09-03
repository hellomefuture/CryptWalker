# CryptWalker · YooAsset 封装层（YooAssetService）

> 封装位置：`Assets/Main/Scripts/Service/YooAssetService.cs`
> 命名空间：`CryptWalker.Main.Service.YooAssetService`
> 依赖：`Assets/YooAsset/Runtime`（已手动安装）

---

## 一、设计原则

| 原则 | 体现 |
| :--- | :--- |
| 业务不直接碰 YooAsset | 所有方法只暴露 `string address` + `Task<T>`，不返回 YooAsset 类型 |
| 三个运行模式按需切 | `EPlayMode { EditorSimulate, Offline, Host }` |
| 句柄缓存统一管理 | 业务无需记句柄，本类负责 Release |
| 失败抛错给上游降级 | 初始化失败抛 `YooAssetInitException`，加载失败返回 null + LogError |

---

## 二、API 总览

| 方法 | 用途 | 返回 |
| :--- | :--- | :--- |
| `InitializeAsync()` | 默认 EditorSimulate 模式初始化 | `Task<bool>` |
| `InitializeAsync(EPlayMode)` | 显式指定模式初始化 | `Task<bool>` |
| `IsAvailable` / `CurrentMode` | 运行时状态查询 | `bool` / `EPlayMode` |
| `GetAsset<T>(address)` | 同步取已缓存资源 | `T` 或 null |
| `LoadAssetAsync<T>(address)` | 异步加载单个资源 | `Task<T>` |
| `LoadAllAssetsAsync<T>(loc)` | 加载整目录/标签下所有 | `Task<IReadOnlyList<T>>` |
| `LoadRawFileAsync(address)` | 加载原始字节(热更dll) | `Task<byte[]>` |
| `InstantiateAsync(address, parent)` | 加载并实例化预制体 | `Task<GameObject>` |
| `LoadSceneAsync(address, mode)` | 场景加载 | `Task<bool>` |
| `RequestPackageVersionAsync()` | 请求版本号(Host模式) | `Task<string>` |
| `UpdatePackageManifestAsync(v)` | 更新资源清单(Host模式) | `Task<bool>` |
| `DownloadAsync(onProgress)` | 下载差异资源(Host模式) | `Task<long>` |
| `ReleaseAsset(address)` | 释放单资源句柄 | `void` |
| `ClearAllCache()` | 清空所有缓存 | `void` |
| `Shutdown()` | 关闭YooAsset | `void` |

---

## 三、典型使用场景

### 1. 启动时初始化（MainLauncher）
```csharp
try {
    var ok = await YooAssetService.InitializeAsync(YooAssetService.EPlayMode.EditorSimulate);
    // 成功 → 业务可加载
} catch (YooAssetInitException ex) {
    // 降级：走本地内存/Resources
}
```

### 2. 加载数据 SO（热更 DataService）
```csharp
var weapon = await DataService.Instance.GetAsync<WeaponConfig>("weapon", "longsword_001");
// 内部会调用 YooAssetService.LoadAssetAsync<WeaponConfig>(address)
```

### 3. 实例化敌人
```csharp
var go = await YooAssetService.InstantiateAsync("asset://prefabs/enemy/skeleton_archer", parent);
// 注意：实例化不归本类缓存，每次返回新 GameObject
```

### 4. 联网热更（真机 Host 模式）
```csharp
await YooAssetService.InitializeAsync(YooAssetService.EPlayMode.Host);
string ver = await YooAssetService.RequestPackageVersionAsync();
bool ok = await YooAssetService.UpdatePackageManifestAsync(ver);
long bytes = await YooAssetService.DownloadAsync((cur, total, p) => {
    Debug.Log($"下载进度 {p:P0}");
});
```

---

## 四、Address 命名规范

| 资源类型 | 模板 | 示例 |
| :--- | :--- | :--- |
| SO 数据 | `asset://data/{typeFolder}/{id}` | `asset://data/weapon/longsword_001` |
| 预制体 | `asset://prefabs/{category}/{name}` | `asset://prefabs/enemy/skeleton_archer` |
| 图集 | `asset://atlas/{name}` | `asset://atlas/ui_common` |
| 场景 | `asset://scene/{name}` | `asset://scene/main_menu` |
| 原始文件(热更dll) | `asset://raw/{name}` | `asset://raw/CryptWalker.HotUpdate.dll` |

> 桥接层 `DataService.MakeAddress` 已经按 `asset://data/{typeFolder}/{id}` 写死，业务直接用 `GetAsync<T>(typeFolder, id)` 即可。

---

## 五、注意事项

1. **句柄缓存 key 必须是 address**——业务不要传不同 alias 重复加载同一资源。
2. **预制体 Instantiate 不缓存**——多次 `InstantiateAsync` 拿多个实例；想缓存整个预制体用 `LoadAssetAsync<GameObject>`。
3. **YooAsset 不支持热释放重命名**——重命名资源后必须重新打包。
4. **EditorSimulate 模式需要先跑一次"模拟构建"**（菜单 `YooAsset → AssetBundle Builder`），否则清单文件不存在会初始化失败。
5. **联网 Host 模式前请先填 `GetRemoteServerURL()` 里的 CDN 地址**。
6. **YooAsset 与 AssetsBundle 互斥**——别再用 `Resources.Load`，本类已替代。

---

## 六、编辑器侧第一次跑通

1. 打开 `YooAsset → AssetBundle Builder`（如果菜单没出来，看 `Assets/YooAsset/Editor` 是否有 EditorOnly dll，**注意：YooAsset 装进 Assets 不需要 Player Settings 额外配**，Unity 会自动识别 asmdef 里的 EditorOnly 范围）。
2. 选 `EditorSimulateMode` → Build → 输出到 `Library/YooAssetSimulate`。
3. Play，看 Console：`[YooAssetService] 初始化完成 mode=EditorSimulate`。
4. 业务层调用 `YooAssetService.LoadAssetAsync<T>` 即可。

---

## 七、与业务系统的对接

- **DataService**（已写好）：`GetAsync<T>(typeFolder, id)` → 自动拼 `asset://data/.../id` → 调本服务
- **MainLauncher**：调 `InitializeAsync(EPlayMode)` 启动
- **以后 Combat/Entity/Pool 等系统**：直接 `LoadAssetAsync<GameObject>(prefabAddress)` / `InstantiateAsync`
- **场景切换**：`LoadSceneAsync("asset://scene/...")`
