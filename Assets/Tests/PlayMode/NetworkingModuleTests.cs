using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
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

        [Test]
        public void LengthPrefixedSocketCodec_DecodesFragmentedAndMultipleFrames()
        {
            LengthPrefixedSocketCodec codec = new LengthPrefixedSocketCodec();
            byte[] first = codec.Encode(SocketMessage.TextMessage("one"));
            byte[] second = codec.Encode(SocketMessage.TextMessage("two"));
            SocketReceiveBuffer buffer = new SocketReceiveBuffer();

            buffer.Append(first, 2);
            SocketMessage decoded;
            Assert.IsFalse(codec.TryDecode(buffer, out decoded));

            byte[] remaining = new byte[first.Length - 2 + second.Length];
            Buffer.BlockCopy(first, 2, remaining, 0, first.Length - 2);
            Buffer.BlockCopy(second, 0, remaining, first.Length - 2, second.Length);
            buffer.Append(remaining, remaining.Length);

            Assert.IsTrue(codec.TryDecode(buffer, out decoded));
            Assert.AreEqual("one", decoded.Text);
            Assert.IsTrue(codec.TryDecode(buffer, out decoded));
            Assert.AreEqual("two", decoded.Text);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void SocketService_CreatesAndRemovesClients()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                SocketService service = fixture.Initialize(new SocketService());
                ISocketClient tcp = service.CreateTcpClient("127.0.0.1", 9000);
                ISocketClient webSocket = service.CreateWebSocketClient("ws://localhost:9001/socket");

                Assert.AreEqual(2, service.Clients.Count);
                Assert.AreEqual(SocketTransportType.Tcp, tcp.Options.Transport);
                Assert.AreEqual(SocketTransportType.WebSocket, webSocket.Options.Transport);
                Assert.IsTrue(service.RemoveClient(tcp));
                Assert.AreEqual(1, service.Clients.Count);

                service.Shutdown();
            }
        }

        [Test]
        public void SocketClientOptions_RejectInvalidEndpoints()
        {
            Assert.Throws<System.ArgumentException>(() => new SocketClient(SocketClientOptions.Tcp("", 1)));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SocketClient(SocketClientOptions.Tcp("127.0.0.1", 70000)));
            Assert.Throws<System.ArgumentException>(() => new SocketClient(SocketClientOptions.WebSocket("http://localhost/socket")));
        }

        [UnityTest]
        public IEnumerator SocketClient_TcpConnectsSendsReceivesAndDisconnects()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            CancellationTokenSource serverCancellation = new CancellationTokenSource();
            Task serverTask = RunLengthPrefixedEchoServer(listener, serverCancellation.Token);

            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                SocketService service = fixture.Initialize(new SocketService());
                ISocketClient client = service.CreateTcpClient("127.0.0.1", port, options =>
                {
                    options.AutoReconnect = false;
                    options.ConnectTimeoutMilliseconds = 2000;
                    options.SendQueueLimit = 8;
                });

                string received = null;
                client.MessageReceived += (socket, message) => received = message.Text;

                bool connected = false;
                Exception connectException = null;
                yield return client.ConnectAsync().ToCoroutine(value => connected = value, exception => connectException = exception);

                Assert.IsNull(connectException);
                Assert.IsTrue(connected);
                Assert.AreEqual(SocketClientState.Connected, client.State);
                Assert.IsTrue(client.SendText("hello"));

                float timeout = Time.realtimeSinceStartup + 2f;
                while (received == null && Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }

                Assert.AreEqual("hello", received);
                Assert.AreEqual(1, client.Metrics.SentMessages);
                Assert.AreEqual(1, client.Metrics.ReceivedMessages);

                yield return client.DisconnectAsync().ToCoroutine();
                Assert.AreEqual(SocketClientState.Disconnected, client.State);
                service.Shutdown();
            }

            serverCancellation.Cancel();
            listener.Stop();
            yield return WaitForServerTask(serverTask);
            serverCancellation.Dispose();
        }

        [UnityTest]
        public IEnumerator SocketClient_WebSocketConnectsSendsReceivesAndDisconnects()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Assert.Ignore("ClientWebSocket is not available on WebGL builds.");
            yield break;
