/*
 * OpensslCompatibleAesCrypter.cs
 * 
 * Created by Skyzi000 on 2020/11/21.
 * 
 */
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Skyzi000.Cryptography
{
    public enum CompressAlgorithm
    {
        Deflate,
        GZip,
    }
    public class OpensslCompatibleAesCryptor : IDisposable
    {
        public HashAlgorithmName HashAlgorithm { get; set; } = HashAlgorithmName.SHA256;
        public CipherMode Mode { get; set; }
        public CompressionLevel CompressionLevel { get; set; }
        public CompressAlgorithm CompressAlgorithm { get; set; }
        public int IterationCount { get; set; }
        public byte[]? Salt { get; private set; }
        public byte[]? Iv { get; private set; }
        public string prefix = "Salted__";

        public readonly int keySize;
        private readonly int blockSize;
        private byte[] password;
        private byte[]? key;
        private readonly SymmetricAlgorithm aes;

        public OpensslCompatibleAesCryptor(string password,
                                           int keySize = 256,
                                           int iterationCount = 10000,
                                           CipherMode cipherMode = CipherMode.CBC,
                                           CompressionLevel compressionLevel = CompressionLevel.NoCompression,
                                           CompressAlgorithm compressAlgorithm = CompressAlgorithm.Deflate)
        {
            this.password = Encoding.UTF8.GetBytes(password);
            this.keySize = (keySize == 128 || keySize == 192) ? keySize : 256;
            this.IterationCount = iterationCount;
            this.Mode = cipherMode;
            this.aes = new AesCng { Mode = this.Mode, Padding = PaddingMode.PKCS7 };
            this.blockSize = aes.BlockSize / 8;
            this.CompressionLevel = compressionLevel;
            this.CompressAlgorithm = compressAlgorithm;
        }

        public void EncryptFile(string inputPath, string outputPath)
        {
            using FileStream infs = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
            using FileStream outfs = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite);
            EncryptStream(infs, outfs);
        }

        public void DecryptFile(string inputPath, string outputPath)
        {
            using FileStream infs = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
            using FileStream outfs = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite);
            DecryptStream(infs, outfs);
        }

        public void EncryptStream(Stream input, Stream output)
        {
            Salt = GenerateSalt(8);
            (key, Iv) = GetKeyAndIV(GenerateKIV(Salt, password, HashAlgorithm, 10000, keySize / 8 + blockSize));
            output.Write(Encoding.UTF8.GetBytes(prefix));
            output.Write(Salt);
            ICryptoTransform cryptoTransform = aes.CreateEncryptor(key, Iv);
            using CryptoStream cryptoStream = new CryptoStream(output, cryptoTransform, CryptoStreamMode.Write);
            if (CompressionLevel != CompressionLevel.NoCompression)
            {
                switch (CompressAlgorithm)
                {
                    case CompressAlgorithm.Deflate:
                        {
                            using DeflateStream deflateStream = new DeflateStream(cryptoStream, CompressionLevel);
                            input.CopyTo(deflateStream);
                            break;
                        }
                    case CompressAlgorithm.GZip:
                        {
                            using GZipStream deflateStream = new GZipStream(cryptoStream, CompressionLevel);
                            input.CopyTo(deflateStream);
                            break;
                        }
                }
            }
            else
            {
                input.CopyTo(cryptoStream);
            }
        }

        public void DecryptStream(Stream input, Stream output)
        {
            if (!TryExtractSalt(input, out var salt))
                throw new ArgumentException("Cannot read salt from input.");
            else
                Salt = salt;
            (key, Iv) = GetKeyAndIV(GenerateKIV(Salt, password, HashAlgorithmName.SHA256, 10000, keySize / 8 + blockSize));
            ICryptoTransform cryptoTransform = aes.CreateDecryptor(key, Iv);
            if (CompressionLevel != CompressionLevel.NoCompression)
            {
                switch (CompressAlgorithm)
                {
                    case CompressAlgorithm.Deflate:
                        {
                            using CryptoStream cryptoStream = new CryptoStream(input, cryptoTransform, CryptoStreamMode.Read);
                            using DeflateStream deflateStream = new DeflateStream(cryptoStream, CompressionMode.Decompress);
                            deflateStream.CopyTo(output);
                            break;
                        }
                    case CompressAlgorithm.GZip:
                        {
                            using CryptoStream cryptoStream = new CryptoStream(input, cryptoTransform, CryptoStreamMode.Read);
                            using GZipStream deflateStream = new GZipStream(cryptoStream, CompressionMode.Decompress);
                            deflateStream.CopyTo(output);
                            break;
                        }
                }
            }
            else
            {
                using CryptoStream cryptoStream = new CryptoStream(output, cryptoTransform, CryptoStreamMode.Write);
                input.CopyTo(cryptoStream);
            }
        }

        private bool TryExtractSalt(Stream encrypted, [NotNullWhen(true)] out byte[]? salt)
        {
            byte[] pre = new byte[8];
            encrypted.Read(pre, 0, 8);
            if (Encoding.ASCII.GetString(pre) == prefix)
            {
                salt = new byte[8];
                if (encrypted.Read(salt, 0, 8) == 8)
                    return true;
            }
            salt = null;
            return false;
        }
        private (byte[] key, byte[] iv) GetKeyAndIV(byte[] kiv)
        {
            byte[] k = new byte[keySize / 8];
            byte[] i = new byte[blockSize];
            Array.Copy(kiv, 0, k, 0, k.Length);
            Array.Copy(kiv, k.Length, i, 0, blockSize);
            return (k, i);
        }

        // 以下 GenerateSalt(int saltLength) と GenerateKIV(byte[] salt, byte[] password, HashAlgorithmName hashAlgorithm, int iterationCount, int size) は
        // c-yan さんの「OpenSSL で暗号化したデータを C# で復号する - Qiita」より一部を使わせていただきました。ありがとうございます。
        // https://qiita.com/c-yan/items/1e8f66f6b1019aad56bd
        // Qiita利用規約(2021年4月23日閲覧)：https://qiita.com/terms
        private byte[] GenerateSalt(int saltLength)
        {
            var result = new byte[saltLength];
            using (var csp = new RNGCryptoServiceProvider())
            {
                csp.GetBytes(result);
                return result;
            }
        }
        private byte[] GenerateKIV(byte[] salt, byte[] password, HashAlgorithmName hashAlgorithm, int iterationCount, int size)
        {
            return new Rfc2898DeriveBytes(password, salt, iterationCount, hashAlgorithm).GetBytes(size);
        }

        public void Dispose()
        {
            ((IDisposable)aes).Dispose();
        }
    }
}
