# YooAsset 集成使用示例

YooAsset 集成把 YooAsset 包资源适配成框架统一的 `IAssetService`，并提供基于 YooAsset 版本清单和下载器的 `YooAssetResourceUpdateService`。框架现在只保留 YooAsset 资源后端，业务层继续依赖 `Frame.Assets.IAssetService`，资源更新直接使用 `Frame.YooAsset` 下的具体服务。

## 前置条件

- 项目已安装 YooAsset 包。
- 保留 `Assets/Frame/Integrations/YooAsset/Frame.YooAsset.asmdef`。
- `FrameSettings.EnableAssetService = true` 时，`YooAssetModuleInstaller` 会自动注册 `YooAssetAssetService`。
- `FrameSettings.EnableResourceUpdateService = true` 时，`YooAssetModuleInstaller` 会自动注册 `YooAssetResourceUpdateService`。

## 命名空间

业务代码：

```csharp
using Frame.Assets;
using Frame.Core;
using Frame.YooAsset;
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
- `YooAssetDownloadMaxConcurrency`: 默认下载并发数。
- `YooAssetDownloadMaxRequestPerFrame`: YooAsset 下载器每帧请求数。
- `YooAssetDownloadWatchdogTimeout`: YooAsset 下载看门狗超时。

资源更新相关开关：

- `EnableAssetService`: 启用 `YooAssetAssetService`。
- `EnableResourceUpdateService`: 启用 `YooAssetResourceUpdateService`。

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

这里的路径是 YooAsset location，不会按 Resources 规则截取目录或移除扩展名。

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

## 资源更新服务

`YooAssetResourceUpdateService` 用于 Host/Web 模式下检查远端版本、加载远端清单、创建下载器、下载补丁资源和清理缓存。

```csharp
YooAssetResourceUpdateService updater = Framework.Resolve<YooAssetResourceUpdateService>();

YooAssetResourceUpdateOperation operation = updater.CheckForUpdates(new YooAssetResourceUpdateOptions
{
    PackageName = "DefaultPackage",
    TimeoutSeconds = 60
}, result =>
{
    if (!result.Success)
    {
        Debug.LogWarning(result.Error);
        return;
    }

    Debug.Log("Remote version: " + result.RemoteVersion);
});
```

下载更新：

```csharp
YooAssetResourceUpdateOperation operation = updater.Update(new YooAssetResourceUpdateOptions
{
    PackageName = "DefaultPackage",
    DownloadMaxConcurrency = 5,
    DownloadRetryCount = 3,
    ClearUnusedCacheAfterUpdate = true
});

while (!operation.IsDone)
{
    progressBar.value = operation.Progress;
    progressText.text = operation.CurrentDownloadBytes + " / " + operation.TotalDownloadBytes;
    await Cysharp.Threading.Tasks.UniTask.Yield();
}

if (!operation.Success)
{
    Debug.LogWarning(operation.Error);
}
```

按 tag 下载：

```csharp
updater.Update(new YooAssetResourceUpdateOptions
{
    Tags = new[] { "ui", "audio" }
});
```

取消下载：

```csharp
operation.Cancel();
```

清理缓存：

```csharp
updater.ClearCache(new YooAssetResourceUpdateOptions
{
    PackageName = "DefaultPackage"
});
```

`CheckForUpdates` 只检查远端版本和是否需要更新，不下载资源。`Update` 会请求版本、加载清单并创建 YooAsset downloader。`ClearCache` 默认清理未使用 bundle；传入 `Tags` 时按 tag 清理。

在 `EditorSimulate` 和 `Offline` 模式下，资源更新不支持远端版本检查，结果会以 `Success = true`、`IsSupported = false` 完成，业务层可以直接跳过热更新流程。

## PlayMode 说明

```csharp
YooAssetPlayMode.EditorSimulate
YooAssetPlayMode.Offline
YooAssetPlayMode.Host
YooAssetPlayMode.Web
```

- `EditorSimulate`: 编辑器模拟模式，仅编辑器可用；非编辑器会回退到 Offline。
- `Offline`: 使用内置文件系统。
- `Host`: 内置文件系统 + 沙盒缓存文件系统 + 远端下载地址。
- `Web`: WebServer 文件系统 + WebNetwork 文件系统。

Host/Web 模式的远端 URL 由 `YooAssetDefaultHostServer` 和 `YooAssetFallbackHostServer` 拼接文件名得到。

## 手动注册

通常不需要手动注册。测试或自定义宿主可直接添加：

```csharp
ModuleManager modules = new ModuleManager();
modules.Add(new YooAssetAssetService());
modules.Add(new YooAssetResourceUpdateService());
```

或使用 installer：

```csharp
new YooAssetModuleInstaller().Install(modules, settings);
```

Installer 会在启用 AssetService 时添加 `YooAssetAssetService`，在启用 ResourceUpdateService 时添加 `YooAssetResourceUpdateService`。

## 初始化行为

`YooAssetAssetService` 初始化时会：

1. 确保 `YooAssets.Initialize()` 已执行。
2. 获取或创建配置的 package。
3. 根据 `YooAssetPlayMode` 创建初始化选项。
4. 同步等待 `InitializePackageAsync` 完成。

`YooAssetResourceUpdateService` 初始化时只注册服务，不创建 package。它依赖 `YooAssetAssetService` 已经完成 YooAsset 和 package 初始化；如果 package 不存在或初始化失败，检查/更新操作会返回失败结果。

## 注意事项

- YooAsset 包资源的 location 要和构建管线配置保持一致。
- Host/Web 模式需要先完成 YooAsset 包构建、上传和版本/清单发布流程。
- `UnloadUnusedAssets()` 会先调用 YooAsset package 的卸载操作，再调用 Unity `Resources.UnloadUnusedAssets()`。
- 模块关闭时，如果服务自己创建了 package，会尝试销毁并从 YooAssets 移除。
- 每次成功加载都要释放句柄，避免 bundle 长时间被引用。
