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
        /// バックアップ済みディレクトリの辞書
        /// </summary>
        [DataMember]
        public Dictionary<string, BackedUpDirectoryData> backedUpDirectoriesDict;

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
        /// ファイル名は(originBaseDirPath + destBaseDirPath)のSHA1
        /// </summary>
        public string SaveFileName => DataContractWriter.GetDatabaseFileName(originBaseDirPath, destBaseDirPath);

        public BackupDatabase(string originBaseDirPath, string destBaseDirPath)
        {
            this.originBaseDirPath = originBaseDirPath;
            this.destBaseDirPath = destBaseDirPath;
            backedUpFilesDict = new Dictionary<string, BackedUpFileData>();
            backedUpDirectoriesDict = new Dictionary<string, BackedUpDirectoryData>();
            failedFiles = new HashSet<string>();
            ignoreFiles = new HashSet<string>();
            deletedFiles = null;
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
        public DateTime? creationTime = null;
        [DataMember]
        public DateTime? lastWriteTime = null;
        [DataMember]
        public long originSize = DefaultSize;
        [DataMember]
        public FileAttributes? fileAttributes = null;
        [DataMember]
        public string sha1 = null;
        public const long DefaultSize = -1;
        public BackedUpFileData(DateTime? creationTime = null, DateTime? lastWriteTime = null, long originSize = DefaultSize, FileAttributes? fileAttributes = null, string sha1 = null)
        {
            this.creationTime = creationTime;
            this.lastWriteTime = lastWriteTime;
            this.originSize = originSize;
            this.fileAttributes = fileAttributes;
            this.sha1 = sha1;
        }
    }
    /// <summary>
    /// バックアップ済みディレクトリの詳細データ保管用クラス
    /// </summary>
    [DataContract]
    [KnownType(typeof(FileAttributes?))]
    [KnownType(typeof(DateTime?))]
    public class BackedUpDirectoryData
    {
        [DataMember]
        public DateTime? creationTime = null;
        [DataMember]
        public DateTime? lastWriteTime = null;
        [DataMember]
        public FileAttributes? fileAttributes = null;

        public BackedUpDirectoryData(DateTime? creationTime = null, DateTime? lastWriteTime = null, FileAttributes? fileAttributes = null)
        {
            this.creationTime = creationTime;
            this.lastWriteTime = lastWriteTime;
            this.fileAttributes = fileAttributes;
        }
    }

    public class DataContractWriter
    {
        public static readonly string ParentDirectoryName = "Data";
        public static readonly string DatabaseFileName = "Database.xml";

        private static XmlWriterSettings XmlSettings { get; } = new XmlWriterSettings() { Encoding = new System.Text.UTF8Encoding(false), Indent = true };
        public static string GetPath(object obj)
        {
            switch (obj)
            {
                case IDataContractSerializable data:
                    return GetPath(data.SaveFileName);
                default:
                    throw new ArgumentException($"'{obj.GetType()}'型に対応するパス設定はありません。");
            }
        }
        public static string GetPath(string fileName) => Path.Combine(Properties.Settings.Default.AppDataPath, fileName);
        public static string GetDatabaseDirectoryName(string originBaseDirPath, string destBaseDirPath) => BackupDirectory.ComputeStringSHA1(originBaseDirPath + destBaseDirPath);
        public static string GetDatabaseFileName(string originBaseDirPath, string destBaseDirPath) => Path.Combine(ParentDirectoryName, GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), DatabaseFileName);
        public static string GetDatabasePath(string originBaseDirPath, string destBaseDirPath) => GetPath(GetDatabaseFileName(originBaseDirPath, destBaseDirPath));
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
            using XmlReader xmlReader = XmlReader.Create(GetPath(fileName));
            return (T)serializer.ReadObject(xmlReader);
        }
        public static void Delete(object obj)
        {
            File.Delete(GetPath(obj));
        }
        public static void Delete<T>(string fileName) where T : IDataContractSerializable
        {
            File.Delete(GetPath(fileName));
        }
        public static void DeleteDatabase(string originBaseDirPath, string destBaseDirPath) => Delete<BackupDatabase>(GetDatabaseFileName(originBaseDirPath, destBaseDirPath));
    }
}