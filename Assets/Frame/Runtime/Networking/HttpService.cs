using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Frame.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Frame.Networking
{
    public sealed class HttpService : GameModuleBase, IHttpService
    {
        private readonly Dictionary<string, string> defaultHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private int activeRequestCount;
        private int startedRequestCount;
        private int completedRequestCount;
        private int failedRequestCount;

        public event Action<HttpRequest> RequestStarted;

        public event Action<HttpRequest, HttpResponse> RequestCompleted;

        public string BaseUrl { get; set; }

        public IHttpResponseParser ResponseParser { get; set; }

        public IReadOnlyDictionary<string, string> DefaultHeaders
        {
            get { return defaultHeaders; }
        }

        public int ActiveRequestCount
        {
            get { return activeRequestCount; }
        }

        public int StartedRequestCount
        {
            get { return startedRequestCount; }
        }

        public int CompletedRequestCount
        {
            get { return completedRequestCount; }
        }

        public int FailedRequestCount
        {
            get { return failedRequestCount; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<IHttpService>(this);
            Context.Services.Register(this);
        }

        public void ClearMetrics()
        {
            startedRequestCount = 0;
            completedRequestCount = 0;
            failedRequestCount = 0;
        }

        public void SetDefaultHeader(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("HTTP header name is required.", "name");
            }

            if (value == null)
            {
                defaultHeaders.Remove(name);
                return;
            }

            defaultHeaders[name] = value;
        }

        public bool RemoveDefaultHeader(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && defaultHeaders.Remove(name);
        }

        public void ClearDefaultHeaders()
        {
            defaultHeaders.Clear();
        }

        public void SetBearerToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                RemoveDefaultHeader("Authorization");
                return;
            }

            SetDefaultHeader("Authorization", "Bearer " + token);
        }

        public HttpRequestHandle Get(string url, Action<HttpResponse> completed)
        {
            return Send(new HttpRequest { Url = url, Method = HttpMethod.Get }, completed);
        }

        public HttpRequestHandle GetJson<TResponse>(string url, Action<HttpResponse<TResponse>> completed)
        {
            return SendJson(new HttpRequest { Url = url, Method = HttpMethod.Get }, completed);
        }

        public HttpRequestHandle PostJson(string url, string json, Action<HttpResponse> completed)
        {
            return Send(new HttpRequest { Url = url, Method = HttpMethod.Post, Body = json, ContentType = "application/json" }, completed);
        }

        public HttpRequestHandle PostJson<TRequest, TResponse>(string url, TRequest body, Action<HttpResponse<TResponse>> completed)
        {
            string json = JsonConvert.SerializeObject(body);
            return SendJson(new HttpRequest { Url = url, Method = HttpMethod.Post, Body = json, ContentType = "application/json" }, completed);
        }

        public HttpRequestHandle Send(HttpRequest request, Action<HttpResponse> completed)
        {
            HttpRequestHandle handle = new HttpRequestHandle();
            SendAsync(PrepareRequest(request), completed, handle).Forget();
            return handle;
        }

        public HttpRequestHandle SendJson<TResponse>(HttpRequest request, Action<HttpResponse<TResponse>> completed)
        {
            return Send(request, response =>
            {
                HttpResponse<TResponse> typed = HttpResponse<TResponse>.From(response, ResponseParser);
                if (completed != null)
                {
                    completed(typed);
                }
            });
        }

        protected override void OnShutdown()
        {
            defaultHeaders.Clear();
            BaseUrl = null;
            ResponseParser = null;
            activeRequestCount = 0;
            startedRequestCount = 0;
            completedRequestCount = 0;
            failedRequestCount = 0;
            RequestStarted = null;
            RequestCompleted = null;
        }

        private async UniTaskVoid SendAsync(HttpRequest request, Action<HttpResponse> completed, HttpRequestHandle handle)
        {
            BeginRequest(request);
            HttpResponse finalResponse = null;
            if (request == null || string.IsNullOrWhiteSpace(request.Url))
            {
                finalResponse = new HttpResponse { Success = false, Error = "Url is empty." };
                FinishRequest(request, finalResponse);
                Complete(handle, completed, finalResponse);
                return;
            }

            try
            {
                int attempts = Math.Max(0, request.Retries) + 1;
                for (int i = 0; i < attempts; i++)
                {
                    if (handle.IsCanceled)
                    {
                        break;
                    }

                    using (UnityWebRequest webRequest = CreateUnityRequest(request))
                    {
                        handle.Attach(webRequest);
                        bool shouldSend = true;

                        try
                        {
                            webRequest.timeout = Math.Max(1, request.TimeoutSeconds);
                            if (request.Headers != null)
                            {
                                foreach (KeyValuePair<string, string> header in request.Headers)
                                {
                                    if (!string.IsNullOrWhiteSpace(header.Key))
                                    {
                                        webRequest.SetRequestHeader(header.Key, header.Value);
                                    }
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            FrameLog.Exception(exception);
                            shouldSend = false;
                            finalResponse = new HttpResponse { Success = false, Error = exception.Message };
                        }

                        if (shouldSend)
                        {
                            try
                            {
                                await webRequest.SendWebRequest().ToUniTask();
                                finalResponse = CreateResponse(webRequest);
                            }
                            catch (UnityWebRequestException)
                            {
                                finalResponse = CreateResponse(webRequest);
                            }
                        }

                        handle.Detach(webRequest);

                        if (finalResponse.Success)
                        {
                            break;
                        }
                    }

                    if (i < attempts - 1 && request.RetryDelaySeconds > 0f)
                    {
                        int delayMilliseconds = Mathf.CeilToInt(request.RetryDelaySeconds * 1000f);
                        await UniTask.Delay(delayMilliseconds, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update);
                    }
                }
            }
            catch (Exception exception)
            {
                if (!handle.IsCanceled)
                {
                    FrameLog.Exception(exception);
                    finalResponse = new HttpResponse { Success = false, Error = exception.Message };
                }
            }

            if (handle.IsCanceled)
            {
                finalResponse = new HttpResponse { Success = false, Error = "Request canceled." };
            }
            else if (finalResponse == null)
            {
                finalResponse = new HttpResponse { Success = false, Error = "Request failed." };
            }

            FinishRequest(request, finalResponse);
            Complete(handle, completed, finalResponse);
        }

        private static UnityWebRequest CreateUnityRequest(HttpRequest request)
        {
            switch (request.Method)
            {
                case HttpMethod.Post:
                    return CreateUploadRequest(UnityWebRequest.kHttpVerbPOST, request);
                case HttpMethod.Put:
                    return CreateUploadRequest(UnityWebRequest.kHttpVerbPUT, request);
                case HttpMethod.Delete:
                    return UnityWebRequest.Delete(request.Url);
                default:
                    return UnityWebRequest.Get(request.Url);
            }
        }

        private static UnityWebRequest CreateUploadRequest(string method, HttpRequest request)
        {
            UnityWebRequest webRequest = new UnityWebRequest(request.Url, method);
            byte[] bytes = Encoding.UTF8.GetBytes(request.Body == null ? string.Empty : request.Body);
            webRequest.uploadHandler = new UploadHandlerRaw(bytes);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", string.IsNullOrWhiteSpace(request.ContentType) ? "application/json" : request.ContentType);
            return webRequest;
        }

        private static HttpResponse CreateResponse(UnityWebRequest webRequest)
        {
            if (webRequest == null)
            {
                return new HttpResponse { Success = false, Error = "Request failed." };
            }

            return new HttpResponse
            {
                Success = webRequest.result == UnityWebRequest.Result.Success,
                StatusCode = webRequest.responseCode,
                Text = webRequest.downloadHandler == null ? null : webRequest.downloadHandler.text,
                Data = webRequest.downloadHandler == null ? null : webRequest.downloadHandler.data,
                Error = webRequest.error
            };
        }

        private HttpRequest PrepareRequest(HttpRequest source)
        {
            if (source == null)
            {
                return null;
            }

            HttpRequest request = new HttpRequest
            {
                Url = ResolveUrl(source.Url),
                Method = source.Method,
                Body = source.Body,
                ContentType = source.ContentType,
                TimeoutSeconds = source.TimeoutSeconds,
                Retries = source.Retries,
                RetryDelaySeconds = source.RetryDelaySeconds
            };

            foreach (KeyValuePair<string, string> header in defaultHeaders)
            {
                request.Headers[header.Key] = header.Value;
            }

            if (source.Headers != null)
            {
                foreach (KeyValuePair<string, string> header in source.Headers)
                {
                    request.Headers[header.Key] = header.Value;
                }
            }

            return request;
        }

        private string ResolveUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(BaseUrl) || Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return url;
            }

            return BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/');
        }

        private void BeginRequest(HttpRequest request)
        {
            activeRequestCount++;
            startedRequestCount++;
            Action<HttpRequest> handler = RequestStarted;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(request);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }

        private void FinishRequest(HttpRequest request, HttpResponse response)
        {
            activeRequestCount = Math.Max(0, activeRequestCount - 1);
            completedRequestCount++;
            if (response == null || !response.Success)
            {
                failedRequestCount++;
            }

            Action<HttpRequest, HttpResponse> handler = RequestCompleted;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(request, response);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }

        private static void Complete(HttpRequestHandle handle, Action<HttpResponse> completed, HttpResponse response)
        {
            if (handle != null)
            {
                handle.Complete(response);
            }

            if (completed != null)
            {
                try
                {
                    completed(response);
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                }
            }
        }
    }
}
