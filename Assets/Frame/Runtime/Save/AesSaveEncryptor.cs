using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Frame.Save
{
    public sealed class AesSaveEncryptor : ISaveEncryptor
    {
        private static readonly byte[] Header = { 70, 83, 65, 69, 83, 1 };

        private readonly byte[] key;

        public AesSaveEncryptor(string passphrase)
            : this(HashPassphrase(passphrase))
        {
        }

        public AesSaveEncryptor(byte[] key)
        {
            if (key == null || key.Length == 0)
            {
                throw new ArgumentException("Encryption key is required.", "key");
            }

            this.key = NormalizeKey(key);
        }

        public byte[] Encrypt(byte[] bytes)
        {
            if (bytes == null)
            {
                bytes = Array.Empty<byte>();
            }

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.GenerateIV();

                using (MemoryStream output = new MemoryStream())
                {
                    output.Write(Header, 0, Header.Length);
                    output.WriteByte((byte)aes.IV.Length);
                    output.Write(aes.IV, 0, aes.IV.Length);

                    using (CryptoStream cryptoStream = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(bytes, 0, bytes.Length);
                    }

                    return output.ToArray();
                }
            }
        }

        public byte[] Decrypt(byte[] bytes)
        {
            if (bytes == null || bytes.Length <= Header.Length + 1)
            {
                throw new InvalidDataException("Encrypted save data is invalid.");
            }

            int offset = 0;
            for (int i = 0; i < Header.Length; i++)
            {
                if (bytes[i] != Header[i])
                {
                    throw new InvalidDataException("Encrypted save data header is invalid.");
                }
            }

            offset += Header.Length;
            int ivLength = bytes[offset];
            offset++;
            if (ivLength <= 0 || bytes.Length <= offset + ivLength)
            {
                throw new InvalidDataException("Encrypted save data IV is invalid.");
            }

            byte[] iv = new byte[ivLength];
            Buffer.BlockCopy(bytes, offset, iv, 0, ivLength);
            offset += ivLength;

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;

                using (MemoryStream input = new MemoryStream(bytes, offset, bytes.Length - offset))
                using (CryptoStream cryptoStream = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read))
                using (MemoryStream output = new MemoryStream())
                {
                    cryptoStream.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        private static byte[] HashPassphrase(string passphrase)
        {
            if (string.IsNullOrEmpty(passphrase))
            {
                throw new ArgumentException("Encryption passphrase is required.", "passphrase");
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(passphrase));
            }
        }

        private static byte[] NormalizeKey(byte[] key)
        {
            if (key.Length == 16 || key.Length == 24 || key.Length == 32)
            {
                byte[] copy = new byte[key.Length];
                Buffer.BlockCopy(key, 0, copy, 0, key.Length);
                return copy;
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(key);
            }
        }
    }
}