#else
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            CancellationTokenSource serverCancellation = new CancellationTokenSource();
            Task serverTask = RunWebSocketEchoServer(listener, serverCancellation.Token);

            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                SocketService service = fixture.Initialize(new SocketService());
                ISocketClient client = service.CreateWebSocketClient("ws://127.0.0.1:" + port + "/socket", options =>
                {
                    options.AutoReconnect = false;
                    options.ConnectTimeoutMilliseconds = 2000;
                    options.SendQueueLimit = 8;
                });

                string received = null;
                client.MessageReceived += (socket, message) => received = message.Text;

                bool connected = false;
                Exception connectException = null;
                yield return client.ConnectAsync().ToCoroutine(value => connected = value, exception => connectException = exception);

                Assert.IsNull(connectException);
                Assert.IsTrue(connected);
                Assert.AreEqual(SocketClientState.Connected, client.State);
                Assert.IsTrue(client.SendText("websocket"));

                float timeout = Time.realtimeSinceStartup + 2f;
                while (received == null && Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }

                Assert.AreEqual("websocket", received);
                Assert.AreEqual(1, client.Metrics.SentMessages);
                Assert.AreEqual(1, client.Metrics.ReceivedMessages);

                yield return client.DisconnectAsync().ToCoroutine();
                Assert.AreEqual(SocketClientState.Disconnected, client.State);
                service.Shutdown();
            }

            serverCancellation.Cancel();
            listener.Stop();
            yield return WaitForServerTask(serverTask);
            serverCancellation.Dispose();
