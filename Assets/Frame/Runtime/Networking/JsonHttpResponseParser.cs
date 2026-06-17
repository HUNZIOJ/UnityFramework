using System;
using Frame.Core;
using Newtonsoft.Json;

namespace Frame.Networking
{
    public sealed class JsonHttpResponseParser : IHttpResponseParser
    {
        public static readonly JsonHttpResponseParser Instance = new JsonHttpResponseParser();

        public HttpResponse<TData> Parse<TData>(HttpResponse response)
        {
            HttpResponse<TData> typed = HttpResponse<TData>.CreateFromBase(response);

            if (!typed.Success)
            {
                return typed;
            }

            if (typeof(TData) == typeof(string))
            {
                typed.Value = (TData)(object)typed.Text;
                return typed;
            }

            if (typeof(TData) == typeof(byte[]))
            {
                typed.Value = (TData)(object)typed.Data;
                return typed;
            }

            if (string.IsNullOrWhiteSpace(typed.Text))
            {
                return typed;
            }

            try
            {
                typed.Value = JsonConvert.DeserializeObject<TData>(typed.Text);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                typed.Success = false;
                typed.Error = "Failed to parse response JSON: " + exception.Message;
            }

            return typed;
        }
    }
}
