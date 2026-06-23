using System.Reflection;

namespace Frame.HotUpdate
{
    public sealed class HotUpdateLoadResult
    {
        public bool Success { get; internal set; }

        public string Name { get; internal set; }

        public Assembly Assembly { get; internal set; }

        public string Error { get; internal set; }

        public string ErrorCode { get; internal set; }

        public static HotUpdateLoadResult Succeeded(string name, Assembly assembly = null)
        {
            return new HotUpdateLoadResult
            {
                Success = true,
                Name = name,
                Assembly = assembly
            };
        }

        public static HotUpdateLoadResult Failed(string name, string error, string errorCode = null)
        {
            return new HotUpdateLoadResult
            {
                Success = false,
                Name = name,
                Error = error,
                ErrorCode = errorCode
            };
        }
    }
}
