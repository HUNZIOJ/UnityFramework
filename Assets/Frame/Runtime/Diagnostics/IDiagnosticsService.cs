using System;
using System.Collections.Generic;
using Frame.Core;

namespace Frame.Diagnostics
{
    public interface IDiagnosticsService
    {
        event Action<FrameLogEntry> LogReceived;

        IReadOnlyList<FrameLogEntry> Logs { get; }

        DiagnosticsSnapshot CaptureSnapshot();

        IDisposable WriteLogsToFile(string filePath, long maxBytes = 1048576);

        void ClearLogs();
    }
}
