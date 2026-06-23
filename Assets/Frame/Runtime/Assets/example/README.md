# Assets Module Examples

The Assets module exposes `IAssetService` as the single resource loading API. This project only supports the YooAsset backend.

Keep `Assets/Frame/Integrations/YooAsset/Frame.YooAsset.asmdef` in the project. `YooAssetModuleInstaller` registers `YooAssetAssetService` during framework startup when `FrameSettings.EnableAssetService` is enabled.

## Namespace

```csharp
using Frame.Assets;
using Frame.Core;
using UnityEngine;
```

## Resolve Service

```csharp
IAssetService assets = Framework.Resolve<IAssetService>();
```

## YooAsset Locations

Paths are YooAsset locations. The framework only normalizes slashes and trims whitespace; it does not remove file extensions and does not strip `Resources/` prefixes.

```text
UI\MainMenu.prefab                       -> UI/MainMenu.prefab
Assets/Game/Resources/UI/MainMenu.prefab -> Assets/Game/Resources/UI/MainMenu.prefab
```

## Synchronous Load

```csharp
using (AssetHandle<TextAsset> handle = assets.Load<TextAsset>("Configs/player.json"))
{
    if (!handle.IsValid)
    {
        return;
    }

    string json = handle.Asset.text;
    Debug.Log(json);
}
```

## TryLoad

```csharp
if (assets.TryLoad<Sprite>("Icons/Coin.png", out AssetHandle<Sprite> handle))
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

## Async Load

```csharp
AssetRequest<GameObject> request = assets.LoadAsync<GameObject>(
    "UI/ShopPanel.prefab",
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

`AssetRequest<T>` exposes:

- `IsDone`
- `IsCanceled`
- `Success`
- `Progress`
- `Error`
- `Handle`
- `Asset`

## Coroutine

```csharp
private IEnumerator LoadIcon()
{
    IAssetService assets = Framework.Resolve<IAssetService>();
    AssetRequest<Sprite> request = assets.LoadAsync<Sprite>("Icons/Coin.png");

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

## Cancel

```csharp
AssetRequest<Texture2D> request = assets.LoadAsync<Texture2D>("Textures/LargePreview.png");

if (shouldClose)
{
    request.Cancel();
}
```

Cancellation completes the request with `Success == false`. The YooAsset handle is released by the backend.

## Instantiate Prefab

```csharp
GameObject instance = assets.Instantiate("UI/MainMenu.prefab", parentTransform);
```

Successful instances receive `AssetInstanceLease`, which releases the underlying asset handle when the instance is destroyed.

## AssetReference

`AssetReference<T>` is a serializable YooAsset location wrapper.

```csharp
[SerializeField] private AssetReference<AudioClip> clickSound =
    new AssetReference<AudioClip>("Audio/UI/Click.wav");

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

## Stats And Release

```csharp
bool loaded = assets.IsLoaded("UI/MainMenu.prefab");
int refs = assets.GetReferenceCount("UI/MainMenu.prefab");

List<AssetStats> stats = assets.GetLoadedAssetStats();
foreach (AssetStats item in stats)
{
    Debug.Log(item.Path + " refs=" + item.ReferenceCount + " type=" + item.TypeName);
}

assets.Release("UI/MainMenu.prefab");
assets.ReleaseAll();
assets.UnloadUnusedAssets();
```

YooAsset locations must match the active package manifest.
