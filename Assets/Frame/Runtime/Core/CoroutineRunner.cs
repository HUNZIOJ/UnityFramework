using System.Collections;
using UnityEngine;

namespace Frame.Core
{
    public sealed class CoroutineRunner : MonoBehaviour
    {
        public Coroutine Run(IEnumerator routine)
        {
            if (routine == null)
            {
                return null;
            }

            return StartCoroutine(routine);
        }

        public void Stop(Coroutine routine)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
            }
        }
    }
}
