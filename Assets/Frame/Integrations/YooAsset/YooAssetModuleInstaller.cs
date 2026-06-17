using Frame.Assets;
using Frame.Core;

namespace Frame.YooAsset
{
    public sealed class YooAssetModuleInstaller : IFrameModuleInstaller
    {
        public void Install(ModuleManager modules, FrameSettings settings)
        {
            if (settings == null || !settings.EnableAssetService || settings.AssetServiceBackend != AssetServiceBackend.YooAsset)
            {
                return;
            }

            modules.Add(new YooAssetAssetService());
        }
    }
}
