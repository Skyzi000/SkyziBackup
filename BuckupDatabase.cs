using System;
using System.Collections.Generic;

namespace SkyziBackup
{
    public class BackupDatabase
    {
        public string originBaseDirPath;
        public string destBaseDirPath;

        /// <summary>
        /// originFilePathをキーとするバックアップ済みファイルの辞書
        /// </summary>
        public Dictionary<string, BackedUpFileData> fileDataDict;

        /// <summary>
        /// 失敗したファイルのリスト
        /// </summary>
        public List<string> failedList;

        /// <summary>
        /// 無視したファイルのリスト
        /// </summary>
        public List<string> ignoreList;

        /// <summary>
        /// 削除したファイルのリスト(今回もしくは前回削除したもののみ)
        /// </summary>
        public List<string> deletedList = null;

        //TODO: destBaseDirPathの名前でProperties.Settings.Default.AppDataPathにデータベースファイルを置く。
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
}