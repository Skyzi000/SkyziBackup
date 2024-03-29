﻿using System;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Skyzi000.Data
{
    public static class DataFileWriter
    {
        public const string JsonExtension = ".json";
        public static readonly string TempFileExtension = ".tmp";
        public static readonly string BackupFileExtension = ".bac";
        public static readonly string DefaultExtension = JsonExtension;

        public static readonly string DefaultBaseDirectoryPath =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;

        public static readonly string DefaultParentDirectoryName = "Data";
        public static string BaseDirectoryPath = DefaultBaseDirectoryPath;
        public static string ParentDirectoryName = DefaultParentDirectoryName;

        private static JsonSerializerOptions SerializerOptions { get; } = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
            IgnoreNullValues = true,
        };

        public static string GetPath(SaveableData obj) => GetPath(obj.SaveFileName ?? throw new ArgumentException(nameof(obj.SaveFileName)));
        public static string GetPath(string fileName) => Path.Combine(BaseDirectoryPath, fileName);

        public static async Task WriteAsync(SaveableData obj, string? filePath = null, bool makeBackup = false, CancellationToken cancellationToken = default)
        {
            if (filePath is null && obj.SaveFileName is null)
                throw new ArgumentNullException(nameof(filePath));
            filePath ??= GetPath(obj);
            var tmpPath = filePath + TempFileExtension;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? throw new ArgumentException(nameof(filePath)));
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(fs, obj, obj.GetType(), SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            Replace(tmpPath, filePath, makeBackup);
        }

        public static void Replace(string sourceFileName, string destinationFileName, bool makeBackup = false)
        {
            try
            {
                File.Replace(sourceFileName, destinationFileName, makeBackup ? destinationFileName + BackupFileExtension : null, true);
            }
            catch (Exception)
            {
                if (makeBackup && File.Exists(destinationFileName))
                    File.Move(destinationFileName, destinationFileName + BackupFileExtension, true);
                File.Move(sourceFileName, destinationFileName, true);
            }
        }

        public static void Write(SaveableData obj, string? filePath = null, bool makeBackup = false) => WriteAsync(obj, filePath, makeBackup).Wait();

        public static async Task<T?> ReadAsync<T>(string fileName, CancellationToken cancellationToken = default) where T : SaveableData
        {
            var filePath = GetPath(fileName);
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return await JsonSerializer.DeserializeAsync<T>(fs, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // デシリアライズに失敗した場合、バックアップがあればそっちを読みに行く
                var bacFilePath = filePath + BackupFileExtension;
                if (!File.Exists(bacFilePath))
                    throw;
                using var bfs = new FileStream(bacFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return await JsonSerializer.DeserializeAsync<T>(bfs, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }
        }

        public static T? Read<T>(string fileName) where T : SaveableData => ReadAsync<T>(fileName).Result;
        public static void Delete(SaveableData obj) => File.Delete(GetPath(obj));
        public static void Delete<T>(string fileName) where T : SaveableData => File.Delete(GetPath(fileName));
    }
}
