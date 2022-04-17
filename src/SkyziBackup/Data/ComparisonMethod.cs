using System;

namespace SkyziBackup.Data
{
    /// <summary>
    /// ファイルの変更を検知する方法
    /// </summary>
    [Flags]
    public enum ComparisonMethod
    {
        /// <summary>
        /// 比較しない
        /// </summary>
        /// <remarks>他の値と同時に指定することはできない</remarks>
        NoComparison = 0,

        /// <summary>
        /// Archive属性による比較
        /// </summary>
        /// <remarks>バックアップ時に元ファイルのArchive属性を変更する点に注意</remarks>
        ArchiveAttribute = 1,

        /// <summary>
        /// 更新日時による比較
        /// </summary>
        WriteTime = 1 << 1,

        /// <summary>
        /// サイズによる比較
        /// </summary>
        Size = 1 << 2,

        /// <summary>
        /// SHA1による比較
        /// </summary>
        FileContentsSHA1 = 1 << 3,

        /// <summary>
        /// 生データによるバイナリ比較
        /// </summary>
        /// <remarks>データベースを利用出来ず、暗号化や圧縮と併用できない点に注意</remarks>
        FileContentsBinary = 1 << 4,
    }
}
