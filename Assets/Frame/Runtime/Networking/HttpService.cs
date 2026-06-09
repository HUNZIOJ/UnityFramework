using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Frame.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Frame.Networking
{
    public sealed class HttpService : GameModuleBase, IHttpService
    {
        protected override void OnInitialize()
        {
            Context.Services.Register<IHttpService>(this);
            Context.Services.Register(this);
        }

        public HttpRequestHandle Get(string url, Action<HttpResponse> completed)
        {
            return Send(new HttpRequest { Url = url, Method = HttpMethod.Get }, completed);
        }

        public HttpRequestHandle PostJson(string url, string json, Action<HttpResponse> completed)
        {
            return Send(new HttpRequest { Url = url, Method = HttpMethod.Post, Body = json, ContentType = "application/json" }, completed);
        }

        public HttpRequestHandle Send(HttpRequest request, Action<HttpResponse> completed)
        {
            HttpRequestHandle handle = new HttpRequestHandle();
            SendAsync(request, completed, handle).Forget();
            return handle;
        }

        private async UniTaskVoid SendAsync(HttpRequest request, Action<HttpResponse> completed, HttpRequestHandle handle)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Url))
            {
                Complete(handle, completed, new HttpResponse { Success = false, Error = "Url is empty." });

                return;
            }

            int attempts = Math.Max(0, request.Retries) + 1;
            HttpResponse lastResponse = null;
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
                        lastResponse = new HttpResponse { Success = false, Error = exception.Message };
                    }

                    if (shouldSend)
                    {
                        await webRequest.SendWebRequest().ToUniTask();

                        lastResponse = new HttpResponse
                        {
                            Success = webRequest.result == UnityWebRequest.Result.Success,
                            StatusCode = webRequest.responseCode,
                            Text = webRequest.downloadHandler == null ? null : webRequest.downloadHandler.text,
                            Data = webRequest.downloadHandler == null ? null : webRequest.downloadHandler.data,
                            Error = webRequest.error
                        };
                    }

                    handle.Detach(webRequest);

                    if (lastResponse.Success)
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

            if (handle.IsCanceled)
            {
                lastResponse = new HttpResponse { Success = false, Error = "Request canceled." };
            }

            Complete(handle, completed, lastResponse);
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
