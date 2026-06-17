namespace Frame.Save
{
    public interface ISaveSerializer
    {
        string FileExtension { get; }

        byte[] Serialize<TData>(TData data);

        TData Deserialize<TData>(byte[] bytes);
    }
}
