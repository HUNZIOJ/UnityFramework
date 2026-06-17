using UnityEngine;

namespace Frame.UI
{
    [RequireComponent(typeof(RectTransform))]
    public abstract class UIPanelBase : MonoBehaviour
    {
        private bool created;

        public UIPanelContext Context
        {
            get;
            private set;
        }

        public bool IsOpen
        {
            get;
            private set;
        }

        internal void InternalCreate(UIPanelContext context)
        {
            Context = context;
            if (!created)
            {
                created = true;
                OnCreate();
            }
        }

        internal void InternalOpen(object args)
        {
            IsOpen = true;
            gameObject.SetActive(true);
            OnOpen(args);
        }

        internal bool InternalClose(bool deactivate = true)
        {
            if (!IsOpen)
            {
                return false;
            }

            IsOpen = false;
            OnClose();
            if (deactivate)
            {
                gameObject.SetActive(false);
            }

            return true;
        }

        internal void InternalSetClosed()
        {
            gameObject.SetActive(false);
        }

        internal void InternalDispose()
        {
            OnDispose();
            Context = null;
        }

        public void Close(bool destroy = false)
        {
            if (Context != null)
            {
                Context.Service.Close(this, destroy);
            }
        }

        protected virtual void OnCreate()
        {
        }

        protected virtual void OnOpen(object args)
        {
        }

        protected virtual void OnClose()
        {
        }

        protected virtual void OnDispose()
        {
        }
    }
}
