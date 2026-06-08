using System.Collections.Generic;

namespace Frame.Networking
{
    public sealed class HttpRequest
    {
        public string Url;
        public HttpMethod Method = HttpMethod.Get;
        public string Body;
        public string ContentType = "application/json";
        public int TimeoutSeconds = 15;
        public int Retries = 0;
        public float RetryDelaySeconds = 0.25f;
        public Dictionary<string, string> Headers = new Dictionary<string, string>();
    }
}
