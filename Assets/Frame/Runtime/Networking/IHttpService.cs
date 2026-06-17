using System;
using System.Collections.Generic;

namespace Frame.Networking
{
    public interface IHttpService
    {
        event Action<HttpRequest> RequestStarted;

        event Action<HttpRequest, HttpResponse> RequestCompleted;

        string BaseUrl { get; set; }

        IHttpResponseParser ResponseParser { get; set; }

        IReadOnlyDictionary<string, string> DefaultHeaders { get; }

        int ActiveRequestCount { get; }

        int StartedRequestCount { get; }

        int CompletedRequestCount { get; }

        int FailedRequestCount { get; }

        void ClearMetrics();

        void SetDefaultHeader(string name, string value);

        bool RemoveDefaultHeader(string name);

        void ClearDefaultHeaders();

        void SetBearerToken(string token);

        HttpRequestHandle Get(string url, Action<HttpResponse> completed);

        HttpRequestHandle GetJson<TResponse>(string url, Action<HttpResponse<TResponse>> completed);

        HttpRequestHandle PostJson(string url, string json, Action<HttpResponse> completed);

        HttpRequestHandle PostJson<TRequest, TResponse>(string url, TRequest body, Action<HttpResponse<TResponse>> completed);

        HttpRequestHandle Send(HttpRequest request, Action<HttpResponse> completed);

        HttpRequestHandle SendJson<TResponse>(HttpRequest request, Action<HttpResponse<TResponse>> completed);
    }
}
