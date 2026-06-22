# YooAsset 集成使用示例

YooAsset 集成把 YooAsset 包资源适配成框架统一的 `IAssetService`。业务层继续使用 `Frame.Assets.IAssetService`，由 `FrameSettings` 决定后端。

## 前置条件

- 项目已安装 YooAsset 包。
- 保留 `Assets/Frame/Integrations/YooAsset/Frame.YooAsset.asmdef`。
- `FrameSettings.EnableAssetService = true`。
- `FrameSettings.AssetServiceBackend = AssetServiceBackend.YooAsset`。

`YooAssetModuleInstaller` 会在框架初始化时自动注册 `YooAssetAssetService`。

## 命名空间

业务代码：

```csharp
using Frame.Assets;
using Frame.Core;
using UnityEngine;
```

手动访问集成实现：

```csharp
using Frame.YooAsset;
```

## FrameSettings 配置

常用 YooAsset 字段：

- `YooAssetPackageName`: 包名，默认 `DefaultPackage`。
- `YooAssetPlayMode`: `EditorSimulate`、`Offline`、`Host`、`Web`。
- `YooAssetEditorPackageRoot`: EditorSimulate 文件系统根。
- `YooAssetBuiltinPackageRoot`: 内置包根。
- `YooAssetDefaultHostServer`: 默认远端地址。
- `YooAssetFallbackHostServer`: 备用远端地址。
- `YooAssetDownloadMaxConcurrency`
- `YooAssetDownloadMaxRequestPerFrame`
- `YooAssetDownloadWatchdogTimeout`

## 加载资源

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

这里的路径是 YooAsset location，不会按 Resources 规则截取。

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

协程：

```csharp
yield return request;

if (request.Success)
{
    icon.sprite = request.Asset;
    request.Handle.Release();
}
else
{
    Debug.LogWarning(request.Error);
}
```

取消：

```csharp
request.Cancel();
```

取消时底层 YooAsset handle 会被释放。

## 实例化 Prefab

```csharp
GameObject panel = assets.Instantiate("UI/MainMenu", parent);
```

实例会挂 `AssetInstanceLease`，销毁实例时自动释放资源句柄。

## 查询和释放

```csharp
bool loaded = assets.IsLoaded("UI/MainMenu");
int refs = assets.GetReferenceCount("UI/MainMenu");

List<AssetStats> stats = assets.GetLoadedAssetStats();

assets.Release("UI/MainMenu");
assets.ReleaseAll();
assets.UnloadUnusedAssets();
```

引用计数归零时，服务会释放 YooAsset `AssetHandle`。

## PlayMode 说明

```csharp
YooAssetPlayMode.EditorSimulate
YooAssetPlayMode.Offline
YooAssetPlayMode.Host
YooAssetPlayMode.Web
```

- `EditorSimulate`: 编辑器模拟模式，仅编辑器可用，非编辑器会回退到 Offline。
- `Offline`: 使用内置文件系统。
- `Host`: 内置文件系统 + 沙盒缓存文件系统 + 远端下载地址。
- `Web`: WebServer 文件系统 + WebNetwork 文件系统。

Host/Web 模式的远端 URL 由 `YooAssetDefaultHostServer` 和 `YooAssetFallbackHostServer` 拼接文件名得到。

## 手动注册

通常不需要手动注册。测试或自定义宿主可直接添加：

```csharp
ModuleManager modules = new ModuleManager();
modules.Add(new YooAssetAssetService());
```

或使用 Installer：

```csharp
new YooAssetModuleInstaller().Install(modules, settings);
```

Installer 只有在 `AssetServiceBackend.YooAsset` 时才添加模块。

## 初始化行为

`YooAssetAssetService` 初始化时会：

1. 确保 `YooAssets.Initialize()` 已执行。
2. 获取或创建配置的 package。
3. 根据 `YooAssetPlayMode` 创建初始化选项。
4. 同步等待 `InitializePackageAsync` 完成。

如果初始化失败，加载会返回无效句柄，并写入 warning。

## 注意事项

- YooAsset 包资源的 location 要和构建管线配置保持一致。
- 远端模式需要先完成 YooAsset 包构建、上传和版本/清单流程。
- `UnloadUnusedAssets()` 会先调用 YooAsset package 的卸载操作，再调用 Unity `Resources.UnloadUnusedAssets()`。
- 模块关闭时，如果服务自己创建了 package，会尝试销毁并从 YooAssets 移除。
- 每次成功加载都要释放句柄，避免 bundle 长时间被引用。
