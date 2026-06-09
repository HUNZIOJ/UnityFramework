using UnityEngine;
using UnityEngine.Networking;

namespace Frame.Networking
{
    public sealed class HttpRequestHandle : CustomYieldInstruction
    {
        private UnityWebRequest webRequest;

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

        public HttpResponse Response
        {
            get;
            private set;
        }

        public void Cancel()
        {
            if (IsDone)
            {
                return;
            }

            IsCanceled = true;
            if (webRequest != null)
            {
                webRequest.Abort();
            }
        }

        internal void Attach(UnityWebRequest request)
        {
            webRequest = request;
            if (IsCanceled && webRequest != null)
            {
                webRequest.Abort();
            }
        }

        internal void Detach(UnityWebRequest request)
        {
            if (ReferenceEquals(webRequest, request))
            {
                webRequest = null;
            }
        }

        internal void Complete(HttpResponse response)
        {
            Response = response;
            webRequest = null;
            IsDone = true;
        }
    }
}
