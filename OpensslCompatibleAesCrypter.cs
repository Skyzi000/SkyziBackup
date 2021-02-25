/*
 * OpensslCompatibleAesCrypter.cs
 * 
 * Created by Skyzi000 on 2020/11/21.
 * 
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Skyzi000.Cryptography
{
    public enum CustomCipherMode
    {
        CBC = 1,
        ECB = 2,
        OFB = 3,
        CFB = 4,
        CTS = 5,
        CTR = 6,
    }
    public enum CompressAlgorithm
    {
        Deflate,
        GZip,
    }
    public class OpensslCompatibleAesCrypter
    {
        public HashAlgorithmName HashAlgorithm { get; set; } = HashAlgorithmName.SHA256;
        public CustomCipherMode Mode { get; set; }
        public CompressionLevel CompressionLevel { get; set; }
        public CompressAlgorithm CompressAlgorithm { get; set; }
        public int IterationCount { get; set; }
        public byte[] Salt { get; private set; }
        public byte[] Iv { get; private set; }
        public Exception Error { get; private set; }

        public readonly int keySize;
        public readonly string prefix = "Salted__";
        private readonly int blockSize;
        private byte[] password;
        private byte[] key;
        private readonly SymmetricAlgorithm aes;

        public OpensslCompatibleAesCrypter(string password,
                                           int keySize = 256,
                                           int iterationCount = 10000,
                                           CustomCipherMode cipherMode = CustomCipherMode.CBC,
                                           CompressionLevel compressionLevel = CompressionLevel.NoCompression,
                                           CompressAlgorithm compressAlgorithm = CompressAlgorithm.Deflate)
        {
            this.password = Encoding.UTF8.GetBytes(password);
            this.keySize = (keySize == 128 || keySize == 192) ? keySize : 256;
            this.IterationCount = iterationCount;
            this.Mode = cipherMode;
            CipherMode cm = (Mode == CustomCipherMode.CTR) ? CipherMode.ECB : (CipherMode)cipherMode;
            PaddingMode pm = (Mode == CustomCipherMode.CTR) ? PaddingMode.None : PaddingMode.PKCS7;
            this.aes = new AesCng { Mode = cm, Padding = pm };
            this.blockSize = aes.BlockSize / 8;
            this.CompressionLevel = compressionLevel;
            this.CompressAlgorithm = compressAlgorithm;
        }

        public bool EncryptFile(string inputPath, string outputPath)
        {
            using FileStream infs = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
            using FileStream outfs = new FileStream(outputPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            return EncryptStream(infs, outfs);
        }

        public bool DecryptFile(string inputPath, string outputPath)
        {
            using FileStream infs = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
            using FileStream outfs = new FileStream(outputPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            return DecryptStream(infs, outfs);
        }

        public bool EncryptStream(Stream input, Stream output)
        {
            bool result = true;
            try
            {
                if (input is null)
                {
                    throw new ArgumentNullException(nameof(input));
                }

                if (output is null)
                {
                    throw new ArgumentNullException(nameof(output));
                }
                Salt = GenerateSalt(8);
                //if (!GetOpenSSLKey(password, salt)) return false;
                SetKeyAndIV(GenerateKIV(Salt, password, HashAlgorithm, 10000, keySize / 8 + blockSize));
                output.Write(Encoding.UTF8.GetBytes(prefix));
                output.Write(Salt);
                if (Mode == CustomCipherMode.CTR)
                    AesCtrTransform(Iv, input, output);
                else
                {
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
            }
            catch (Exception e)
            {
                result = false;
                Error = e;
            }
            return result;
        }

        public bool DecryptStream(Stream input, Stream output)
        {
            bool result = true;
            try
            {
                if (input is null)
                {
                    throw new ArgumentNullException(nameof(input));
                }

                if (output is null)
                {
                    throw new ArgumentNullException(nameof(output));
                }
                if (!ExtractAndSetSalt(input)) return false;
                //if (!GetOpenSSLKey(password, salt)) return false;
                SetKeyAndIV(GenerateKIV(Salt, password, HashAlgorithmName.SHA256, 10000, keySize / 8 + blockSize));
                if (Mode == CustomCipherMode.CTR)
                    AesCtrTransform(Iv, input, output);
                else
                {
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
            }
            catch (Exception e)
            {
                result = false;
                Error = e;
            }
            return result;
        }

        private bool ExtractAndSetSalt(Stream encrypted)
        {
            byte[] pre = new byte[8];
            encrypted.Read(pre, 0, 8);
            if (Encoding.ASCII.GetString(pre) == prefix)
            {
                Salt = new byte[8];
                if (encrypted.Read(Salt, 0, 8) == 8)
                    return true;
            }
            return false;
        }
        private void SetKeyAndIV(byte[] kiv)
        {
            key = new byte[keySize / 8];
            Iv = new byte[blockSize];
            Array.Copy(kiv, 0, key, 0, key.Length);
            Array.Copy(kiv, key.Length, Iv, 0, blockSize);
        }

        // https://qiita.com/c-yan/items/1e8f66f6b1019aad56bd
        private byte[] GenerateSalt(int size)
        {
            var result = new byte[size];
            using (var csp = new RNGCryptoServiceProvider())
            {
                csp.GetBytes(result);
                return result;
            }
        }
        private byte[] GenerateKIV(byte[] salt, byte[] password, HashAlgorithmName hashAlgorithm, int iterationCount, int size)
        {
            return new Rfc2898DeriveBytes(password, salt, iterationCount, hashAlgorithm)
                .GetBytes(size);
        }

        // 1バイトずつ処理するせいか、CBCモードなどと比べて非常に遅い
        // https://stackoverflow.com/questions/6374437/can-i-use-aes-in-ctr-mode-in-net
        private void AesCtrTransform(
        byte[] salt, Stream inputStream, Stream outputStream)
        {
            if (salt.Length != blockSize)
            {
                throw new ArgumentException(
                    string.Format(
                        "Salt size must be same as block size (actual: {0}, expected: {1})",
                        salt.Length, blockSize));
            }

            byte[] counter = (byte[])salt.Clone();

            Queue<byte> xorMask = new Queue<byte>();

            var zeroIv = new byte[blockSize];
            ICryptoTransform counterEncryptor = aes.CreateEncryptor(key, zeroIv);

            int b;
            while ((b = inputStream.ReadByte()) != -1)
            {
                if (xorMask.Count == 0)
                {
                    var counterModeBlock = new byte[blockSize];

                    counterEncryptor.TransformBlock(
                        counter, 0, counter.Length, counterModeBlock, 0);

                    for (var i2 = counter.Length - 1; i2 >= 0; i2--)
                    {
                        if (++counter[i2] != 0)
                        {
                            break;
                        }
                    }

                    foreach (var b2 in counterModeBlock)
                    {
                        xorMask.Enqueue(b2);
                    }
                }

                var mask = xorMask.Dequeue();
                outputStream.WriteByte((byte)(((byte)b) ^ mask));
            }
        }

        // 恐らく古いバージョンのOpenSSLで使われていたMD5を利用する方法
        // https://qiita.com/h-ymmr/items/eb21378f5132400b3e80
        private bool GetOpenSSLKey(byte[] baKey, byte[] baSalt)
        {
            bool blnIsOk = true;
            MD5 objMd5 = MD5.Create();
            byte[] baHash1 = new byte[16];
            byte[] baHash2 = new byte[16];
            byte[] baPreKey = new byte[baKey.Length + baSalt.Length];
            byte[] baPreIV = new byte[16 + baPreKey.Length];
            byte[] baPreHash2 = new byte[16 + baPreKey.Length];
            // 128
            //   Key   = MD5(暗号キー + SALT)
            //   IV    = MD5(Key + 暗号キー + SALT)
            // 192,256
            //   Hash0 = ''
            //   Hash1 = MD5(Hash0 + 暗号キー + SALT)
            //   Hash2 = MD5(Hash1 + 暗号キー + SALT)
            //   Hash3 = MD5(Hash2 + 暗号キー + SALT)
            //   Key   = Hash1 + Hash2
            //   IV    = Hash3
            try
            {
                Buffer.BlockCopy(baKey, 0, baPreKey, 0, baKey.Length);
                Buffer.BlockCopy(baSalt, 0, baPreKey, baKey.Length, baSalt.Length);
                if (128 == keySize)
                {
                    key = objMd5.ComputeHash(baPreKey);
                }
                else
                {
                    baHash1 = objMd5.ComputeHash(baPreKey);
                    Buffer.BlockCopy(baHash1, 0, baPreHash2, 0, baHash1.Length);
                    Buffer.BlockCopy(baPreKey, 0, baPreHash2, baHash1.Length, baPreKey.Length);
                    baHash2 = objMd5.ComputeHash(baPreHash2);
                    key = new byte[32];
                    Buffer.BlockCopy(baHash1, 0, key, 0, baHash1.Length);
                    Buffer.BlockCopy(baHash2, 0, key, baHash1.Length, baHash2.Length);
                }
                if (128 == keySize)
                {
                    Buffer.BlockCopy(key, 0, baPreIV, 0, key.Length);
                    Buffer.BlockCopy(baPreKey, 0, baPreIV, key.Length, baPreKey.Length);
                }
                else
                {
                    Buffer.BlockCopy(baHash2, 0, baPreIV, 0, baHash2.Length);
                    Buffer.BlockCopy(baPreKey, 0, baPreIV, baHash2.Length, baPreKey.Length);
                }
                Iv = objMd5.ComputeHash(baPreIV);
            }
            catch (Exception e)
            {
                blnIsOk = false;
                Error = e;
            }
            finally
            {
                objMd5.Clear();
            }
            return blnIsOk;
        }
    }
}
