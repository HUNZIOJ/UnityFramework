namespace Frame.Save
{
    public interface ISaveEncryptor
    {
        byte[] Encrypt(byte[] bytes);

        byte[] Decrypt(byte[] bytes);
    }
}
