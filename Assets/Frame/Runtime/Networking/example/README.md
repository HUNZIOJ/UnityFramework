# Networking 模块使用示例

Networking 模块包含 HTTP 服务和长连接 Socket 服务。HTTP 基于 `UnityWebRequest`，Socket 支持 TCP 和 WebSocket，提供自动重连、心跳、消息队列和指标统计。

## 命名空间

```csharp
using Frame.Core;
using Frame.Networking;
using Cysharp.Threading.Tasks;
```

## HTTP 获取服务

```csharp
IHttpService http = Framework.Resolve<IHttpService>();
```

## 设置 BaseUrl 和默认 Header

```csharp
http.BaseUrl = "https://example.com/api";
http.SetDefaultHeader("X-Client-Version", Application.version);
http.SetBearerToken("access-token");
```

移除：

```csharp
http.RemoveDefaultHeader("X-Client-Version");
http.SetBearerToken(null);
http.ClearDefaultHeaders();
```

## GET 文本或原始响应

```csharp
HttpRequestHandle handle = http.Get("version", response =>
{
    if (response.Success)
    {
        FrameLog.Info(response.Text);
    }
    else
    {
        FrameLog.Warning(response.Error);
    }
});
```

取消：

```csharp
handle.Cancel();
```

## GET JSON

```csharp
public sealed class VersionResponse
{
    public string Version;
}

http.GetJson<VersionResponse>("version", response =>
{
    if (response.Success)
    {
        FrameLog.Info(response.Value.Version);
    }
});
```

## POST JSON 字符串

```csharp
http.PostJson("login", "{\"account\":\"demo\"}", response =>
{
    FrameLog.Info("status=" + response.StatusCode);
});
```

## POST JSON 对象并解析响应

```csharp
public sealed class LoginRequest
{
    public string Account;
    public string Password;
}

public sealed class LoginResponse
{
    public string Token;
}

http.PostJson<LoginRequest, LoginResponse>(
    "login",
    new LoginRequest { Account = "demo", Password = "123456" },
    response =>
    {
        if (response.Success)
        {
            http.SetBearerToken(response.Value.Token);
        }
    });
```

## 自定义请求

```csharp
HttpRequest request = new HttpRequest
{
    Url = "items/1",
    Method = HttpMethod.Put,
    Body = "{\"count\":3}",
    ContentType = "application/json",
    TimeoutSeconds = 10,
    Retries = 2,
    RetryDelaySeconds = 0.5f
};

request.Headers["X-Trace-Id"] = Guid.NewGuid().ToString("N");

http.Send(request, response =>
{
    FrameLog.Info("success=" + response.Success);
});
```

## 响应解析器

默认 JSON 解析器会把响应正文反序列化为 `TResponse`。

接口包裹结构可使用 `EnvelopeHttpResponseParser`：

```json
{
  "code": 0,
  "message": "ok",
  "data": {
    "name": "Player"
  }
}
```

```csharp
http.ResponseParser = new EnvelopeHttpResponseParser
{
    CodeField = "code",
    MessageField = "message",
    DataField = "data"
};
```

自定义成功码：

```csharp
EnvelopeHttpResponseParser parser = new EnvelopeHttpResponseParser();
parser.SuccessCodes.Add("SUCCESS");
http.ResponseParser = parser;
```

## HTTP 事件和指标

```csharp
http.RequestStarted += request => FrameLog.Info("HTTP start " + request.Url);
http.RequestCompleted += (request, response) => FrameLog.Info("HTTP done " + response.StatusCode);

int active = http.ActiveRequestCount;
int started = http.StartedRequestCount;
int completed = http.CompletedRequestCount;
int failed = http.FailedRequestCount;

http.ClearMetrics();
```

## Socket 获取服务

```csharp
ISocketService sockets = Framework.Resolve<ISocketService>();
```

## 创建 TCP 客户端

```csharp
ISocketClient client = sockets.CreateTcpClient("127.0.0.1", 9000, options =>
{
    options.Id = "game-tcp";
    options.AutoReconnect = true;
    options.HeartbeatIntervalSeconds = 10f;
    options.HeartbeatTimeoutSeconds = 30f;
    options.HeartbeatPayload = System.Text.Encoding.UTF8.GetBytes("ping");
});
```

连接和发送：

