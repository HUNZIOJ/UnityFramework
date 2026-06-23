using Frame.Core;

namespace Frame.YooAsset
{
    public sealed class YooAssetModuleInstaller : IFrameModuleInstaller
    {
        public void Install(ModuleManager modules, FrameSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (settings.EnableAssetService)
            {
                modules.Add(new YooAssetAssetService());
            }

            if (settings.EnableResourceUpdateService)
            {
                modules.Add(new YooAssetResourceUpdateService());
            }
        }
    }
}
