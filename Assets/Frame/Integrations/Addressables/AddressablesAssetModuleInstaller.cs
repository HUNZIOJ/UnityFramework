using Frame.Assets;
using Frame.Core;

namespace Frame.Addressables
{
    public sealed class AddressablesAssetModuleInstaller : IFrameModuleInstaller
    {
        public void Install(ModuleManager modules, FrameSettings settings)
        {
            if (settings == null || !settings.EnableAssetService || settings.AssetServiceBackend != AssetServiceBackend.Addressables)
            {
                return;
            }

            modules.Add(new AddressablesAssetService());
        }
    }
}
