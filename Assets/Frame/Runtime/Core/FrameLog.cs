using UnityEngine;

namespace Frame.Core
{
    public static class FrameLog
    {
        private static bool enabled = true;
        private static FrameLogLevel minimumLevel = FrameLogLevel.Info;

        public static void Configure(FrameSettings settings)
        {
            if (settings == null)
            {
                enabled = true;
                minimumLevel = FrameLogLevel.Info;
                return;
            }

            enabled = settings.EnableLogs;
            minimumLevel = settings.MinimumLogLevel;
        }

        public static void Trace(string message)
        {
            Write(FrameLogLevel.Trace, message);
        }

        public static void Debug(string message)
        {
            Write(FrameLogLevel.Debug, message);
        }

        public static void Info(string message)
        {
            Write(FrameLogLevel.Info, message);
        }

        public static void Warning(string message)
        {
            Write(FrameLogLevel.Warning, message);
        }

        public static void Error(string message)
        {
            Write(FrameLogLevel.Error, message);
        }

        public static void Exception(System.Exception exception)
        {
            if (!enabled || minimumLevel > FrameLogLevel.Error)
            {
                return;
            }

            UnityEngine.Debug.LogException(exception);
        }

        public static void Write(FrameLogLevel level, string message)
        {
            if (!enabled || level < minimumLevel || minimumLevel == FrameLogLevel.Off)
            {
                return;
            }

            string formatted = "[Frame] " + message;
            if (level >= FrameLogLevel.Error)
            {
                UnityEngine.Debug.LogError(formatted);
            }
            else if (level == FrameLogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning(formatted);
            }
            else
            {
                UnityEngine.Debug.Log(formatted);
            }
        }
    }
}
