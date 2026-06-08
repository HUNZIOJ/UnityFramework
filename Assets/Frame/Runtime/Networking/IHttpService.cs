using System;

namespace Frame.Networking
{
    public interface IHttpService
    {
        HttpRequestHandle Get(string url, Action<HttpResponse> completed);

        HttpRequestHandle PostJson(string url, string json, Action<HttpResponse> completed);

        HttpRequestHandle Send(HttpRequest request, Action<HttpResponse> completed);
    }
}
