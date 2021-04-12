using System;
using System.Collections.Generic;

namespace SkyziBackup
{
    public class BackupResults
    {
        /// <summary>
        /// 完了したかどうか。リトライ中は false。
        /// </summary>
        public bool IsFinished { get => _isFinished; set { _isFinished = value; if (_isFinished) OnFinished(EventArgs.Empty); } }

        private bool _isFinished;

        public event EventHandler Finished;

        protected virtual void OnFinished(EventArgs e) => Finished?.Invoke(this, e);

        /// <summary>
        /// 成功したかどうか。バックアップ進行中や一つでも失敗したファイルがある場合は false。
        /// </summary>
        public bool isSuccess;

        /// <summary>
        /// 全体的なメッセージ。
        /// </summary>
        public string Message { get => _message; set { _message = value; OnMessageChanged(EventArgs.Empty); } }

        private string _message;

        public event EventHandler MessageChanged;

        protected virtual void OnMessageChanged(EventArgs e) => MessageChanged?.Invoke(this, e);

        // ※以下のHashSetに記録されるのはバックアップ元のパスのみ。
        /// <summary>
        /// 今回バックアップ/リストアされたファイルのパス。
        /// </summary>
        public HashSet<string> successfulFiles = new HashSet<string>();

        /// <summary>
        /// 今回バックアップ/リストアされたディレクトリのパス。
        /// </summary>
        public HashSet<string> successfulDirectories = new HashSet<string>();

        /// <summary>
        /// バックアップ対象だが前回のバックアップからの変更が確認されず、スキップされたファイルのパス。
        /// </summary>
        public HashSet<string> unchangedFiles = null;

        /// <summary>
        /// 除外パターンによって除外されたファイルのパス。
        /// </summary>
        public HashSet<string> ignoredFiles = null;

        /// <summary>
        /// 除外パターンによって除外されたディレクトリのパス。
        /// </summary>
        public HashSet<string> ignoredDirectories = null;

        /// <summary>
        /// バックアップ対象だが失敗したファイルのパス。
        /// </summary>
        public HashSet<string> failedFiles = new HashSet<string>();

        /// <summary>
        /// バックアップ対象だが失敗したディレクトリのパス。
        /// </summary>
        public HashSet<string> failedDirectories = new HashSet<string>();

        /// <summary>
        /// 削除したファイルのパス。
        /// </summary>
        public HashSet<string> deletedFiles = null;

        /// <summary>
        /// 削除したディレクトリのパス。
        /// </summary>
        public HashSet<string> deletedDirectories = null;

        public BackupResults(bool isSuccess, bool isFinished = false, string message = "")
        {
            this.isSuccess = isSuccess;
            this.IsFinished = isFinished;
            this.Message = message;
        }
    }
}