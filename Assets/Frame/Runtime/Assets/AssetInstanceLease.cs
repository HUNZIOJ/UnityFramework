using System;
using UnityEngine;

namespace Frame.Assets
{
    [DisallowMultipleComponent]
    public sealed class AssetInstanceLease : MonoBehaviour
    {
        private IDisposable lease;

        public void Bind(IDisposable disposable)
        {
            if (ReferenceEquals(lease, disposable))
            {
                return;
            }

            IDisposable previous = lease;
            lease = disposable;
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        private void OnDestroy()
        {
            IDisposable disposable = lease;
            lease = null;
            if (disposable != null)
            {
                disposable.Dispose();
            }
        }
    }
}
