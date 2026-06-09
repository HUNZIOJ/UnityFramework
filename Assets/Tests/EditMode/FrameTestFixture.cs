using System;
using Frame.Core;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Frame.Tests.EditMode
{
    internal sealed class FrameTestFixture : IDisposable
    {
        private readonly GameObject root;

        public FrameTestFixture(string name = "FrameTest")
        {
            root = new GameObject(name);
            Settings = ScriptableObject.CreateInstance<FrameSettings>();
            Services = new ServiceRegistry();
            Context = new FrameContext(Entry, Settings, Services, root.transform);
        }

        public GameEntry Entry { get; private set; }

        public FrameSettings Settings { get; private set; }

        public ServiceRegistry Services { get; private set; }

        public FrameContext Context { get; private set; }

        public TModule Initialize<TModule>(TModule module) where TModule : GameModuleBase
        {
            module.Initialize(Context);
            return module;
        }

        public void Dispose()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }

            if (Settings != null)
            {
                Object.DestroyImmediate(Settings);
            }
        }
    }

    internal static class AssertEx
    {
        public static void DoesNotThrowTwice(Action action)
        {
            Assert.DoesNotThrow(() => action());
            Assert.DoesNotThrow(() => action());
        }

        public static void WithFrameLogsOff(Action action)
        {
            FrameSettings settings = ScriptableObject.CreateInstance<FrameSettings>();
            try
            {
                SetPrivateField(settings, "minimumLogLevel", FrameLogLevel.Off);
                FrameLog.Configure(settings);
                action();
            }
            finally
            {
                FrameLog.Configure(null);
                Object.DestroyImmediate(settings);
            }
        }

        private static void SetPrivateField<TValue>(object target, string fieldName, TValue value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field.SetValue(target, value);
        }
    }
}
