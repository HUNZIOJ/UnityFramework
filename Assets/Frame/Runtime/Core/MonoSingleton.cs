using UnityEngine;

namespace Frame.Core
{
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T instance;
        private static bool isApplicationQuitting;

        public static T Instance
        {
            get { return instance; }
        }

        public static bool HasInstance
        {
            get { return instance != null; }
        }

        protected static bool IsApplicationQuitting
        {
            get { return isApplicationQuitting; }
        }

        protected virtual bool UseDontDestroyOnLoad
        {
            get { return false; }
        }

        public static T GetOrCreate()
        {
            if (instance != null)
            {
                return instance;
            }

            T existing = FindExistingInstance();
            if (existing != null)
            {
                return existing;
            }

            GameObject go = new GameObject(typeof(T).Name);
            return go.AddComponent<T>();
        }

        protected static T FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
#else
            return Object.FindObjectOfType<T>(true);
#endif
        }

        protected virtual void Awake()
        {
            if (instance != null && instance != this)
            {
                OnDuplicateInstance(instance);
                Destroy(gameObject);
                return;
            }

            instance = (T)this;
            OnSingletonAwake();
            if (UseDontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        protected virtual void OnApplicationQuit()
        {
            isApplicationQuitting = true;
            OnSingletonApplicationQuit();
        }

        protected virtual void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            OnSingletonDestroyed();
            instance = null;
        }

        protected virtual void OnSingletonAwake()
        {
        }

        protected virtual void OnSingletonApplicationQuit()
        {
        }

        protected virtual void OnSingletonDestroyed()
        {
        }

        protected virtual void OnDuplicateInstance(T current)
        {
        }
    }
}
