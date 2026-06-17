namespace Frame.Save
{
    public readonly struct SaveLoadResult<TData>
    {
        public SaveLoadResult(bool success, TData data)
        {
            Success = success;
            Data = data;
        }

        public bool Success { get; }

        public TData Data { get; }
    }
}
