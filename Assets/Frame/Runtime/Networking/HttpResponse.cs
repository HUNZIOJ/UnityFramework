namespace Frame.Networking
{
    public sealed class HttpResponse
    {
        public bool Success;
        public long StatusCode;
        public string Text;
        public byte[] Data;
        public string Error;
    }
}
