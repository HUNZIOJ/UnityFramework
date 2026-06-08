using System;
using System.Collections.Generic;

namespace Frame.Core
{
    public sealed class ServiceRegistry
    {
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();

        public void Register<TService>(TService service) where TService : class
        {
            if (service == null)
            {
                throw new ArgumentNullException("service");
            }

            services[typeof(TService)] = service;
        }

        public bool TryResolve<TService>(out TService service) where TService : class
        {
            object value;
            if (services.TryGetValue(typeof(TService), out value))
            {
                service = value as TService;
                return service != null;
            }

            service = null;
            return false;
        }

        public TService Resolve<TService>() where TService : class
        {
            TService service;
            if (TryResolve(out service))
            {
                return service;
            }

            throw new FrameException("Service is not registered: " + typeof(TService).FullName);
        }

        public void Unregister<TService>() where TService : class
        {
            services.Remove(typeof(TService));
        }

        public void Clear()
        {
            List<object> disposed = new List<object>();
            foreach (object service in services.Values)
            {
                bool alreadyDisposed = false;
                for (int i = 0; i < disposed.Count; i++)
                {
                    if (ReferenceEquals(disposed[i], service))
                    {
                        alreadyDisposed = true;
                        break;
                    }
                }

                if (alreadyDisposed)
                {
                    continue;
                }

                IDisposable disposable = service as IDisposable;
                if (disposable != null)
                {
                    disposable.Dispose();
                    disposed.Add(service);
                }
            }

            services.Clear();
        }
    }
}