#endif
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

        private static async Task RunLengthPrefixedEchoServer(TcpListener listener, CancellationToken cancellationToken)
        {
            try
            {
                using (TcpClient client = await listener.AcceptTcpClientAsync())
                using (NetworkStream stream = client.GetStream())
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        byte[] header = await ReadExactAsync(stream, 4, cancellationToken);
                        if (header == null)
                        {
                            return;
                        }

                        int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                        byte[] payload = await ReadExactAsync(stream, length, cancellationToken);
                        if (payload == null)
                        {
                            return;
                        }

                        await stream.WriteAsync(header, 0, header.Length, cancellationToken);
                        if (payload.Length > 0)
                        {
                            await stream.WriteAsync(payload, 0, payload.Length, cancellationToken);
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            catch (IOException)
            {
            }
            catch (System.OperationCanceledException)
            {
            }
        }

        private static async Task RunWebSocketEchoServer(TcpListener listener, CancellationToken cancellationToken)
        {
            try
            {
                using (TcpClient client = await listener.AcceptTcpClientAsync())
                using (NetworkStream stream = client.GetStream())
                {
                    string header = await ReadHttpHeaderAsync(stream, cancellationToken);
                    string key = ReadHttpHeaderValue(header, "Sec-WebSocket-Key");
                    string accept = CreateWebSocketAccept(key);
                    byte[] response = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 101 Switching Protocols\r\n" +
                        "Upgrade: websocket\r\n" +
                        "Connection: Upgrade\r\n" +
                        "Sec-WebSocket-Accept: " + accept + "\r\n\r\n");

                    await stream.WriteAsync(response, 0, response.Length, cancellationToken);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        TestWebSocketFrame frame = await ReadWebSocketFrameAsync(stream, cancellationToken);
                        if (frame == null)
                        {
                            return;
                        }

                        if (frame.Opcode == 0x8)
                        {
                            await WriteWebSocketFrameAsync(stream, frame.Payload, 0x8, cancellationToken);
                            return;
                        }

                        if (frame.Opcode == 0x9)
                        {
                            await WriteWebSocketFrameAsync(stream, frame.Payload, 0xA, cancellationToken);
                            continue;
                        }

                        await WriteWebSocketFrameAsync(stream, frame.Payload, frame.Opcode, cancellationToken);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            catch (IOException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task<string> ReadHttpHeaderAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] marker = Encoding.ASCII.GetBytes("\r\n\r\n");
            byte[] one = new byte[1];
            int matched = 0;
            using (MemoryStream buffer = new MemoryStream())
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int read = await stream.ReadAsync(one, 0, 1, cancellationToken);
                    if (read <= 0)
                    {
                        return string.Empty;
                    }

                    buffer.WriteByte(one[0]);
                    if (one[0] == marker[matched])
                    {
                        matched++;
                        if (matched == marker.Length)
                        {
                            break;
                        }
                    }
                    else
                    {
                        matched = one[0] == marker[0] ? 1 : 0;
                    }
                }

                return Encoding.ASCII.GetString(buffer.ToArray());
            }
        }

        private static string ReadHttpHeaderValue(string header, string name)
        {
            using (StringReader reader = new StringReader(header ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int separator = line.IndexOf(':');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring(separator + 1).Trim();
                    }
                }
            }

            return string.Empty;
        }

        private static string CreateWebSocketAccept(string key)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] source = Encoding.ASCII.GetBytes((key ?? string.Empty) + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
                return Convert.ToBase64String(sha1.ComputeHash(source));
            }
        }

        private static async Task<TestWebSocketFrame> ReadWebSocketFrameAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] header = await ReadExactAsync(stream, 2, cancellationToken);
            if (header == null)
            {
                return null;
            }

            byte opcode = (byte)(header[0] & 0x0f);
            bool masked = (header[1] & 0x80) != 0;
            ulong length = (ulong)(header[1] & 0x7f);
            if (length == 126)
            {
                byte[] extended = await ReadExactAsync(stream, 2, cancellationToken);
                if (extended == null)
                {
                    return null;
                }

                length = (ulong)((extended[0] << 8) | extended[1]);
            }
            else if (length == 127)
            {
                byte[] extended = await ReadExactAsync(stream, 8, cancellationToken);
                if (extended == null)
                {
                    return null;
                }

                length = 0;
                for (int i = 0; i < extended.Length; i++)
                {
                    length = (length << 8) | extended[i];
                }
            }

            byte[] mask = masked ? await ReadExactAsync(stream, 4, cancellationToken) : null;
            if (masked && mask == null)
            {
                return null;
            }

            if (length > 1024 * 1024)
            {
                throw new IOException("Test WebSocket frame is too large.");
            }

            byte[] payload = await ReadExactAsync(stream, (int)length, cancellationToken);
            if (payload == null)
            {
                return null;
            }

            if (masked)
            {
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] = (byte)(payload[i] ^ mask[i % 4]);
                }
            }

            return new TestWebSocketFrame(opcode, payload);
        }

        private static async Task WriteWebSocketFrameAsync(Stream stream, byte[] payload, byte opcode, CancellationToken cancellationToken)
        {
            payload = payload ?? new byte[0];
            byte[] header;
            if (payload.Length < 126)
            {
                header = new[] { (byte)(0x80 | opcode), (byte)payload.Length };
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                header = new[]
                {
                    (byte)(0x80 | opcode),
                    (byte)126,
                    (byte)((payload.Length >> 8) & 0xff),
                    (byte)(payload.Length & 0xff)
                };
            }
            else
            {
                header = new byte[10];
                header[0] = (byte)(0x80 | opcode);
                header[1] = 127;
                ulong length = (ulong)payload.Length;
                for (int i = 0; i < 8; i++)
                {
                    header[9 - i] = (byte)(length & 0xff);
                    length >>= 8;
                }
            }

            await stream.WriteAsync(header, 0, header.Length, cancellationToken);
            if (payload.Length > 0)
            {
                await stream.WriteAsync(payload, 0, payload.Length, cancellationToken);
            }
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset, cancellationToken);
                if (read <= 0)
                {
                    return null;
                }

                offset += read;
            }

            return buffer;
        }

        private static IEnumerator WaitForServerTask(Task task)
        {
            float timeout = Time.realtimeSinceStartup + 1f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
        }

        private sealed class TestWebSocketFrame
        {
            public TestWebSocketFrame(byte opcode, byte[] payload)
            {
                Opcode = opcode;
                Payload = payload;
            }

            public byte Opcode { get; private set; }

            public byte[] Payload { get; private set; }
        }
    }
}
