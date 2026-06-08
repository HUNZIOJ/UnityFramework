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

        public AssetHandle<T> Handle
        {
            get;
            private set;
        }

        public T Asset
        {
            get { return Handle == null ? null : Handle.Asset; }
        }

        internal void Complete(AssetHandle<T> handle)
        {
            Handle = handle;
            IsDone = true;
        }
    }
}
