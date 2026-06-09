using Frame.Utilities;
using NUnit.Framework;

namespace Frame.Tests.EditMode
{
    public sealed class UtilitiesModuleTests
    {
        [Test]
        public void FramePathUtility_NormalizesResourcesPaths()
        {
            Assert.AreEqual("UI/MainMenu", FramePathUtility.NormalizeResourcesPath(@"Assets/Game/Resources/UI/MainMenu.prefab"));
            Assert.AreEqual("Configs/item", FramePathUtility.NormalizeResourcesPath("Configs/item.json"));
            Assert.AreEqual(string.Empty, FramePathUtility.NormalizeResourcesPath(null));
        }

        [Test]
        public void FramePathUtility_SanitizesFileNames()
        {
            Assert.AreEqual("default", FramePathUtility.SanitizeFileName(""));
            string sanitized = FramePathUtility.SanitizeFileName("a:b?c");
            Assert.IsFalse(sanitized.Contains(":"));
            Assert.IsFalse(sanitized.Contains("?"));
        }

        [Test]
        public void DisposableAction_RunsOnce()
        {
            int count = 0;
            DisposableAction disposable = new DisposableAction(() => count++);

            disposable.Dispose();
            disposable.Dispose();

            Assert.AreEqual(1, count);
        }
    }
}
