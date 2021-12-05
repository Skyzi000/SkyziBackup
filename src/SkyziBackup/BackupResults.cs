﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace SkyziBackup
{
    public class BackupResults : SaveableData
    {
        [JsonPropertyName("ob")]
        public string? OriginBaseDirPath { get; set; }
        [JsonPropertyName("db")]
        public string? DestBaseDirPath { get; set; }

        /// <summary>
        /// 完了したかどうか。リトライ中は false。
        /// </summary>
        public bool IsFinished { get => _isFinished; set { _isFinished = value; if (_isFinished) OnFinished(EventArgs.Empty); } }

        private bool _isFinished;

        public event EventHandler? Finished;

        protected virtual void OnFinished(EventArgs e) => Finished?.Invoke(this, e);

        /// <summary>
        /// 成功したかどうか。バックアップ進行中や一つでも失敗したファイルがある場合は false。
        /// </summary>
        public bool isSuccess { get; set; } = false;

        /// <summary>
        /// メッセージ
        /// </summary>
        public string Message { get => _message; set { _message = value; OnMessageChanged(EventArgs.Empty); } }

        private string _message = string.Empty;

        public event EventHandler? MessageChanged;

        protected virtual void OnMessageChanged(EventArgs e) => MessageChanged?.Invoke(this, e);

        // ※以下のHashSetに記録されるのはバックアップ元のパスのみ。
        /// <summary>
        /// 今回バックアップ/リストアされたファイルのパス。
        /// </summary>
        public HashSet<string> successfulFiles { get; set; } = new HashSet<string>();

        /// <summary>
        /// 今回バックアップ/リストアされたディレクトリのパス。
        /// </summary>
        public HashSet<string> successfulDirectories { get; set; } = new HashSet<string>();

        /// <summary>
        /// バックアップ対象だが前回のバックアップからの変更が確認されず、スキップされたファイルのパス。
        /// </summary>
        public HashSet<string>? unchangedFiles { get; set; } = null;

        /// <summary>
        /// バックアップ対象だが失敗したファイルのパス。
        /// </summary>
        public HashSet<string> failedFiles { get; set; } = new HashSet<string>();

        /// <summary>
        /// バックアップ対象だが失敗したディレクトリのパス。
        /// </summary>
        public HashSet<string> failedDirectories { get; set; } = new HashSet<string>();

        /// <summary>
        /// 削除したファイルのパス。
        /// </summary>
        public HashSet<string>? deletedFiles { get; set; } = null;

        /// <summary>
        /// 削除したディレクトリのパス。
        /// </summary>
        public HashSet<string>? deletedDirectories { get; set; } = null;

        [JsonIgnore]
        public override string? SaveFileName => OriginBaseDirPath is null || DestBaseDirPath is null ? null : GetFileName(OriginBaseDirPath, DestBaseDirPath);



        public static readonly string FileName = nameof(BackupResults) + DataFileWriter.DefaultExtension;

        public BackupResults() { }

        public BackupResults(bool isSuccess, bool isFinished = false, string message = "")
        {
            this.isSuccess = isSuccess;
            this.IsFinished = isFinished;
            this.Message = message;
        }

        public BackupResults(string originBaseDirPath, string destBaseDirPath)
        {
            OriginBaseDirPath = originBaseDirPath;
            DestBaseDirPath = destBaseDirPath;
        }

        private static string GetFileName(string originBaseDirPath, string destBaseDirPath) =>
            Path.Combine(DataFileWriter.GetDatabaseDirectoryName(originBaseDirPath, destBaseDirPath), FileName);
    }
}