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
                Assert.Throws<FrameException>(() => service.LoadAsync(new SceneLoadArgs { SceneName = "MissingScene" }));
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
                int startedCount = 0;
                int progressCount = 0;
                int completedEventCount = 0;
                string startedScene = null;
                bool completed = false;
                service.LoadStarted += operation =>
                {
                    startedCount++;
                    startedScene = operation.SceneName;
                };
                service.LoadProgress += (operation, progress) =>
                {
                    progressCount++;
                    lastProgress = progress;
                };
                service.LoadCompleted += operation => completedEventCount++;

                SceneLoadOperation operation = service.LoadAsync(new SceneLoadArgs
                {
                    SceneName = "SampleScene",
                    Mode = LoadSceneMode.Additive,
                    ActivateOnLoad = true,
                    Progress = progress => lastProgress = progress,
                    Completed = scene => completed = scene.IsValid(),
                    SetActiveOnComplete = true
                });

                Assert.IsTrue(service.IsLoading);
                Assert.AreSame(operation, service.CurrentOperation);
                Assert.AreEqual(1, startedCount);
                Assert.AreEqual("SampleScene", startedScene);

                yield return operation;
                yield return null;

                Assert.IsTrue(operation.IsDone);
                Assert.IsTrue(completed);
                Assert.AreEqual(1, completedEventCount);
                Assert.Greater(progressCount, 0);
                Assert.AreEqual("SampleScene", operation.LoadedScene.name);
                Assert.GreaterOrEqual(lastProgress, 1f);
                Assert.IsFalse(service.IsLoading);
                Assert.IsNull(service.CurrentOperation);
                Assert.AreEqual("SampleScene", service.ActiveScene.name);
                AsyncOperation unload = service.UnloadAsync("SampleScene");
                if (unload != null)
                {
                    yield return unload;
                }

                service.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator SceneService_LoadAsyncSupportsManualActivationAndConcurrentGuard()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                SceneService service = fixture.Initialize(new SceneService());
                Assert.IsTrue(service.IsSceneInBuildSettings("SampleScene"));

                SceneLoadOperation operation = service.LoadAsync(new SceneLoadArgs
                {
                    SceneName = "SampleScene",
                    Mode = LoadSceneMode.Additive,
                    ActivateOnLoad = false
                });

                Assert.IsTrue(service.IsLoading);
                Assert.Throws<FrameException>(() => service.LoadAsync(new SceneLoadArgs
                {
                    SceneName = "SampleScene",
                    Mode = LoadSceneMode.Additive
                }));

                int safetyFrames = 0;
                while (!operation.IsReadyToActivate && safetyFrames < 120)
                {
                    safetyFrames++;
                    yield return null;
                }

                Assert.IsTrue(operation.IsReadyToActivate);
                Assert.IsFalse(operation.IsDone);

                operation.Activate();
                yield return operation;
                yield return null;

                Assert.IsTrue(operation.IsDone);
                Assert.IsTrue(service.IsSceneLoaded("SampleScene"));
                Assert.IsTrue(service.SetActiveScene("SampleScene"));

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
