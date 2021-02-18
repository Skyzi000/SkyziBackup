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
    [KnownType(typeof(Dictionary<string, BackedUpFileData>))][KnownType(typeof(BackupSettings))]
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
        public Dictionary<string, BackedUpFileData> fileDataDict;

        /// <summary>
        /// 失敗したファイルのリスト
        /// </summary>
        [DataMember]
        public List<string> failedList;

        /// <summary>
        /// 無視したファイルのリスト
        /// </summary>
        [DataMember]
        public List<string> ignoreList;

        /// <summary>
        /// 削除したファイルのリスト(今回もしくは前回削除したもののみ)
        /// </summary>
        [DataMember]
        public List<string> deletedList = null;

        /// <summary>
        /// バックアップ設定(nullの場合はグローバル設定が適用される)
        /// </summary>
        [DataMember]
        public BackupSettings localSettings = null;

        public string SaveFileName => destBaseDirPath;

        public BackupDatabase(string originBaseDirPath, string destBaseDirPath)
        {
            this.originBaseDirPath = originBaseDirPath;
            this.destBaseDirPath = destBaseDirPath;
            fileDataDict = new Dictionary<string, BackedUpFileData>();
            failedList = new List<string>();
            ignoreList = new List<string>();
        }
    }

    /// <summary>
    /// バックアップ済みファイルの詳細データ保管用クラス
    /// </summary>
    public class BackedUpFileData
    {
        public string destFilePath;
        public FileAttributes? fileAttributes = null;
        public DateTime? lastWriteTime = null;
        public ulong? originSize = null;
        public string sha1 = null;
    }

    public class DataContractWriter
    {
        private static XmlWriterSettings XmlSettings { get; } = new XmlWriterSettings() { Encoding = new System.Text.UTF8Encoding(false), Indent = true };
        public static string GetPath(object obj)
        {
            switch (obj)
            {
                case IDataContractSerializable data:
                    return GetPath<IDataContractSerializable>(data.SaveFileName);
                default:
                    throw new ArgumentException($"'{obj.GetType()}'型に対応するパス設定はありません。");
            }
        }
        public static string GetPath<T>(string fileName) where T : IDataContractSerializable
        {
            return Path.Combine(Properties.Settings.Default.AppDataPath, GetDirectory<T>(), $"{fileName}.xml");
        }
        private static string GetDirectory<T>() where T : IDataContractSerializable
        {
            if (typeof(T) == typeof(BackupDatabase)) return "database";
            else return "etc";
        }
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