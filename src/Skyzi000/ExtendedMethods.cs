using Skyzi000.Cryptography;
using System.IO;
using System.IO.Compression;
namespace Skyzi000
{
    public static class ExtendedMethods
    {
        /// <summary>
        /// <see cref="Stream"/>を圧縮/展開しながらコピーします。
        /// </summary>
        public static void CopyWithCompressionTo(this Stream stream, Stream destination, CompressionLevel compressionLevel, CompressionMode compressionMode, CompressAlgorithm compressAlgorithm = CompressAlgorithm.Deflate)
        {
            if (compressionLevel == CompressionLevel.NoCompression)
            {
                stream.CopyTo(destination);
            }
            else if (compressionMode == CompressionMode.Decompress)
            {
                switch (compressAlgorithm)
                {
                    case CompressAlgorithm.Deflate:
                        {
                            using var compressionStream = new DeflateStream(stream, CompressionMode.Decompress);
                            compressionStream.CopyTo(destination);
                            break;
                        }

                    case CompressAlgorithm.GZip:
                        {
                            using var compressionStream = new GZipStream(stream, CompressionMode.Decompress);
                            compressionStream.CopyTo(destination);
                            break;
                        }
                }
            }
            else
            {
                switch (compressAlgorithm)
                {
                    case CompressAlgorithm.Deflate:
                        {
                            using var compressionStream = new DeflateStream(destination, compressionLevel);
                            stream.CopyTo(compressionStream);
                            break;
                        }

                    case CompressAlgorithm.GZip:
                        {
                            using var compressionStream = new GZipStream(destination, compressionLevel);
                            stream.CopyTo(compressionStream);
                            break;
                        }
                }
            }
        }
    }
}