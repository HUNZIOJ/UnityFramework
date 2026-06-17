using System;
using System.IO;
using System.Text.RegularExpressions;
using Frame.Core;
using Frame.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frame.Tests.EditMode
{
    public sealed class CoreModuleTests
    {
        [Test]
        public void ServiceRegistry_RegistersResolvesUnregistersAndDisposesServices()
        {
            ServiceRegistry registry = new ServiceRegistry();
            DisposableService disposable = new DisposableService();

            registry.Register<IDisposableService>(disposable);

            Assert.AreSame(disposable, registry.Resolve<IDisposableService>());
            Assert.IsTrue(registry.TryResolve(out IDisposableService resolved));
            Assert.AreSame(disposable, resolved);

            registry.Unregister<IDisposableService>();
            Assert.IsFalse(registry.TryResolve(out resolved));
            Assert.Throws<FrameException>(() => registry.Resolve<IDisposableService>());

            registry.Register<IDisposableService>(disposable);
            registry.Register(disposable);
            registry.Clear();

            Assert.AreEqual(1, disposable.DisposeCount);
        }

        [Test]
        public void ModuleManager_OrdersLifecycleByPriorityAndShutdownReverseOrder()
        {
            ModuleManager manager = new ModuleManager();
            LifecycleRecorder recorder = new LifecycleRecorder();
            LateModule late = new LateModule(recorder);
            EarlyModule early = new EarlyModule(recorder);

            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                manager.Add(late);
                manager.Add(early);
                manager.InitializeAll(fixture.Context);
                manager.StartAll();
                manager.UpdateAll(1f, 1f);
                manager.FixedUpdateAll(0.02f, 0.02f);
                manager.LateUpdateAll(1f, 1f);
                manager.PauseAll(true);
                manager.FocusAll(false);
                manager.ApplicationQuitAll();
                manager.ShutdownAll();
            }

            CollectionAssert.AreEqual(new[]
            {
                "Early.Initialize",
                "Late.Initialize",
                "Early.Start",
                "Late.Start",
                "Early.Update",
                "Late.Update",
                "Early.FixedUpdate",
                "Late.FixedUpdate",
                "Early.LateUpdate",
                "Late.LateUpdate",
                "Early.Pause",
                "Late.Pause",
                "Early.Focus",
                "Late.Focus",
                "Early.Quit",
                "Late.Quit",
                "Late.Shutdown",
                "Early.Shutdown"
            }, recorder.Items);
        }

        [Test]
        public void ModuleManager_RejectsDuplicateConcreteModules()
        {
            ModuleManager manager = new ModuleManager();
            manager.Add(new DuplicateModule("A", 0, new LifecycleRecorder()));

            Assert.Throws<FrameException>(() => manager.Add(new DuplicateModule("B", 0, new LifecycleRecorder())));
        }

        [Test]
        public void GameModuleBase_InitializesOnceAndShutdownIsIdempotent()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                CountingModule module = new CountingModule();

                module.Initialize(fixture.Context);
                module.Initialize(fixture.Context);
                module.Shutdown();
                module.Shutdown();

                Assert.AreEqual(1, module.InitializeCount);
                Assert.AreEqual(1, module.ShutdownCount);
                Assert.IsFalse(module.IsInitialized);
            }
        }

        [Test]
        public void GameModuleBase_CleansUpWhenInitializeThrows()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                FailingModule module = new FailingModule();

                Assert.Throws<InvalidOperationException>(() => module.Initialize(fixture.Context));
                Assert.AreEqual(1, module.ShutdownCount);
                Assert.IsFalse(module.IsInitialized);
            }
        }

        [Test]
        public void Singleton_CreatesAndReleasesPlainInstance()
        {
            PlainSingleton.ReleaseInstance();
            Assert.IsFalse(PlainSingleton.HasInstance);

            PlainSingleton first = PlainSingleton.Instance;
            PlainSingleton second = PlainSingleton.Instance;

            Assert.AreSame(first, second);
            Assert.IsTrue(PlainSingleton.HasInstance);
            Assert.AreEqual(1, first.InitializeCount);

            PlainSingleton.ReleaseInstance();

            Assert.IsFalse(PlainSingleton.HasInstance);
            Assert.AreEqual(1, first.ReleaseCount);
        }

        [Test]
        public void FrameSettings_LoadOrDefaultReturnsRuntimeInstanceWhenAssetMissing()
        {
            FrameSettings settings = FrameSettings.LoadOrDefault();

            Assert.IsNotNull(settings);
            Assert.Greater(settings.UIReferenceResolution.x, 0f);
            Assert.Greater(settings.AudioSourcePoolSize, 0);
        }

        [Test]
        public void FrameLog_AllLevelsAreSafeToCall()
        {
            FrameLog.Configure(null);
            LogAssert.Expect(LogType.Warning, "[Frame] warning");
            LogAssert.Expect(LogType.Error, "[Frame] error");
            LogAssert.Expect(LogType.Exception, new Regex("Exception: test"));

            Assert.DoesNotThrow(() => FrameLog.Trace("trace"));
            Assert.DoesNotThrow(() => FrameLog.Debug("debug"));
            Assert.DoesNotThrow(() => FrameLog.Info("info"));
            Assert.DoesNotThrow(() => FrameLog.Warning("warning"));
            Assert.DoesNotThrow(() => FrameLog.Error("error"));
            Assert.DoesNotThrow(() => FrameLog.Exception(new Exception("test")));
        }

        [Test]
        public void FrameLog_BuffersAndPublishesEntries()
        {
            FrameLog.Configure(null);
            FrameLog.ClearBufferedEntries();
            int previousMax = FrameLog.MaxBufferedEntries;
            FrameLog.MaxBufferedEntries = 2;
            FrameLogEntry received = null;
            Action<FrameLogEntry> handler = entry => received = entry;
            FrameLog.EntryWritten += handler;
            try
            {
                FrameLog.Info("one");
                FrameLog.Info("two");
                FrameLog.Info("three");

                Assert.AreEqual(2, FrameLog.BufferedEntries.Count);
                Assert.AreEqual("two", FrameLog.BufferedEntries[0].Message);
                Assert.AreEqual("three", FrameLog.BufferedEntries[1].Message);
                Assert.AreEqual("three", received.Message);
            }
            finally
            {
                FrameLog.EntryWritten -= handler;
                FrameLog.MaxBufferedEntries = previousMax;
                FrameLog.ClearBufferedEntries();
            }
        }

        [Test]
        public void DiagnosticsService_CapturesLogCountsAndSnapshot()
        {
            FrameLog.Configure(null);
            FrameLog.ClearBufferedEntries();
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                DiagnosticsService service = fixture.Initialize(new DiagnosticsService());
                int receivedCount = 0;
                service.LogReceived += entry => receivedCount++;

                LogAssert.Expect(LogType.Warning, "[Frame] diagnostics warning");
                LogAssert.Expect(LogType.Error, "[Frame] diagnostics error");
                FrameLog.Warning("diagnostics warning");
                FrameLog.Error("diagnostics error");

                DiagnosticsSnapshot snapshot = service.CaptureSnapshot();

                Assert.AreEqual(2, receivedCount);
                Assert.GreaterOrEqual(snapshot.BufferedLogCount, 2);
                Assert.AreEqual(1, snapshot.WarningCount);
                Assert.AreEqual(1, snapshot.ErrorCount);
                Assert.Greater(snapshot.ManagedMemoryBytes, 0);

                service.ClearLogs();
                Assert.AreEqual(0, service.CaptureSnapshot().BufferedLogCount);
                service.Shutdown();
            }
        }

        [Test]
        public void FileLogSink_WritesEntriesToDisk()
        {
            FrameLog.Configure(null);
            FrameLog.ClearBufferedEntries();
            string path = CreateTempLogPath();

            try
            {
                using (new FileLogSink(path))
                {
                    FrameLog.Info("file sink test");
                }

                string contents = File.ReadAllText(path);
                StringAssert.Contains("file sink test", contents);
                StringAssert.Contains("[Info]", contents);
            }
            finally
            {
                DeleteFileIfExists(path);
                DeleteFileIfExists(path + ".bak");
                FrameLog.ClearBufferedEntries();
            }
        }

        [Test]
        public void FileLogSink_RotatesWhenMaxBytesExceeded()
        {
            FrameLog.Configure(null);
            FrameLog.ClearBufferedEntries();
            string path = CreateTempLogPath();

            try
            {
                File.WriteAllText(path, new string('x', 128));

                using (new FileLogSink(path, maxBytes: 64))
                {
                    FrameLog.Info("rotated entry");
                }

                Assert.IsTrue(File.Exists(path + ".bak"));
                StringAssert.Contains("rotated entry", File.ReadAllText(path));
                StringAssert.Contains(new string('x', 32), File.ReadAllText(path + ".bak"));
            }
            finally
            {
                DeleteFileIfExists(path);
                DeleteFileIfExists(path + ".bak");
                FrameLog.ClearBufferedEntries();
            }
        }

        [Test]
        public void DiagnosticsService_WriteLogsToFileReturnsDisposableSink()
        {
            FrameLog.Configure(null);
            FrameLog.ClearBufferedEntries();
            string path = CreateTempLogPath();

            try
            {
                using (FrameTestFixture fixture = new FrameTestFixture())
                {
                    DiagnosticsService service = fixture.Initialize(new DiagnosticsService());
                    IDisposable sink = service.WriteLogsToFile(path);

                    FrameLog.Info("service file sink");
                    sink.Dispose();
                    FrameLog.Info("after sink disposed");

                    string contents = File.ReadAllText(path);
                    StringAssert.Contains("service file sink", contents);
                    Assert.IsFalse(contents.Contains("after sink disposed"));

                    service.Shutdown();
                }
            }
            finally
            {
                DeleteFileIfExists(path);
                DeleteFileIfExists(path + ".bak");
                FrameLog.ClearBufferedEntries();
            }
        }

        private static string CreateTempLogPath()
        {
            return Path.Combine(Path.GetTempPath(), "frame-log-" + Guid.NewGuid().ToString("N") + ".log");
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private interface IDisposableService : IDisposable
        {
            int DisposeCount { get; }
        }

        private sealed class DisposableService : IDisposableService
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class LifecycleRecorder
        {
            public readonly System.Collections.Generic.List<string> Items = new System.Collections.Generic.List<string>();
        }

        private abstract class RecordingModule : GameModuleBase
        {
            private readonly string moduleName;
            private readonly int priority;
            private readonly LifecycleRecorder recorder;

            protected RecordingModule(string moduleName, int priority, LifecycleRecorder recorder)
            {
                this.moduleName = moduleName;
                this.priority = priority;
                this.recorder = recorder;
            }

            public override string Name { get { return moduleName; } }

            public override int Priority { get { return priority; } }

            protected override void OnInitialize() { recorder.Items.Add(moduleName + ".Initialize"); }

            public override void Start() { recorder.Items.Add(moduleName + ".Start"); }

            public override void Update(float deltaTime, float unscaledDeltaTime) { recorder.Items.Add(moduleName + ".Update"); }

            public override void FixedUpdate(float fixedDeltaTime, float fixedUnscaledDeltaTime) { recorder.Items.Add(moduleName + ".FixedUpdate"); }

            public override void LateUpdate(float deltaTime, float unscaledDeltaTime) { recorder.Items.Add(moduleName + ".LateUpdate"); }

            public override void OnApplicationPause(bool paused) { recorder.Items.Add(moduleName + ".Pause"); }

            public override void OnApplicationFocus(bool focused) { recorder.Items.Add(moduleName + ".Focus"); }

            public override void OnApplicationQuit() { recorder.Items.Add(moduleName + ".Quit"); }

            protected override void OnShutdown() { recorder.Items.Add(moduleName + ".Shutdown"); }
        }

        private sealed class EarlyModule : RecordingModule
        {
            public EarlyModule(LifecycleRecorder recorder)
                : base("Early", -10, recorder)
            {
            }
        }

        private sealed class LateModule : RecordingModule
        {
            public LateModule(LifecycleRecorder recorder)
                : base("Late", 10, recorder)
            {
            }
        }

        private sealed class DuplicateModule : RecordingModule
        {
            public DuplicateModule(string moduleName, int priority, LifecycleRecorder recorder)
                : base(moduleName, priority, recorder)
            {
            }
        }

        private sealed class CountingModule : GameModuleBase
        {
            public int InitializeCount { get; private set; }

            public int ShutdownCount { get; private set; }

            protected override void OnInitialize() { InitializeCount++; }

            protected override void OnShutdown() { ShutdownCount++; }
        }

        private sealed class FailingModule : GameModuleBase
        {
            public int ShutdownCount { get; private set; }

            protected override void OnInitialize()
            {
                throw new InvalidOperationException("fail");
            }

            protected override void OnShutdown()
            {
                ShutdownCount++;
            }
        }

        private sealed class PlainSingleton : Singleton<PlainSingleton>
        {
            private PlainSingleton()
            {
            }

            public int InitializeCount { get; private set; }

            public int ReleaseCount { get; private set; }

            protected override void OnSingletonInitialize()
            {
                InitializeCount++;
            }

            protected override void OnSingletonRelease()
            {
                ReleaseCount++;
            }
        }
    }
}
