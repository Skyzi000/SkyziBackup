using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace SkyziBackup
{
    public interface IDataContractSerializable
    {
        public string SaveFileName { get; }
    }
    [DataContract]
    [KnownType(typeof(BackedUpFileData))]
    [KnownType(typeof(BackupSettings))]
    public class BackupDatabase : IDataContractSerializable
    {
        [DataMember]
        public string originBaseDirPath;
        [DataMember]
        public string destBaseDirPath;

        /// <summary>
        /// originFilePathをキーとするバックアップ済みファイルの辞書
        /// </summary>
        [DataMember]
        public Dictionary<string, BackedUpFileData> backedUpFilesDict;

        /// <summary>
        /// バックアップ済みフォルダのリスト
        /// </summary>
        [DataMember]
        public HashSet<string> backedUpDirectories;

        /// <summary>
        /// 失敗したファイルのリスト
        /// </summary>
        [DataMember]
        public HashSet<string> failedFiles;

        /// <summary>
        /// 無視したファイルのリスト
        /// </summary>
        [DataMember]
        public HashSet<string> ignoreFiles;

        /// <summary>
        /// 削除したファイルのリスト(今回もしくは前回削除したもののみ)
        /// </summary>
        [DataMember]
        public HashSet<string> deletedFiles = null;

        /// <summary>
        /// バックアップ設定(nullの場合はグローバル設定が適用される)
        /// </summary>
        [DataMember]
        public BackupSettings localSettings = null;

        /// <summary>
        /// ファイル名は(originBaseDirPath + destBaseDirPath)のSHA1
        /// </summary>
        public string SaveFileName => DirectoryBackup.ComputeStringSHA1(originBaseDirPath + destBaseDirPath);

        public BackupDatabase(string originBaseDirPath, string destBaseDirPath)
        {
            this.originBaseDirPath = originBaseDirPath;
            this.destBaseDirPath = destBaseDirPath;
            backedUpFilesDict = new Dictionary<string, BackedUpFileData>();
            backedUpDirectories = new HashSet<string>();
            failedFiles = new HashSet<string>();
            ignoreFiles = new HashSet<string>();
            deletedFiles = null;
            localSettings = null;
        }
    }

    /// <summary>
    /// バックアップ済みファイルの詳細データ保管用クラス
    /// </summary>
    [DataContract]
    [KnownType(typeof(FileAttributes?))]
    [KnownType(typeof(DateTime?))]
    public class BackedUpFileData
    {
        [DataMember]
        public string destFilePath;
        [DataMember]
        public DateTime? lastWriteTime = null;
        [DataMember]
        public long originSize = DefaultSize;
        [DataMember]
        public FileAttributes? fileAttributes = null;
        [DataMember]
        public string sha1 = null;
        public const long DefaultSize = -1;
        public BackedUpFileData(string destFilePath, DateTime? lastWriteTime = null, long originSize = DefaultSize, FileAttributes? fileAttributes = null, string sha1 = null)
        {
            this.destFilePath = destFilePath;
            this.lastWriteTime = lastWriteTime;
            this.originSize = originSize;
            this.fileAttributes = fileAttributes;
            this.sha1 = sha1;
        }
    }

    public class DataContractWriter
    {
        private static XmlWriterSettings XmlSettings { get; } = new XmlWriterSettings() { Encoding = new System.Text.UTF8Encoding(false), Indent = true };
        public static string GetPath(object obj)
        {
            switch (obj)
            {
                case BackupDatabase database:
                    return GetPath<BackupDatabase>(database.SaveFileName);
                case IDataContractSerializable data:
                    return GetPath<IDataContractSerializable>(data.SaveFileName);
                default:
                    throw new ArgumentException($"'{obj.GetType()}'型に対応するパス設定はありません。");
            }
        }
        public static string GetPath<T>(string fileName) where T : IDataContractSerializable
        {
            string directory = (typeof(T) == typeof(BackupDatabase))    ? "database"
                                                                        : "etc";
            return Path.Combine(Properties.Settings.Default.AppDataPath, directory, $"{fileName}.xml");
        }

        // TODO: エラー処理を追加する
        public static void Write<T>(T obj) where T : IDataContractSerializable
        {
            DataContractSerializer serializer = new DataContractSerializer(typeof(T));
            string filePath = GetPath(obj);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            using XmlWriter xmlWriter = XmlWriter.Create(filePath, XmlSettings);
            serializer.WriteObject(xmlWriter, obj);
        }
        public static T Read<T>(string fileName) where T : IDataContractSerializable
        {
            DataContractSerializer serializer = new DataContractSerializer(typeof(T));
            using XmlReader xmlReader = XmlReader.Create(GetPath<T>(fileName));
            return (T)serializer.ReadObject(xmlReader);
        }
    }
}