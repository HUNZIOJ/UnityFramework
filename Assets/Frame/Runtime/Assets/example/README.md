# Assets 模块使用示例

Assets 模块通过 `IAssetService` 统一封装资源加载、异步请求、实例化、引用计数和加载统计。默认实现是 `ResourcesAssetService`，也可以通过集成模块切换到 Addressables 或 YooAsset。

## 命名空间

```csharp
using Frame.Assets;
using Frame.Core;
using UnityEngine;
```

## 获取服务

```csharp
IAssetService assets = Framework.Resolve<IAssetService>();
```

## Resources 路径规则

默认后端使用 Unity `Resources`。路径不带扩展名，不包含 `Resources/` 之前的目录。

这些路径会被标准化成同一个 key：

```text
Assets/Game/Resources/UI/MainMenu.prefab -> UI/MainMenu
Resources\Configs/player.json           -> Configs/player
UI/MainMenu.prefab                       -> UI/MainMenu
```

## 同步加载

```csharp
using (AssetHandle<TextAsset> handle = assets.Load<TextAsset>("Configs/player"))
{
    if (!handle.IsValid)
    {
        return;
    }

    string json = handle.Asset.text;
    Debug.Log(json);
}
```

`AssetHandle<T>` 实现了 `IDisposable`。`Dispose()` 和 `Release()` 都会减少引用计数。

## TryLoad

不希望缺失资源打印 warning 时使用 `TryLoad`：

```csharp
if (assets.TryLoad<Sprite>("Icons/Coin", out AssetHandle<Sprite> handle))
{
    try
    {
        iconImage.sprite = handle.Asset;
    }
    finally
    {
        handle.Release();
    }
}
```

## 异步加载回调

```csharp
AssetRequest<GameObject> request = assets.LoadAsync<GameObject>(
    "UI/ShopPanel",
    handle =>
    {
        if (handle != null && handle.IsValid)
        {
            GameObject prefab = handle.Asset;
            Debug.Log(prefab.name);
            handle.Release();
        }
    });
```

`AssetRequest<T>` 可读取：

- `IsDone`
- `IsCanceled`
- `Success`
- `Progress`
- `Error`
- `Handle`
- `Asset`

## 协程中等待异步加载

```csharp
private IEnumerator LoadIcon()
{
    IAssetService assets = Framework.Resolve<IAssetService>();
    AssetRequest<Sprite> request = assets.LoadAsync<Sprite>("Icons/Coin");

    yield return request;

    if (request.Success)
    {
        iconImage.sprite = request.Asset;
        request.Handle.Release();
    }
    else
    {
        Debug.LogWarning(request.Error);
    }
}
```

## 取消异步请求

```csharp
AssetRequest<Texture2D> request = assets.LoadAsync<Texture2D>("Textures/LargePreview");

if (shouldClose)
{
    request.Cancel();
}
```

取消后请求最终会完成，`Success` 为 `false`，`Error` 通常为取消原因。

## 实例化 Prefab

```csharp
GameObject instance = assets.Instantiate("UI/MainMenu", parentTransform);
```

实例化成功后，框架会给实例挂 `AssetInstanceLease` 并绑定加载句柄。实例销毁时会自动释放资源引用。

如果实例化失败：

```csharp
GameObject instance = assets.Instantiate("Effects/Explosion");
if (instance == null)
{
    Debug.LogWarning("failed to instantiate effect");
}
```

## AssetReference

`AssetReference<T>` 是可序列化的轻量路径引用，适合放在 MonoBehaviour 或 ScriptableObject 字段中。

```csharp
[SerializeField] private AssetReference<AudioClip> clickSound =
    new AssetReference<AudioClip>("Audio/UI/Click");

public void Play()
{
    IAssetService assets = Framework.Resolve<IAssetService>();
    using (AssetHandle<AudioClip> handle = clickSound.Load(assets))
    {
        if (handle.IsValid)
        {
            AudioSource.PlayClipAtPoint(handle.Asset, Vector3.zero);
        }
    }
}
```

异步：

```csharp
AssetRequest<AudioClip> request = clickSound.LoadAsync(assets, handle =>
{
    if (handle.IsValid)
    {
        AudioSource.PlayClipAtPoint(handle.Asset, Vector3.zero);
        handle.Release();
    }
});
```

## 查询加载状态和引用计数

```csharp
bool loaded = assets.IsLoaded("UI/MainMenu");
int refs = assets.GetReferenceCount("UI/MainMenu");

List<AssetStats> stats = assets.GetLoadedAssetStats();
foreach (AssetStats item in stats)
{
    Debug.Log(item.Path + " refs=" + item.ReferenceCount + " type=" + item.TypeName);
}
```

## 手动释放

```csharp
AssetHandle<TextAsset> handle = assets.Load<TextAsset>("Configs/player");
handle.Release();

assets.Release("Configs/player");
assets.ReleaseAll();
assets.UnloadUnusedAssets();
```

`Release(path)` 只减少指定路径引用计数。`ReleaseAll()` 清空模块缓存。`UnloadUnusedAssets()` 调用 Unity 卸载未使用资源。

## 切换资源后端

在 `FrameSettings.AssetServiceBackend` 中选择：

- `Resources`: 使用 `ResourcesAssetService`。
- `Addressables`: 需要 `Frame.Addressables` 集成模块。
- `YooAsset`: 需要 `Frame.YooAsset` 集成模块。

业务代码保持依赖 `IAssetService`，不需要因为后端切换而改调用方式。

## 注意事项

- 同步加载会阻塞当前线程，复杂资源优先使用 `LoadAsync`。
- 每次成功 `Load` 或 `LoadAsync` 都会增加引用计数，必须释放句柄。
- `Instantiate` 返回的实例不要手动释放句柄，销毁实例即可。
- `Resources` 后端路径会去掉扩展名；Addressables 和 YooAsset 后端保留地址语义，详见各自集成示例。
