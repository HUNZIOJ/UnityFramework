using System.Collections;
using Frame.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frame.Tests.PlayMode
{
    public sealed class NetworkingModuleTests
    {
        [Test]
        public void HttpService_ManagesDefaultHeadersAndBearerToken()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                HttpService service = fixture.Initialize(new HttpService());

                service.BaseUrl = "https://api.example.test";
                service.SetDefaultHeader("X-Project", "Frame");
                service.SetBearerToken("token");

                Assert.AreEqual("https://api.example.test", service.BaseUrl);
                Assert.AreEqual("Frame", service.DefaultHeaders["X-Project"]);
                Assert.AreEqual("Bearer token", service.DefaultHeaders["Authorization"]);

                service.SetBearerToken(null);
                Assert.IsFalse(service.DefaultHeaders.ContainsKey("Authorization"));
                Assert.IsTrue(service.RemoveDefaultHeader("X-Project"));

                service.SetDefaultHeader("X-Trace", "1");
                service.ClearDefaultHeaders();
                Assert.AreEqual(0, service.DefaultHeaders.Count);
                service.Shutdown();
            }
        }

        [Test]
        public void HttpResponse_ParsesTypedJsonPayload()
        {
            HttpResponse response = new HttpResponse
            {
                Success = true,
                StatusCode = 200,
                Text = "{\"name\":\"Player\",\"level\":3}"
            };

            HttpResponse<TypedPayload> typed = HttpResponse<TypedPayload>.From(response);

            Assert.IsTrue(typed.Success);
            Assert.AreEqual(200, typed.StatusCode);
            Assert.AreEqual("Player", typed.Value.name);
            Assert.AreEqual(3, typed.Value.level);
        }

        [Test]
        public void EnvelopeHttpResponseParser_MapsProtocolSuccessDataAndErrors()
        {
            EnvelopeHttpResponseParser parser = new EnvelopeHttpResponseParser();
            HttpResponse success = new HttpResponse
            {
                Success = true,
                StatusCode = 200,
                Text = "{\"code\":0,\"message\":\"ok\",\"data\":{\"name\":\"Player\",\"level\":5}}"
            };

            HttpResponse<TypedPayload> typed = parser.Parse<TypedPayload>(success);

            Assert.IsTrue(typed.Success);
            Assert.AreEqual("0", typed.ErrorCode);
            Assert.AreEqual("ok", typed.Message);
            Assert.AreEqual("Player", typed.Value.name);
            Assert.AreEqual(5, typed.Value.level);

            HttpResponse failed = new HttpResponse
            {
                Success = true,
                StatusCode = 200,
                Text = "{\"code\":401,\"message\":\"Unauthorized\",\"data\":null}"
            };

            HttpResponse<TypedPayload> error = parser.Parse<TypedPayload>(failed);

            Assert.IsFalse(error.Success);
            Assert.AreEqual("401", error.ErrorCode);
            Assert.AreEqual("Unauthorized", error.Error);
            Assert.IsNull(error.Value);
        }

        [Test]
        public void EnvelopeHttpResponseParser_FallsBackToRawJsonForNonEnvelopePayload()
        {
            EnvelopeHttpResponseParser parser = new EnvelopeHttpResponseParser();
            HttpResponse response = new HttpResponse
            {
                Success = true,
                StatusCode = 200,
                Text = "{\"name\":\"Player\",\"level\":7}"
            };

            HttpResponse<TypedPayload> typed = parser.Parse<TypedPayload>(response);

            Assert.IsTrue(typed.Success);
            Assert.AreEqual("Player", typed.Value.name);
            Assert.AreEqual(7, typed.Value.level);
        }

        [UnityTest]
        public IEnumerator HttpService_RejectsEmptyUrlWithFailureResponse()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                HttpService service = fixture.Initialize(new HttpService());
                HttpResponse completed = null;
                HttpRequest startedRequest = null;
                HttpResponse completedEventResponse = null;
                int startedEventCount = 0;
                int completedEventCount = 0;
                service.RequestStarted += request =>
                {
                    startedEventCount++;
                    startedRequest = request;
                };
                service.RequestCompleted += (request, response) =>
                {
                    completedEventCount++;
                    completedEventResponse = response;
                };

                HttpRequestHandle handle = service.Get("", response => completed = response);
                yield return handle;

                Assert.IsTrue(handle.IsDone);
                Assert.IsFalse(handle.Response.Success);
                Assert.AreEqual("Url is empty.", handle.Response.Error);
                Assert.AreSame(handle.Response, completed);
                Assert.IsNotNull(startedRequest);
                Assert.AreEqual("", startedRequest.Url);
                Assert.AreSame(handle.Response, completedEventResponse);
                Assert.AreEqual(1, startedEventCount);
                Assert.AreEqual(1, completedEventCount);
                Assert.AreEqual(0, service.ActiveRequestCount);
                Assert.AreEqual(1, service.StartedRequestCount);
                Assert.AreEqual(1, service.CompletedRequestCount);
                Assert.AreEqual(1, service.FailedRequestCount);

                service.ClearMetrics();
                Assert.AreEqual(0, service.ActiveRequestCount);
                Assert.AreEqual(0, service.StartedRequestCount);
                Assert.AreEqual(0, service.CompletedRequestCount);
                Assert.AreEqual(0, service.FailedRequestCount);
                service.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator HttpService_TypedJsonFailurePropagatesRawError()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                HttpService service = fixture.Initialize(new HttpService());
                HttpResponse<TypedPayload> completed = null;

                HttpRequestHandle handle = service.GetJson<TypedPayload>("", response => completed = response);
                yield return handle;

                Assert.IsTrue(handle.IsDone);
                Assert.IsFalse(completed.Success);
                Assert.AreEqual("Url is empty.", completed.Error);
                Assert.IsNull(completed.Value);
                service.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator HttpService_UsesConfiguredResponseParserForTypedJson()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                HttpService service = fixture.Initialize(new HttpService());
                CountingParser parser = new CountingParser();
                HttpResponse<TypedPayload> completed = null;
                service.ResponseParser = parser;

                HttpRequestHandle handle = service.GetJson<TypedPayload>("", response => completed = response);
                yield return handle;

                Assert.AreEqual(1, parser.Count);
                Assert.IsFalse(completed.Success);
                Assert.AreEqual("parsed", completed.Error);
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

        private sealed class TypedPayload
        {
            public string name { get; set; }
            public int level { get; set; }
        }

        private sealed class CountingParser : IHttpResponseParser
        {
            public int Count;

            public HttpResponse<TData> Parse<TData>(HttpResponse response)
            {
                Count++;
                return new HttpResponse<TData>
                {
                    Success = false,
                    Error = "parsed"
                };
            }
        }
    }
}