```csharp
bool connected = await client.ConnectAsync();
if (connected)
{
    client.SendText("hello");
    client.Send(new byte[] { 1, 2, 3 });
    client.Send(SocketMessage.TextMessage("chat"));
}
```

断开：

```csharp
await client.DisconnectAsync(SocketDisconnectReason.Local);
sockets.RemoveClient(client);
```

## 创建 WebSocket 客户端

```csharp
ISocketClient ws = sockets.CreateWebSocketClient("wss://example.com/realtime", options =>
{
    options.Id = "realtime";
    options.WebSocketHeaders = new Dictionary<string, string>
    {
        { "Authorization", "Bearer token" }
    };
    options.WebSocketSubProtocols = new List<string> { "json" };
});

await ws.ConnectAsync();
ws.SendText("{\"type\":\"hello\"}");
```

## Socket 事件

```csharp
client.StateChanged += (socket, previous, next) =>
{
    FrameLog.Info(previous + " -> " + next);
};

client.Connected += socket =>
{
    FrameLog.Info("connected " + socket.Id);
};

client.Disconnected += (socket, info) =>
{
    FrameLog.Warning("disconnected " + info.Reason + " " + info.Error);
};

client.Reconnecting += (socket, attempt) =>
{
    FrameLog.Info("reconnect attempt " + attempt);
};

client.MessageReceived += (socket, message) =>
{
    if (message.Kind == SocketMessageKind.Text)
    {
        FrameLog.Info(message.Text);
    }
};

client.Error += (socket, exception) =>
{
    FrameLog.Exception(exception);
};
```

事件会派发回主线程。

## SocketClientOptions 常用字段

- `Id`: 客户端 id，空时自动生成。
- `Transport`: `Tcp` 或 `WebSocket`。
- `Host`、`Port`: TCP 地址。
- `Url`: WebSocket 地址。
- `UseTls`、`TlsHostName`、`CertificateValidationCallback`: TCP TLS。
- `ConnectTimeoutMilliseconds`
- `ReceiveBufferSize`
- `MaxMessageSizeBytes`
- `SendQueueLimit`
- `AutoReconnect`
- `MaxReconnectAttempts`
- `ReconnectInitialDelaySeconds`
- `ReconnectMaxDelaySeconds`
- `HeartbeatIntervalSeconds`
- `HeartbeatTimeoutSeconds`
- `HeartbeatPayload`
- `Codec`
- `WebSocketHeaders`
- `WebSocketSubProtocols`

## 指标和客户端管理

```csharp
SocketClientMetrics metrics = client.Metrics;
FrameLog.Info("sent=" + metrics.SentMessages + " recv=" + metrics.ReceivedMessages);

client.ClearMetrics();

int activeConnections = sockets.ActiveConnectionCount;
IReadOnlyList<ISocketClient> clients = sockets.Clients;

await sockets.DisconnectAllAsync();
```

## 自定义消息编解码

TCP 默认使用 `LengthPrefixedSocketCodec`：4 字节大端长度 + payload。

```csharp
SocketClientOptions options = SocketClientOptions.Tcp("127.0.0.1", 9000);
options.Codec = new LengthPrefixedSocketCodec(maxPayloadBytes: 2 * 1024 * 1024);

ISocketClient client = sockets.CreateClient(options);
```

自定义 Codec：

```csharp
public sealed class RawCodec : ISocketMessageCodec
{
    public byte[] Encode(SocketMessage message)
    {
        return message.Data;
    }

    public bool TryDecode(SocketReceiveBuffer buffer, out SocketMessage message)
    {
        message = null;
        if (buffer.Count == 0)
        {
            return false;
        }

        if (buffer.TryRead(buffer.Count, out byte[] data))
        {
            message = SocketMessage.Binary(data);
            return true;
        }

        return false;
    }
}
```

## 注意事项

- HTTP `BaseUrl` 只会拼接相对 URL，绝对 URL 不会被改写。
- HTTP 回调异常会被 `FrameLog.Exception` 捕获。
- Socket `Send` 只在 `Connected` 状态入队，队列满会返回 `false` 并增加 `DroppedMessages`。
- TCP 和 WebSocket 行为受平台限制，WebGL 下 WebSocket 接收类型可用 `WebGlWebSocketReceiveKind` 配置。
- 长连接对象用完要 `DisconnectAsync` 或 `RemoveClient`，模块关闭时会统一释放。
