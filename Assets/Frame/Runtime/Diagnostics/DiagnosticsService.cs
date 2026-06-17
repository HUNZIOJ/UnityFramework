using System;
using System.Collections.Generic;
using Frame.Core;
using Frame.Utilities;
using UnityEngine;
using UnityEngine.Profiling;

namespace Frame.Diagnostics
{
    public sealed class DiagnosticsService : GameModuleBase, IDiagnosticsService
    {
        private int warningCount;
        private int errorCount;
        private int exceptionCount;
        private float sampleElapsed;
        private int sampleFrames;
        private float averageFps;
        private readonly List<IDisposable> logSinks = new List<IDisposable>();

        public event Action<FrameLogEntry> LogReceived;

        public override int Priority
        {
            get { return -1000; }
        }

        public IReadOnlyList<FrameLogEntry> Logs
        {
            get { return FrameLog.BufferedEntries; }
        }

        protected override void OnInitialize()
        {
            RecalculateLogCounts();
            FrameLog.EntryWritten += OnLogEntryWritten;
            Context.Services.Register<IDiagnosticsService>(this);
            Context.Services.Register(this);
        }

        public override void Update(float deltaTime, float unscaledDeltaTime)
        {
            sampleElapsed += unscaledDeltaTime;
            sampleFrames++;
            if (sampleElapsed >= 0.5f)
            {
                averageFps = sampleFrames / sampleElapsed;
                sampleElapsed = 0f;
                sampleFrames = 0;
            }
        }

        public DiagnosticsSnapshot CaptureSnapshot()
        {
            return new DiagnosticsSnapshot
            {
                FrameCount = Time.frameCount,
                RealtimeSinceStartup = Time.realtimeSinceStartup,
                UnscaledTime = Time.unscaledTime,
                DeltaTime = Time.deltaTime,
                AverageFps = averageFps,
                ManagedMemoryBytes = GC.GetTotalMemory(false),
                TotalAllocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong(),
                BufferedLogCount = FrameLog.BufferedEntries.Count,
                WarningCount = warningCount,
                ErrorCount = errorCount,
                ExceptionCount = exceptionCount
            };
        }

        public IDisposable WriteLogsToFile(string filePath, long maxBytes = 1048576)
        {
            FileLogSink sink = new FileLogSink(filePath, maxBytes);
            logSinks.Add(sink);
            return new DisposableAction(() =>
            {
                sink.Dispose();
                logSinks.Remove(sink);
            });
        }

        public void ClearLogs()
        {
            FrameLog.ClearBufferedEntries();
            warningCount = 0;
            errorCount = 0;
            exceptionCount = 0;
        }

        protected override void OnShutdown()
        {
            FrameLog.EntryWritten -= OnLogEntryWritten;
            DisposeLogSinks();
            warningCount = 0;
            errorCount = 0;
            exceptionCount = 0;
            sampleElapsed = 0f;
            sampleFrames = 0;
            averageFps = 0f;
        }

        private void DisposeLogSinks()
        {
            for (int i = logSinks.Count - 1; i >= 0; i--)
            {
                try
                {
                    logSinks[i].Dispose();
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                }
            }

            logSinks.Clear();
        }

        private void OnLogEntryWritten(FrameLogEntry entry)
        {
            Count(entry);
            Action<FrameLogEntry> handler = LogReceived;
            if (handler != null)
            {
                try
                {
                    handler(entry);
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                }
            }
        }

        private void RecalculateLogCounts()
        {
            warningCount = 0;
            errorCount = 0;
            exceptionCount = 0;
            IReadOnlyList<FrameLogEntry> entries = FrameLog.BufferedEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                Count(entries[i]);
            }
        }

        private void Count(FrameLogEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.Level == FrameLogLevel.Warning)
            {
                warningCount++;
            }
            else if (entry.Level >= FrameLogLevel.Error)
            {
                errorCount++;
            }

            if (entry.Exception != null)
            {
                exceptionCount++;
            }
        }
    }
}
