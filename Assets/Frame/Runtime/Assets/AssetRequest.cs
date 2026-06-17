using UnityEngine;
using Object = UnityEngine.Object;

namespace Frame.Assets
{
    public sealed class AssetRequest<T> : CustomYieldInstruction where T : Object
    {
        public override bool keepWaiting
        {
            get { return !IsDone; }
        }

        public bool IsDone
        {
            get;
            private set;
        }

        public bool IsCanceled
        {
            get;
            private set;
        }

        public bool Success
        {
            get { return Handle != null && Handle.IsValid; }
        }

        public float Progress
        {
            get;
            private set;
        }

        public string Error
        {
            get;
            private set;
        }

        public AssetHandle<T> Handle
        {
            get;
            private set;
        }

        public T Asset
        {
            get { return Handle == null ? null : Handle.Asset; }
        }

        public void Cancel()
        {
            if (IsDone)
            {
                return;
            }

            IsCanceled = true;
        }

        internal void SetProgress(float progress)
        {
            Progress = Mathf.Clamp01(progress);
        }

        internal void Complete(AssetHandle<T> handle, string error = null)
        {
            Handle = handle;
            Error = error;
            Progress = 1f;
            IsDone = true;
        }
    }
}
