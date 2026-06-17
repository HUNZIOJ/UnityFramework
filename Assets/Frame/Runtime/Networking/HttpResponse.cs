using System;

namespace Frame.Networking
{
    public class HttpResponse
    {
        public bool Success;
        public long StatusCode;
        public string Text;
        public byte[] Data;
        public string Error;
        public string ErrorCode;
        public string Message;
    }

    public sealed class HttpResponse<TData> : HttpResponse
    {
        public TData Value;

        public static HttpResponse<TData> From(HttpResponse response)
        {
            return JsonHttpResponseParser.Instance.Parse<TData>(response);
        }

        public static HttpResponse<TData> From(HttpResponse response, IHttpResponseParser parser)
        {
            return parser == null ? From(response) : parser.Parse<TData>(response);
        }

        internal static HttpResponse<TData> CreateFromBase(HttpResponse response)
        {
            HttpResponse<TData> typed = new HttpResponse<TData>
            {
                Success = response != null && response.Success,
                StatusCode = response == null ? 0 : response.StatusCode,
                Text = response == null ? null : response.Text,
                Data = response == null ? null : response.Data,
                Error = response == null ? "Response is null." : response.Error,
                ErrorCode = response == null ? null : response.ErrorCode,
                Message = response == null ? null : response.Message
            };

            return typed;
        }
    }
}
