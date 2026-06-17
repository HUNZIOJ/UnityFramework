namespace Frame.Config
{
    public interface IConfigValidator
    {
        bool Validate(out string error);
    }
}
