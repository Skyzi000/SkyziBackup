namespace SkyziBackup.Data
{
    /// <summary>
    /// 削除・上書き時の動作
    /// </summary>
    public enum VersioningMethod
    {
        /// <summary>
        /// 完全消去する(バージョン管理を行わない)
        /// </summary>
        PermanentDeletion = 0,

        /// <summary>
        /// ゴミ箱に送る(ゴミ箱が利用できない時は完全消去する)
        /// </summary>
        RecycleBin = 1,

        /// <summary>
        /// 指定されたディレクトリにそのまま移動し、既存のファイルを置き換える
        /// </summary>
        Replace = 2,

        /// <summary>
        /// 新規作成されたタイムスタンプ付きのディレクトリ以下に移動する
        /// <code>\YYYY-MM-DD_hhmmss\Directory\hoge.txt</code>
        /// </summary>
        DirectoryTimeStamp = 3,

        /// <summary>
        /// タイムスタンプを追加したファイル名で、指定されたディレクトリに移動する
        /// <code>\Directory\File.txt_YYYY-MM-DD_hhmmss.txt</code>
        /// </summary>
        FileTimeStamp = 4,
    }
}
