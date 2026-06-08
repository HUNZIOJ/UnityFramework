namespace Frame.Core
{
    public abstract class GameModuleBase : IFrameModule
    {
        private FrameContext context;

        public virtual string Name
        {
            get { return GetType().Name; }
        }

        public virtual int Priority
        {
            get { return 0; }
        }

        public bool IsInitialized
        {
            get;
            private set;
        }

        protected FrameContext Context
        {
            get { return context; }
        }

        public void Initialize(FrameContext frameContext)
        {
            if (IsInitialized)
            {
                return;
            }

            context = frameContext;
            OnInitialize();
            IsInitialized = true;
        }

        public virtual void Start()
        {
        }

        public virtual void Update(float deltaTime, float unscaledDeltaTime)
        {
        }

        public virtual void FixedUpdate(float fixedDeltaTime, float fixedUnscaledDeltaTime)
        {
        }

        public virtual void LateUpdate(float deltaTime, float unscaledDeltaTime)
        {
        }

        public virtual void OnApplicationPause(bool paused)
        {
        }

        public virtual void OnApplicationFocus(bool focused)
        {
        }

        public void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            OnShutdown();
            IsInitialized = false;
            context = null;
        }

        protected virtual void OnInitialize()
        {
        }

        protected virtual void OnShutdown()
        {
        }
    }
}
