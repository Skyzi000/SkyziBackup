using System;
using System.Collections.Generic;

namespace SkyziBackup
{
    public class BackupResults
    {
        /// <summary>
        /// 完了したかどうか。リトライ中はfalse。
        /// </summary>
        public bool IsFinished { get => _isFinished; set { _isFinished = value; if (_isFinished) OnFinished(EventArgs.Empty); } }

        private bool _isFinished;

        public event EventHandler Finished;

        protected virtual void OnFinished(EventArgs e) => Finished?.Invoke(this, e);

        /// <summary>
        /// 成功したかどうか。バックアップ進行中や一つでも失敗したファイルがある場合はfalse。
        /// </summary>
        public bool isSuccess;

        /// <summary>
        /// 全体的なメッセージ。
        /// </summary>
        public string Message { get => _message; set { _message = value; OnMessageChanged(EventArgs.Empty); } }

        private string _message;

        public event EventHandler MessageChanged;

        protected virtual void OnMessageChanged(EventArgs e) => MessageChanged?.Invoke(this, e);

        /// <summary>
        /// 新しくバックアップされたファイルのパス。(データベース利用時は常にnull)
        /// </summary>
        public HashSet<string> backedUpFiles = null;

        /// <summary>
        /// バックアップ対象だが失敗したファイルのパス。
        /// </summary>
        public HashSet<string> failedFiles;

        public BackupResults(bool isSuccess, bool isFinished = false, string message = "")
        {
            this.isSuccess = isSuccess;
            this.IsFinished = isFinished;
            this.Message = message;
            failedFiles = new HashSet<string>();
        }
    }
}