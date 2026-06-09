using System.Collections;
using Frame.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frame.Tests.PlayMode
{
    public sealed class NetworkingModuleTests
    {
        [UnityTest]
        public IEnumerator HttpService_RejectsEmptyUrlWithFailureResponse()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                HttpService service = fixture.Initialize(new HttpService());
                HttpResponse completed = null;

                HttpRequestHandle handle = service.Get("", response => completed = response);
                yield return handle;

                Assert.IsTrue(handle.IsDone);
                Assert.IsFalse(handle.Response.Success);
                Assert.AreEqual("Url is empty.", handle.Response.Error);
                Assert.AreSame(handle.Response, completed);
                service.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator HttpService_CancelCompletesWithCanceledResponse()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                HttpService service = fixture.Initialize(new HttpService());
                HttpRequest request = new HttpRequest
                {
                    Url = "http://127.0.0.1:9/frame-test",
                    TimeoutSeconds = 5,
                    Retries = 0
                };

                HttpRequestHandle handle = service.Send(request, null);
                handle.Cancel();
                yield return handle;

                Assert.IsTrue(handle.IsDone);
                Assert.IsTrue(handle.IsCanceled);
                Assert.IsFalse(handle.Response.Success);
                Assert.AreEqual("Request canceled.", handle.Response.Error);
                service.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator HttpService_FailedConnectionReturnsFailureResponseAndInvokesCallbackSafely()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                HttpService service = fixture.Initialize(new HttpService());
                int callbackCount = 0;
                HttpRequest request = new HttpRequest
                {
                    Url = "http://127.0.0.1:9/frame-test",
                    TimeoutSeconds = 1,
                    Retries = 1,
                    RetryDelaySeconds = 0f
                };

                LogAssert.Expect(LogType.Exception, "InvalidOperationException: callback");
                HttpRequestHandle handle = service.Send(request, response =>
                {
                    callbackCount++;
                    throw new System.InvalidOperationException("callback");
                });
                yield return handle;

                Assert.IsTrue(handle.IsDone);
                Assert.IsFalse(handle.Response.Success);
                Assert.AreEqual(1, callbackCount);
                service.Shutdown();
            }
        }
    }
}
