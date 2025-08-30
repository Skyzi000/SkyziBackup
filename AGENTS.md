# Repository Guidelines

本ドキュメントは SkyziBackup (.NET 6 / WPF + Windows Forms) への効率的かつ安全な貢献方法を示します。タイトルは要件に従い英語ですが内容は日本語です。

## プロジェクト構成
ルート: ソリューション `SkyziBackup.sln` / メインプロジェクト `SkyziBackup.csproj`。
- `src/SkyziBackup/`: UI・エントリ (`App.xaml.cs`), バックアップ/リストア制御 (`BackupController`, `RestoreController`).
- `src/Skyzi000/`: 共有ユーティリティ (暗号化, IO, Data 取得, 拡張メソッド)。
- `Properties/`: 設定・PublishProfile 生成物。
- `images/`, `NLog.config`, `version.json`: アセットと設定。
現在テストプロジェクトは未作成。将来的には `tests/SkyziBackup.Tests` (xUnit) を推奨。

## ビルド / 実行
前提: Windows 10/11 x64, .NET 6 SDK, Visual Studio 2022 (WPF ワークロード)。代表コマンド:
```bash
dotnet restore
dotnet build
dotnet run --project SkyziBackup.csproj -- "<バックアップ元>" "<バックアップ先>"
dotnet publish -c Release -r win-x64 --self-contained false
```
CI: GitHub Actions (build / CodeQL / SonarCloud)。警告は極力ゼロ。

## コーディング規約
C# 10 / Nullable 有効。命名: 型/メソッドは `PascalCase`、ローカル/引数は `camelCase`。プライベートフィールド追加時は `_camelCase` を使用可。`Async` メソッドは接尾辞 `Async`。インデント 4 スペース、1 ファイル過長 (>~400 行) なら分割検討。既存名前空間 (`SkyziBackup`, `Skyzi000.*`) を維持。ログは `LogManager.GetCurrentClassLogger()`。重複コードは既存ヘルパー (例: `BackupManager.ComputeFileSHA1`) を再利用。

## テスト (未導入)
現状このリポジトリには自動テストは存在しません。以下は「導入する場合」の参考です:
- 推奨: xUnit + FluentAssertions を `tests/SkyziBackup.Tests` に追加。
- 優先対象: 取消処理、SHA1 計算、長パス/アクセス例外、暗号/圧縮の組み合わせ。
- 命名例: `BackupController_StartBackupAsync_LongPath_HandlesException()`。
自動テストを追加しない変更では、手動確認手順を PR に必ず記述してください。

## コミット & PR
コミットメッセージ: 先頭行を命令形/簡潔に (例: `フォルダ削除を再帰対応`, `Bump NLog from 5.0.5 to 5.1.0`)。Issue クローズは `fix #番号`。依存更新は Dependabot 形式維持。PR 要件:
1. 目的/概要
2. 関連 Issue (`fix #`, `refs #`)
3. UI/例外変更はスクリーンショット/ログ
4. 手動確認手順 (バックアップ/復号/ミラーリング等)
5. 残課題 (TODO) 記載
原則 `develop` 宛に小さく分割。

## セキュリティ / 設定
平文パスワードをログ出力しない。暗号は AES(CBC)+PBKDF2 (OpenSSL 互換)。変更時は互換性記述。パス操作は削除影響範囲を検証 (再帰削除ロジック注意)。Regex は既定タイムアウト(10s)利用; 追加 Regex もタイムアウト明示。`NLog.config` 変更時は機微情報漏洩をチェック。

## 将来改善
ソース内 TODO: static 化見直し / ネイティブ API 利用 / SHA1 検証強化など。各改善は独立した PR としテスト追加。リストア時の SHA1 照合実装を検討。

貢献ありがとうございます。安全で信頼性の高いバックアップを共に育てましょう。
