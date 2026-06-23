# HotUpdate Module Examples

`Frame.HotUpdate` provides the HybridCLR runtime loading service:

- `IHotUpdateService`: load HybridCLR AOT supplementary metadata and load hot update assemblies from bytes assets.

Resource update is implemented by `Frame.YooAsset.YooAssetResourceUpdateService`. See `Assets/Frame/Integrations/YooAsset/example/README.md` for the YooAsset update flow.

## HybridCLR

Put DLL bytes in the active asset backend as `TextAsset` files, for example:

```text
HotUpdate/AOT/mscorlib.dll.bytes
HotUpdate/Assemblies/Game.HotUpdate.dll.bytes
```

Then load metadata before loading hot update assemblies.

```csharp
using Frame.Core;
using Frame.HotUpdate;

public static class HotUpdateBootstrap
{
    public static void Load()
    {
        IHotUpdateService hotUpdate = Framework.Resolve<IHotUpdateService>();

        hotUpdate.LoadAotMetadataAsset("HotUpdate/AOT/mscorlib.dll");
        hotUpdate.LoadAotMetadataAsset("HotUpdate/AOT/System.dll");

        HotUpdateLoadResult assembly = hotUpdate.LoadAssemblyAsset("HotUpdate/Assemblies/Game.HotUpdate.dll");
        if (!assembly.Success)
        {
            throw new System.Exception(assembly.Error);
        }

        hotUpdate.InvokeStatic(
            assembly.Assembly.GetName().Name,
            "Game.HotUpdate.Entry",
            "Start");
    }
}
```

The service tracks loaded AOT metadata names and loaded assemblies to prevent duplicate loads through the same API.
