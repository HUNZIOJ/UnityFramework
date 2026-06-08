using Frame.Core;

namespace Frame.DOTween
{
    public sealed class DOTweenModuleInstaller : IFrameModuleInstaller
    {
        public void Install(ModuleManager modules, FrameSettings settings)
        {
            if (settings != null && settings.EnableTweenService)
            {
                modules.Add(new DOTweenTweenService());
            }
        }
    }
}
