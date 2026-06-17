namespace Frame.Networking
{
    public interface IHttpResponseParser
    {
        HttpResponse<TData> Parse<TData>(HttpResponse response);
    }
}
