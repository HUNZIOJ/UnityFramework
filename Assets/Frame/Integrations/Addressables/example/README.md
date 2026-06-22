# Addressables 集成使用示例

Addressables 集成把 Unity Addressables 适配成框架统一的 `IAssetService`。业务层继续依赖 `Frame.Assets.IAssetService`，不需要直接调用 Addressables API。

## 前置条件

- 安装 Unity Addressables 包。
- 保留 `Assets/Frame/Integrations/Addressables/Frame.Addressables.asmdef`。
- `FrameSettings.EnableAssetService = true`。
- `FrameSettings.AssetServiceBackend = AssetServiceBackend.Addressables`。

`AddressablesAssetModuleInstaller` 会在框架初始化时自动注册 `AddressablesAssetService`。

## 命名空间

业务代码通常只需要：

```csharp
using Frame.Assets;
using Frame.Core;
using UnityEngine;
```

如果手动创建集成服务才需要：

```csharp
using Frame.Addressables;
```

## 启用后加载资源

```csharp
IAssetService assets = Framework.Resolve<IAssetService>();

using (AssetHandle<GameObject> handle = assets.Load<GameObject>("UI/MainMenu"))
{
    if (handle.IsValid)
    {
        GameObject prefab = handle.Asset;
    }
}
```

这里的路径是 Addressables address，不会按 Resources 规则去掉扩展名或截取 `Resources/`。

## 异步加载

```csharp
AssetRequest<Sprite> request = assets.LoadAsync<Sprite>("Icons/Coin", handle =>
{
    if (handle.IsValid)
    {
        icon.sprite = handle.Asset;
        handle.Release();
    }
});
```

协程等待：

```csharp
yield return request;

if (request.Success)
{
    icon.sprite = request.Asset;
    request.Handle.Release();
}
```

取消：

```csharp
request.Cancel();
```

取消时底层 Addressables operation 会被释放。

## 实例化 Prefab

```csharp
GameObject panel = assets.Instantiate("UI/MainMenu", parent);
```

实例会挂 `AssetInstanceLease`，销毁实例时自动释放加载句柄。

## 查询和释放

```csharp
bool loaded = assets.IsLoaded("UI/MainMenu");
int refs = assets.GetReferenceCount("UI/MainMenu");

List<AssetStats> stats = assets.GetLoadedAssetStats();

assets.Release("UI/MainMenu");
assets.ReleaseAll();
assets.UnloadUnusedAssets();
```

当引用计数归零时，服务会调用 `Addressables.Release(handle)`。

## 手动安装示例

通常不需要手动安装。如果自定义 `ModuleManager`，可直接添加服务：

```csharp
ModuleManager modules = new ModuleManager();
modules.Add(new AddressablesAssetService());
```

或者使用 Installer：

```csharp
new AddressablesAssetModuleInstaller().Install(modules, settings);
```

Installer 只有在 `AssetServiceBackend.Addressables` 时才添加模块。

## 地址规范建议

- 给 prefab、sprite、text asset 设置稳定 address，例如 `UI/MainMenu`、`Icons/Coin`。
- 业务代码不要混用 Resources path 和 Addressables address。
- 如果从 Resources 后端迁移到 Addressables，保留同名 address 可以减少业务改动。

## 注意事项

- `AddressablesAssetService` 初始化时会同步调用 `Addressables.InitializeAsync().WaitForCompletion()`。
- `Load<T>` 同步加载会阻塞，较大资源优先使用 `LoadAsync`。
- 每次成功加载都会增加引用计数，使用完必须释放。
- `ReleaseAll()` 会释放所有缓存的 Addressables handles，之后已有 asset 引用不再由服务保证生命周期。
