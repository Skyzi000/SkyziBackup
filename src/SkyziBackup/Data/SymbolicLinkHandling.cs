namespace SkyziBackup.Data
{
    /// <summary>
    /// シンボリックリンクやジャンクション(リパースポイント)の取り扱い
    /// </summary>
    public enum SymbolicLinkHandling
    {
        /// <summary>
        /// リパースポイントのディレクトリを無視して、ファイルへのシンボリックリンクの場合はターゲットの実体をバックアップする(デフォルト動作)
        /// </summary>
        /// <remarks>ファイルの属性を確認しない分ちょっと速いかも</remarks>
        IgnoreOnlyDirectories = 0,

        /// <summary>
        /// ディレクトリだけでなくファイルのシンボリックリンクも無視する
        /// </summary>
        IgnoreAll = 1,

        /// <summary>
        /// ターゲットの実体をバックアップする
        /// </summary>
        /// <remarks>無限ループになる可能性があるので注意</remarks>
        Follow = 2,

        /// <summary>
        /// シンボリックリンク/ジャンクション自体をバックアップ先に可能な限り再現する(ターゲットパスは変更しない)
        /// </summary>
        /// <remarks>ミラーリング機能を有効にしている場合、リンク先の実体が削除される恐れがあるので注意</remarks>
        Direct = 3,
    }
}
