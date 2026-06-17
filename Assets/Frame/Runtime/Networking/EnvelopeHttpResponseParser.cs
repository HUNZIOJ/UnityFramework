using System;
using System.Collections.Generic;
using Frame.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Frame.Networking
{
    public sealed class EnvelopeHttpResponseParser : IHttpResponseParser
    {
        private readonly HashSet<string> successCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0",
            "200",
            "OK",
            "Success"
        };

        public string SuccessField = "success";
        public string CodeField = "code";
        public string MessageField = "message";
        public string DataField = "data";
        public bool TreatMissingSuccessFieldAsSuccess = true;
        public bool TreatMissingCodeAsSuccess = true;

        public ISet<string> SuccessCodes
        {
            get { return successCodes; }
        }

        public HttpResponse<TData> Parse<TData>(HttpResponse response)
        {
            HttpResponse<TData> typed = HttpResponse<TData>.CreateFromBase(response);
            if (!typed.Success)
            {
                return typed;
            }

            if (string.IsNullOrWhiteSpace(typed.Text))
            {
                return typed;
            }

            JObject root;
            try
            {
                root = JObject.Parse(typed.Text);
            }
            catch (Exception)
            {
                return JsonHttpResponseParser.Instance.Parse<TData>(response);
            }

            if (!HasEnvelopeFields(root))
            {
                return JsonHttpResponseParser.Instance.Parse<TData>(response);
            }

            string code = ReadString(root, CodeField);
            string message = ReadString(root, MessageField);
            bool protocolSuccess = ResolveProtocolSuccess(root, code);

            typed.ErrorCode = code;
            typed.Message = message;
            typed.Success = protocolSuccess;
            if (!protocolSuccess)
            {
                typed.Error = string.IsNullOrWhiteSpace(message) ? "API error: " + code : message;
                return typed;
            }

            JToken data = string.IsNullOrWhiteSpace(DataField) ? root : root[DataField];
            if (data == null || data.Type == JTokenType.Null)
            {
                return typed;
            }

            try
            {
                if (typeof(TData) == typeof(string))
                {
                    typed.Value = (TData)(object)(data.Type == JTokenType.String ? data.Value<string>() : data.ToString(Formatting.None));
                }
                else if (typeof(TData) == typeof(byte[]))
                {
                    typed.Value = JsonConvert.DeserializeObject<TData>(data.ToString(Formatting.None));
                }
                else
                {
                    typed.Value = data.ToObject<TData>();
                }
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                typed.Success = false;
                typed.Error = "Failed to parse response envelope data: " + exception.Message;
            }

            return typed;
        }

        private bool HasEnvelopeFields(JObject root)
        {
            return HasField(root, SuccessField) ||
                   HasField(root, CodeField) ||
                   HasField(root, MessageField) ||
                   HasField(root, DataField);
        }

        private static bool HasField(JObject root, string fieldName)
        {
            return root != null && !string.IsNullOrWhiteSpace(fieldName) && root[fieldName] != null;
        }

        private bool ResolveProtocolSuccess(JObject root, string code)
        {
            JToken successToken = string.IsNullOrWhiteSpace(SuccessField) ? null : root[SuccessField];
            if (successToken != null && successToken.Type != JTokenType.Null)
            {
                if (successToken.Type == JTokenType.Boolean)
                {
                    return successToken.Value<bool>();
                }

                string successText = successToken.Value<string>();
                if (bool.TryParse(successText, out bool boolValue))
                {
                    return boolValue;
                }

                return successCodes.Contains(successText);
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                return successCodes.Contains(code);
            }

            return TreatMissingSuccessFieldAsSuccess && TreatMissingCodeAsSuccess;
        }

        private static string ReadString(JObject root, string fieldName)
        {
            if (root == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return null;
            }

            JToken token = root[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None);
        }
    }
}
