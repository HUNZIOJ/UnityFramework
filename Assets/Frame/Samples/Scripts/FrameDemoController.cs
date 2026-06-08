using System;
using Frame.Core;
using Frame.Events;
using Frame.Save;
using Frame.Timing;
using UnityEngine;

namespace Frame.Samples
{
    public sealed class FrameDemoController : MonoBehaviour
    {
        private IDisposable eventSubscription;
        private TimerHandle timer;

        private void Start()
        {
            if (!Framework.IsInitialized)
            {
                return;
            }

            IEventBus eventBus = Framework.Resolve<IEventBus>();
            eventSubscription = eventBus.Subscribe<DemoEvent>(OnDemoEvent, this);
            eventBus.Publish(new DemoEvent { Message = "Frame demo event published." });

            TimerService timers = Framework.Resolve<TimerService>();
            timer = timers.Delay(1f, SaveDemoData, true, this);
        }

        private void OnDestroy()
        {
            if (eventSubscription != null)
            {
                eventSubscription.Dispose();
                eventSubscription = null;
            }

            timer.Cancel();
        }

        private void OnDemoEvent(DemoEvent demoEvent)
        {
            Debug.Log(demoEvent.Message);
        }

        private void SaveDemoData()
        {
            SaveService saveService;
            if (!Framework.TryResolve(out saveService))
            {
                return;
            }

            saveService.Save("demo", new DemoSaveData
            {
                PlayerName = "Player",
                Level = 1,
                SavedAt = DateTime.UtcNow.ToString("O")
            });
        }

        private struct DemoEvent
        {
            public string Message;
        }

        [Serializable]
        private sealed class DemoSaveData
        {
            public string PlayerName;
            public int Level;
            public string SavedAt;
        }
    }
}
