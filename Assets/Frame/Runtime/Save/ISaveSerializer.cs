namespace Frame.Save
{
    public interface ISaveSerializer
    {
        string Serialize<TData>(TData data);

        TData Deserialize<TData>(string text);
    }
}
