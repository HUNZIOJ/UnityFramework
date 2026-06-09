using System.Collections;
using Frame.Core;
using Frame.Scenes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Frame.Tests.PlayMode
{
    public sealed class ScenesModuleTests
    {
        [Test]
        public void SceneService_RejectsEmptySceneNames()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                SceneService service = fixture.Initialize(new SceneService());

                Assert.Throws<FrameException>(() => service.Load(""));
                Assert.Throws<FrameException>(() => service.LoadAsync(new SceneLoadArgs()));
                Assert.IsNull(service.UnloadAsync(""));
                service.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator SceneService_LoadAsyncTracksProgressAndCompletion()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                SceneService service = fixture.Initialize(new SceneService());
                float lastProgress = 0f;
                bool completed = false;

                SceneLoadOperation operation = service.LoadAsync(new SceneLoadArgs
                {
                    SceneName = "SampleScene",
                    Mode = LoadSceneMode.Additive,
                    ActivateOnLoad = true,
                    Progress = progress => lastProgress = progress,
                    Completed = scene => completed = scene.IsValid()
                });

                yield return operation;
                yield return null;

                Assert.IsTrue(operation.IsDone);
                Assert.IsTrue(completed);
                Assert.AreEqual("SampleScene", operation.LoadedScene.name);
                Assert.GreaterOrEqual(lastProgress, 1f);
                AsyncOperation unload = service.UnloadAsync("SampleScene");
                if (unload != null)
                {
                    yield return unload;
                }

                service.Shutdown();
            }
        }
    }
}
