using System;
using System.Collections.Generic;
using UnityEngine;

namespace Frame.Core
{
    public static class FrameLog
    {
        private const int DefaultMaxBufferedEntries = 256;

        private static readonly List<FrameLogEntry> bufferedEntries = new List<FrameLogEntry>();
        private static bool enabled = true;
        private static FrameLogLevel minimumLevel = FrameLogLevel.Info;
        private static int maxBufferedEntries = DefaultMaxBufferedEntries;

        public static event Action<FrameLogEntry> EntryWritten;

        public static IReadOnlyList<FrameLogEntry> BufferedEntries
        {
            get { return bufferedEntries; }
        }

        public static int MaxBufferedEntries
        {
            get { return maxBufferedEntries; }
            set { maxBufferedEntries = Mathf.Max(0, value); TrimBuffer(); }
        }

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

        public static void ClearBufferedEntries()
        {
            bufferedEntries.Clear();
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

            string message = exception == null ? "Exception" : exception.Message;
            string formatted = "[Frame] " + message;
            Publish(new FrameLogEntry(FrameLogLevel.Error, message, formatted, exception));
            if (exception != null)
            {
                UnityEngine.Debug.LogException(exception);
            }
            else
            {
                UnityEngine.Debug.LogError(formatted);
            }
        }

        public static void Write(FrameLogLevel level, string message)
        {
            if (!enabled || level < minimumLevel || minimumLevel == FrameLogLevel.Off)
            {
                return;
            }

            string formatted = "[Frame] " + message;
            Publish(new FrameLogEntry(level, message, formatted));
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

        private static void Publish(FrameLogEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (maxBufferedEntries > 0)
            {
                bufferedEntries.Add(entry);
                TrimBuffer();
            }

            Action<FrameLogEntry> handler = EntryWritten;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(entry);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        private static void TrimBuffer()
        {
            if (maxBufferedEntries <= 0)
            {
                bufferedEntries.Clear();
                return;
            }

            int overflow = bufferedEntries.Count - maxBufferedEntries;
            if (overflow > 0)
            {
                bufferedEntries.RemoveRange(0, overflow);
            }
        }
    }
}
