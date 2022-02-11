# Changelog (変更履歴)

## [v0.4](https://github.com/Skyzi000/SkyziBackup/tree/v0.4) (2022-02-11)

[Full Changelog](https://github.com/Skyzi000/SkyziBackup/compare/v0.3...v0.4)

### 🐞バグ修正

- バックアップ中のオリジナルファイルを移動させると予期しない例外が発生する [\#75](https://github.com/Skyzi000/SkyziBackup/issues/75)

### 📝ドキュメントの変更

- タグがpushされたときにReleaseを自動生成する [\#86](https://github.com/Skyzi000/SkyziBackup/issues/86)
- ChangeLogを自動生成する [\#82](https://github.com/Skyzi000/SkyziBackup/issues/82)
- ドキュメントを作成する [\#59](https://github.com/Skyzi000/SkyziBackup/issues/59)

### ✔その他変更など

- CodeQL Analysisを有効にしてみる [\#83](https://github.com/Skyzi000/SkyziBackup/issues/83)
- Issueテンプレートを用意する [\#78](https://github.com/Skyzi000/SkyziBackup/issues/78)
- リファクタリングする [\#66](https://github.com/Skyzi000/SkyziBackup/issues/66)

### Ⓜマージ済みのプルリクエスト

- 自動リリース用のGitHub Actionが正しく動作するよう修正する [\#98](https://github.com/Skyzi000/SkyziBackup/pull/98) ([Skyzi000](https://github.com/Skyzi000))
- ファイル列挙時のFileNotFoundExceptionとPathTooLongExceptionに対応する [\#95](https://github.com/Skyzi000/SkyziBackup/pull/95) ([Skyzi000](https://github.com/Skyzi000))
- 英語版Issueフォームの修正 [\#94](https://github.com/Skyzi000/SkyziBackup/pull/94) ([Skyzi000](https://github.com/Skyzi000))
- 機能していない日本語版Issueフォームの修正 [\#93](https://github.com/Skyzi000/SkyziBackup/pull/93) ([Skyzi000](https://github.com/Skyzi000))
- Issueフォームを作成 [\#92](https://github.com/Skyzi000/SkyziBackup/pull/92) ([Skyzi000](https://github.com/Skyzi000))
- フィードバック送信用Googleフォームを作成 [\#91](https://github.com/Skyzi000/SkyziBackup/pull/91) ([Skyzi000](https://github.com/Skyzi000))
- Bump Nerdbank.GitVersioning from 3.4.244 to 3.4.255 [\#90](https://github.com/Skyzi000/SkyziBackup/pull/90) ([dependabot[bot]](https://github.com/apps/dependabot))
- Bump NLog from 4.7.12 to 4.7.13 [\#87](https://github.com/Skyzi000/SkyziBackup/pull/87) ([dependabot[bot]](https://github.com/apps/dependabot))
- github-changelog-generatorの導入 [\#85](https://github.com/Skyzi000/SkyziBackup/pull/85) ([Skyzi000](https://github.com/Skyzi000))
- Create codeql-analysis.yml [\#84](https://github.com/Skyzi000/SkyziBackup/pull/84) ([Skyzi000](https://github.com/Skyzi000))
- リファクタリングなど、細かな改善 [\#81](https://github.com/Skyzi000/SkyziBackup/pull/81) ([Skyzi000](https://github.com/Skyzi000))
- Bump Nerdbank.GitVersioning from 3.4.194 to 3.4.244 [\#80](https://github.com/Skyzi000/SkyziBackup/pull/80) ([dependabot[bot]](https://github.com/apps/dependabot))
- Bump NLog from 4.7.10 to 4.7.12 [\#79](https://github.com/Skyzi000/SkyziBackup/pull/79) ([dependabot[bot]](https://github.com/apps/dependabot))
- NuGetパッケージの更新 [\#73](https://github.com/Skyzi000/SkyziBackup/pull/73) ([Skyzi000](https://github.com/Skyzi000))
- Create build.yml [\#72](https://github.com/Skyzi000/SkyziBackup/pull/72) ([Skyzi000](https://github.com/Skyzi000))

## [v0.3](https://github.com/Skyzi000/SkyziBackup/tree/v0.3) (2021-07-11)

[Full Changelog](https://github.com/Skyzi000/SkyziBackup/compare/v0.2.4...v0.3)

### 🐞バグ修正

- バックアップ元フォルダがドライブのルートの場合、バックアップ先がシステムフォルダになってしまう [\#67](https://github.com/Skyzi000/SkyziBackup/issues/67)
- シンボリックリンクを無視しない設定が正しく動作しない不具合 [\#60](https://github.com/Skyzi000/SkyziBackup/issues/60)
- 同時に同じファイルペアでバックアップしようとすると予期しない例外が発生する不具合 [\#58](https://github.com/Skyzi000/SkyziBackup/issues/58)

### 📝ドキュメントの変更

- READMEに.NET Desktop Runtime 5.0が必要であると明確に書く [\#68](https://github.com/Skyzi000/SkyziBackup/issues/68)
- README.mdの更新 [\#70](https://github.com/Skyzi000/SkyziBackup/pull/70) ([Skyzi000](https://github.com/Skyzi000))

### ✔その他変更など

- OSシャットダウン時はバックアップ中でも確認ウィンドウを出さずに自動で終了するようにする [\#57](https://github.com/Skyzi000/SkyziBackup/issues/57)

### Ⓜマージ済みのプルリクエスト

- ベースディレクトリの属性をコピーしないように修正 [\#71](https://github.com/Skyzi000/SkyziBackup/pull/71) ([Skyzi000](https://github.com/Skyzi000))
- Feature/imp auto cancel on session ending [\#64](https://github.com/Skyzi000/SkyziBackup/pull/64) ([Skyzi000](https://github.com/Skyzi000))
- Feature/fix symlink handling [\#61](https://github.com/Skyzi000/SkyziBackup/pull/61) ([Skyzi000](https://github.com/Skyzi000))
- Main [\#48](https://github.com/Skyzi000/SkyziBackup/pull/48) ([Skyzi000](https://github.com/Skyzi000))

## [v0.2.4](https://github.com/Skyzi000/SkyziBackup/tree/v0.2.4) (2021-04-27)

[Full Changelog](https://github.com/Skyzi000/SkyziBackup/compare/v0.2...v0.2.4)

### 🐞バグ修正

- バックアップ中のファイルを削除できない問題 [\#56](https://github.com/Skyzi000/SkyziBackup/issues/56)
- BackupControllerのCTSをDisposeした後にBackupControllerをDisposeすると予期しない例外が発生する不具合 [\#55](https://github.com/Skyzi000/SkyziBackup/issues/55)

## [v0.2](https://github.com/Skyzi000/SkyziBackup/tree/v0.2) (2021-04-26)

[Full Changelog](https://github.com/Skyzi000/SkyziBackup/compare/v0.1...v0.2)

### ✨機能強化

- シンボリックリンクやジャンクションに対応する [\#43](https://github.com/Skyzi000/SkyziBackup/issues/43)
- フォルダ選択ダイアログの実装 [\#20](https://github.com/Skyzi000/SkyziBackup/issues/20)

### 🐞バグ修正

- パスワードを保存できない不具合 [\#51](https://github.com/Skyzi000/SkyziBackup/issues/51)

### ✔その他変更など

- Null許容を有効にする [\#39](https://github.com/Skyzi000/SkyziBackup/issues/39)
- MahApps.Metroを試してみる [\#9](https://github.com/Skyzi000/SkyziBackup/issues/9)

## [v0.1](https://github.com/Skyzi000/SkyziBackup/tree/v0.1) (2021-04-25)

[Full Changelog](https://github.com/Skyzi000/SkyziBackup/compare/4963ff1f3baa8bc80dee5502b410e35f92b4f541...v0.1)

### ✨機能強化

- DataContractSerializerのXMLからSystem.Text.Jsonに移行する [\#41](https://github.com/Skyzi000/SkyziBackup/issues/41)
- ログファイルをアプリから開けるようにする [\#36](https://github.com/Skyzi000/SkyziBackup/issues/36)
- MainWindowのPathを入力するテキストボックスの内容を記憶する [\#33](https://github.com/Skyzi000/SkyziBackup/issues/33)
- 同時に同じバックアップを実行しないようにする [\#31](https://github.com/Skyzi000/SkyziBackup/issues/31)
- データベースの変数名を短い名前にする [\#27](https://github.com/Skyzi000/SkyziBackup/issues/27)
- 設定ファイルからパスワードを分離する [\#25](https://github.com/Skyzi000/SkyziBackup/issues/25)
- バックアップ処理を管理するためのクラスを作る [\#24](https://github.com/Skyzi000/SkyziBackup/issues/24)
- コマンドライン引数を読み込む機能を実装する [\#23](https://github.com/Skyzi000/SkyziBackup/issues/23)
- ミラーリング\(バックアップ先ファイルの削除\)機能の実装 [\#19](https://github.com/Skyzi000/SkyziBackup/issues/19)
- 設定画面の実装 [\#14](https://github.com/Skyzi000/SkyziBackup/issues/14)
- 通知機能の実装 [\#12](https://github.com/Skyzi000/SkyziBackup/issues/12)
- タスクをキャンセルできるようにする [\#6](https://github.com/Skyzi000/SkyziBackup/issues/6)
- 暗号化しないでバックアップできるようにする [\#5](https://github.com/Skyzi000/SkyziBackup/issues/5)
- リストアできるようにする [\#3](https://github.com/Skyzi000/SkyziBackup/issues/3)

### Ⓜマージ済みのプルリクエスト

- Release/v0.1 [\#47](https://github.com/Skyzi000/SkyziBackup/pull/47) ([Skyzi000](https://github.com/Skyzi000))
- Feature/migrate xml to json [\#45](https://github.com/Skyzi000/SkyziBackup/pull/45) ([Skyzi000](https://github.com/Skyzi000))
- Feature/delete dest files [\#40](https://github.com/Skyzi000/SkyziBackup/pull/40) ([Skyzi000](https://github.com/Skyzi000))
- Feature/semaphore during backup [\#35](https://github.com/Skyzi000/SkyziBackup/pull/35) ([Skyzi000](https://github.com/Skyzi000))
- Feature/commandline args コマンドライン引数でのバックアップ実行に対応 [\#34](https://github.com/Skyzi000/SkyziBackup/pull/34) ([Skyzi000](https://github.com/Skyzi000))
- Feature/setting window [\#22](https://github.com/Skyzi000/SkyziBackup/pull/22) ([Skyzi000](https://github.com/Skyzi000))
- Feature/backup without encrypt 非暗号化バックアップの実装 [\#17](https://github.com/Skyzi000/SkyziBackup/pull/17) ([Skyzi000](https://github.com/Skyzi000))
- Feature/restore リストア機能を実装した [\#11](https://github.com/Skyzi000/SkyziBackup/pull/11) ([Skyzi000](https://github.com/Skyzi000))



\* *This Changelog was automatically generated by [github_changelog_generator](https://github.com/github-changelog-generator/github-changelog-generator)*
