using System;
using System.Collections.Generic;
using Frame.Assets;
using Frame.Core;
using Frame.Lifecycle;
using Frame.Networking;
using Frame.Pooling;
using Frame.Scenes;
using Frame.Timing;
using UnityEngine;

namespace Frame.Diagnostics
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Frame/Diagnostics/Runtime Diagnostics Overlay")]
    public sealed class RuntimeDiagnosticsOverlay : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private KeyCode toggleKey = KeyCode.BackQuote;
        [SerializeField] private int maxLogLines = 12;
        [SerializeField] private float width = 520f;
        [SerializeField] private float refreshInterval = 0.25f;

        private IDiagnosticsService diagnostics;
        private ILifecycleService lifecycle;
        private IHttpService http;
        private ISocketService sockets;
        private IAssetService assets;
        private IPoolService pools;
        private ISceneService scenes;
        private ITimerService timers;
        private Vector2 scroll;
        private float nextRefreshTime;
        private DiagnosticsSnapshot snapshot;
        private List<PoolStats> poolStats = new List<PoolStats>();
        private List<AssetStats> assetStats = new List<AssetStats>();

        public bool Visible
        {
            get { return visible; }
            set { visible = value; }
        }

        public KeyCode ToggleKey
        {
            get { return toggleKey; }
            set { toggleKey = value; }
        }

        public static RuntimeDiagnosticsOverlay Ensure(Transform parent, bool visibleAtStart, KeyCode toggleKey)
        {
            RuntimeDiagnosticsOverlay existing = parent == null ? null : parent.GetComponentInChildren<RuntimeDiagnosticsOverlay>(true);
            if (existing != null)
            {
                existing.Configure(visibleAtStart, toggleKey);
                return existing;
            }

            GameObject go = new GameObject("RuntimeDiagnosticsOverlay", typeof(RuntimeDiagnosticsOverlay));
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            RuntimeDiagnosticsOverlay overlay = go.GetComponent<RuntimeDiagnosticsOverlay>();
            overlay.Configure(visibleAtStart, toggleKey);
            return overlay;
        }

        public void Configure(bool visibleAtStart, KeyCode key)
        {
            visible = visibleAtStart;
            toggleKey = key;
            RefreshServices();
            RefreshSnapshot(true);
        }

        private void Update()
        {
            if (toggleKey != KeyCode.None && UnityEngine.Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
            }

            if (visible)
            {
                RefreshSnapshot(false);
            }
        }

        private void OnEnable()
        {
            RefreshServices();
            RefreshSnapshot(true);
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            float panelWidth = Mathf.Clamp(width, 280f, Mathf.Max(280f, Screen.width - 20f));
            GUILayout.BeginArea(new Rect(10f, 10f, panelWidth, Screen.height - 20f), "Frame Diagnostics", GUI.skin.window);
            scroll = GUILayout.BeginScrollView(scroll);

            DrawSnapshot();
            DrawLifecycle();
            DrawHttp();
            DrawSockets();
            DrawTimers();
            DrawScenes();
            DrawAssets();
            DrawPools();
            DrawLogs();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSnapshot()
        {
            GUILayout.Label("Runtime");
            if (snapshot == null)
            {
                GUILayout.Label("Diagnostics service is not available.");
                return;
            }

            GUILayout.Label("Frame: " + snapshot.FrameCount);
            GUILayout.Label("FPS: " + snapshot.AverageFps.ToString("0.0"));
            GUILayout.Label("Managed Memory: " + FormatBytes(snapshot.ManagedMemoryBytes));
            GUILayout.Label("Allocated Memory: " + FormatBytes(snapshot.TotalAllocatedMemoryBytes));
            GUILayout.Label("Logs: " + snapshot.BufferedLogCount + "  Warnings: " + snapshot.WarningCount + "  Errors: " + snapshot.ErrorCount + "  Exceptions: " + snapshot.ExceptionCount);
        }

        private void DrawHttp()
        {
            GUILayout.Space(8f);
            GUILayout.Label("HTTP");
            if (http == null)
            {
                GUILayout.Label("HTTP service is not available.");
                return;
            }

            GUILayout.Label("Active: " + http.ActiveRequestCount + "  Started: " + http.StartedRequestCount + "  Completed: " + http.CompletedRequestCount + "  Failed: " + http.FailedRequestCount);
        }

        private void DrawSockets()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Sockets");
            if (sockets == null)
            {
                GUILayout.Label("Socket service is not available.");
                return;
            }

            GUILayout.Label("Clients: " + sockets.Clients.Count + "  Active: " + sockets.ActiveConnectionCount);
            for (int i = 0; i < sockets.Clients.Count; i++)
            {
                ISocketClient client = sockets.Clients[i];
                if (client == null)
                {
                    continue;
                }

                SocketClientMetrics metrics = client.Metrics;
                GUILayout.Label(client.Id + "  " + client.Options.Transport + "  " + client.State + "  sent=" + metrics.SentMessages + " recv=" + metrics.ReceivedMessages + " drop=" + metrics.DroppedMessages);
            }
        }

        private void DrawLifecycle()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Lifecycle");
            if (lifecycle == null)
            {
                GUILayout.Label("Lifecycle service is not available.");
                return;
            }

            GUILayout.Label("Paused: " + lifecycle.IsPaused + "  Focus: " + lifecycle.HasFocus + "  Quitting: " + lifecycle.IsQuitting);
        }

        private void DrawPools()
        {
            GUILayout.Space(8f);
            GUILayout.Label("GameObject Pools");
            if (pools == null)
            {
                GUILayout.Label("Pool service is not available.");
                return;
            }

            if (poolStats.Count == 0)
            {
                GUILayout.Label("No pools.");
                return;
            }

            for (int i = 0; i < poolStats.Count; i++)
            {
                PoolStats stats = poolStats[i];
                if (stats == null)
                {
                    continue;
                }

                GUILayout.Label(stats.Key + "  active=" + stats.CountActive + " inactive=" + stats.CountInactive + " created=" + stats.CreatedCount + " destroyed=" + stats.DestroyedCount);
            }
        }

        private void DrawTimers()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Timers");
            if (timers == null)
            {
                GUILayout.Label("Timer service is not available.");
                return;
            }

            string pausedText = timers.IsPaused ? " paused" : string.Empty;
            GUILayout.Label("Active: " + timers.ActiveTimerCount + "  scaled=" + timers.ScaledTimerCount + " unscaled=" + timers.UnscaledTimerCount + pausedText);
        }

        private void DrawScenes()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Scenes");
            if (scenes == null)
            {
                GUILayout.Label("Scene service is not available.");
                return;
            }

            UnityEngine.SceneManagement.Scene activeScene = scenes.ActiveScene;
            GUILayout.Label("Active: " + (activeScene.IsValid() ? activeScene.name : "none"));

            SceneLoadOperation operation = scenes.CurrentOperation;
            if (operation == null)
            {
                GUILayout.Label("Loading: " + scenes.IsLoading);
                return;
            }

            GUILayout.Label("Loading: " + scenes.IsLoading + "  scene=" + operation.SceneName + " progress=" + operation.NormalizedProgress.ToString("0%") + " ready=" + operation.IsReadyToActivate);
        }

        private void DrawAssets()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Assets");
            if (assets == null)
            {
                GUILayout.Label("Asset service is not available.");
                return;
            }

            if (assetStats.Count == 0)
            {
                GUILayout.Label("No loaded assets.");
                return;
            }

            for (int i = 0; i < assetStats.Count; i++)
            {
                AssetStats stats = assetStats[i];
                if (stats == null)
                {
                    continue;
                }

                GUILayout.Label(stats.Path + "  refs=" + stats.ReferenceCount + " type=" + stats.TypeName);
            }
        }

        private void DrawLogs()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Recent Logs");
            if (diagnostics == null)
            {
                GUILayout.Label("Diagnostics service is not available.");
                return;
            }

            IReadOnlyList<FrameLogEntry> logs = diagnostics.Logs;
            int start = Mathf.Max(0, logs.Count - Mathf.Max(1, maxLogLines));
            for (int i = start; i < logs.Count; i++)
            {
                FrameLogEntry entry = logs[i];
                if (entry == null)
                {
                    continue;
                }

                GUILayout.Label("[" + entry.Level + "] " + entry.Message);
            }
        }

        private void RefreshServices()
        {
            Framework.TryResolve(out diagnostics);
            Framework.TryResolve(out lifecycle);
            Framework.TryResolve(out http);
            Framework.TryResolve(out sockets);
            Framework.TryResolve(out assets);
            Framework.TryResolve(out pools);
            Framework.TryResolve(out scenes);
            Framework.TryResolve(out timers);
        }

        private void RefreshSnapshot(bool force)
        {
            if (!force && Time.unscaledTime < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
            RefreshServices();
            snapshot = diagnostics == null ? null : diagnostics.CaptureSnapshot();
            assetStats = assets == null ? new List<AssetStats>() : assets.GetLoadedAssetStats();
            poolStats = pools == null ? new List<PoolStats>() : pools.GetAllGameObjectPoolStats();
        }

        private static string FormatBytes(long bytes)
        {
            const float mb = 1024f * 1024f;
            return (bytes / mb).ToString("0.0") + " MB";
        }
    }
}
