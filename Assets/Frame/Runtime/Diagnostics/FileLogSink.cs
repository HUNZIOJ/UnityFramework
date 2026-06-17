using System;
using System.Globalization;
using System.IO;
using System.Text;
using Frame.Core;
using UnityEngine;

namespace Frame.Diagnostics
{
    public sealed class FileLogSink : IDisposable
    {
        private const long DefaultMaxBytes = 1024 * 1024;

        private readonly object syncRoot = new object();
        private bool disposed;

        public FileLogSink(string filePath, long maxBytes = DefaultMaxBytes)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Log file path is required.", nameof(filePath));
            }

            FilePath = Path.GetFullPath(filePath);
            MaxBytes = Math.Max(1, maxBytes);
            string directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            FrameLog.EntryWritten += OnEntryWritten;
        }

        public string FilePath { get; private set; }

        public long MaxBytes { get; private set; }

        public string BackupFilePath
        {
            get { return FilePath + ".bak"; }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            FrameLog.EntryWritten -= OnEntryWritten;
        }

        private void OnEntryWritten(FrameLogEntry entry)
        {
            if (disposed || entry == null)
            {
                return;
            }

            try
            {
                string line = Format(entry);
                lock (syncRoot)
                {
                    if (disposed)
                    {
                        return;
                    }

                    RotateIfNeeded(Encoding.UTF8.GetByteCount(line));
                    File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void RotateIfNeeded(int pendingBytes)
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            FileInfo fileInfo = new FileInfo(FilePath);
            if (fileInfo.Length + pendingBytes <= MaxBytes)
            {
                return;
            }

            if (File.Exists(BackupFilePath))
            {
                File.Delete(BackupFilePath);
            }

            File.Move(FilePath, BackupFilePath);
        }

        private static string Format(FrameLogEntry entry)
        {
            string message = string.IsNullOrEmpty(entry.FormattedMessage)
                ? entry.Message
                : entry.FormattedMessage;
            string exceptionText = FormatException(entry.Exception);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:o} [{1}] {2}{3}{4}",
                entry.UtcTime,
                entry.Level,
                Sanitize(message),
                exceptionText,
                Environment.NewLine);
        }

        private static string FormatException(Exception exception)
        {
            if (exception == null)
            {
                return string.Empty;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                " | {0}: {1}",
                exception.GetType().FullName,
                Sanitize(exception.Message));
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
