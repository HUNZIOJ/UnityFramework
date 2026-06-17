namespace Frame.Core
{
    public interface IFrameModule
    {
        string Name { get; }

        int Priority { get; }

        bool IsInitialized { get; }

        void Initialize(FrameContext context);

        void Start();

        void Update(float deltaTime, float unscaledDeltaTime);

        void FixedUpdate(float fixedDeltaTime, float fixedUnscaledDeltaTime);

        void LateUpdate(float deltaTime, float unscaledDeltaTime);

        void OnApplicationPause(bool paused);

        void OnApplicationFocus(bool focused);

        void OnApplicationQuit();

        void Shutdown();
    }
}
