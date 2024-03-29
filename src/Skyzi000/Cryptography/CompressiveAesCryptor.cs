﻿using System;
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

    public class CompressiveAesCryptor : IDisposable
    {
        public HashAlgorithmName HashAlgorithm { get; set; } = HashAlgorithmName.SHA256;
        public CipherMode Mode { get; set; }
        public CompressionLevel CompressionLevel { get; set; }
        public CompressAlgorithm CompressAlgorithm { get; set; }
        public int IterationCount { get; set; }
        public byte[]? Salt { get; private set; }
        public byte[]? Iv { get; private set; }
        public string Prefix = "Salted__";
        public readonly int KeySize;

        private readonly int _blockSize;
        private readonly byte[] _password;
        private byte[]? _key;
        private readonly SymmetricAlgorithm _aes;

        public CompressiveAesCryptor(string password,
            int keySize = 256,
            int iterationCount = 10000,
            CipherMode cipherMode = CipherMode.CBC,
            CompressionLevel compressionLevel = CompressionLevel.NoCompression,
            CompressAlgorithm compressAlgorithm = CompressAlgorithm.Deflate)
        {
            _password = Encoding.UTF8.GetBytes(password);
            KeySize = keySize is 128 or 192 ? keySize : 256;
            IterationCount = iterationCount;
            Mode = cipherMode;
            _aes = new AesCng { Mode = Mode, Padding = PaddingMode.PKCS7 };
            _blockSize = _aes.BlockSize / 8;
            CompressionLevel = compressionLevel;
            CompressAlgorithm = compressAlgorithm;
        }

        public void EncryptFile(string inputPath, string outputPath)
        {
            using var infs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var outfs = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite);
            EncryptStream(infs, outfs);
        }

        public void DecryptFile(string inputPath, string outputPath)
        {
            using var infs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var outfs = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite);
            DecryptStream(infs, outfs);
        }

        public void EncryptStream(Stream input, Stream output)
        {
            Salt = GenerateSalt(8);
            (_key, Iv) = GetKeyAndIV(GenerateKIV(Salt, _password, HashAlgorithm, 10000, KeySize / 8 + _blockSize));
            output.Write(Encoding.UTF8.GetBytes(Prefix));
            output.Write(Salt);
            ICryptoTransform cryptoTransform = _aes.CreateEncryptor(_key, Iv);
            using var cryptoStream = new CryptoStream(output, cryptoTransform, CryptoStreamMode.Write);
            if (CompressionLevel != CompressionLevel.NoCompression)
            {
                switch (CompressAlgorithm)
                {
                    case CompressAlgorithm.Deflate:
                    {
                        using var deflateStream = new DeflateStream(cryptoStream, CompressionLevel);
                        input.CopyTo(deflateStream);
                        break;
                    }
                    case CompressAlgorithm.GZip:
                    {
                        using var deflateStream = new GZipStream(cryptoStream, CompressionLevel);
                        input.CopyTo(deflateStream);
                        break;
                    }
                }
            }
            else
                input.CopyTo(cryptoStream);
        }

        public void DecryptStream(Stream input, Stream output)
        {
            if (!TryExtractSalt(input, out var salt))
                throw new ArgumentException("Cannot read salt from input.");
            Salt = salt;
            (_key, Iv) = GetKeyAndIV(GenerateKIV(Salt, _password, HashAlgorithmName.SHA256, 10000, KeySize / 8 + _blockSize));
            ICryptoTransform cryptoTransform = _aes.CreateDecryptor(_key, Iv);
            if (CompressionLevel != CompressionLevel.NoCompression)
            {
                switch (CompressAlgorithm)
                {
                    case CompressAlgorithm.Deflate:
                    {
                        using var cryptoStream = new CryptoStream(input, cryptoTransform, CryptoStreamMode.Read);
                        using var deflateStream = new DeflateStream(cryptoStream, CompressionMode.Decompress);
                        deflateStream.CopyTo(output);
                        break;
                    }
                    case CompressAlgorithm.GZip:
                    {
                        using var cryptoStream = new CryptoStream(input, cryptoTransform, CryptoStreamMode.Read);
                        using var deflateStream = new GZipStream(cryptoStream, CompressionMode.Decompress);
                        deflateStream.CopyTo(output);
                        break;
                    }
                }
            }
            else
            {
                using var cryptoStream = new CryptoStream(output, cryptoTransform, CryptoStreamMode.Write);
                input.CopyTo(cryptoStream);
            }
        }

        private bool TryExtractSalt(Stream encrypted, [NotNullWhen(true)] out byte[]? salt)
        {
            var pre = new byte[8];
            encrypted.Read(pre, 0, 8);
            if (Encoding.ASCII.GetString(pre) == Prefix)
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
            var k = new byte[KeySize / 8];
            var i = new byte[_blockSize];
            Array.Copy(kiv, 0, k, 0, k.Length);
            Array.Copy(kiv, k.Length, i, 0, _blockSize);
            return (k, i);
        }

        // 以下 GenerateSalt(int saltLength) と GenerateKIV(byte[] salt, byte[] password, HashAlgorithmName hashAlgorithm, int iterationCount, int size) は
        // c-yan さんの「OpenSSL で暗号化したデータを C# で復号する - Qiita」より一部を使わせていただきました。ありがとうございます。
        // https://qiita.com/c-yan/items/1e8f66f6b1019aad56bd
        // Qiita利用規約(2021年4月23日閲覧)：https://qiita.com/terms
        private byte[] GenerateSalt(int saltLength)
        {
            var result = new byte[saltLength];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(result);
            return result;
        }

        private byte[] GenerateKIV(byte[] salt, byte[] password, HashAlgorithmName hashAlgorithm, int iterationCount, int size) =>
            new Rfc2898DeriveBytes(password, salt, iterationCount, hashAlgorithm).GetBytes(size);

        public void Dispose() => ((IDisposable) _aes).Dispose();
    }
}
