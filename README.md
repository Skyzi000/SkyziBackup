# ![SkyziBackup](https://user-images.githubusercontent.com/38061609/146688893-4a185096-a2ed-49bf-bce8-f59ec20b08ae.png)SkyziBackup

[![Release](https://img.shields.io/github/v/release/Skyzi000/SkyziBackup?sort=semver)](https://github.com/Skyzi000/SkyziBackup/releases)
[![Download](https://img.shields.io/github/downloads/Skyzi000/SkyziBackup/total)](https://github.com/Skyzi000/SkyziBackup/releases)
[![Last Commit](https://img.shields.io/github/last-commit/Skyzi000/SkyziBackup)](https://github.com/Skyzi000/SkyziBackup/commits)
[![Build](https://github.com/Skyzi000/SkyziBackup/actions/workflows/build.yml/badge.svg?branch=develop)](https://github.com/Skyzi000/SkyziBackup/actions/workflows/build.yml)
[![CodeQL](https://github.com/Skyzi000/SkyziBackup/actions/workflows/codeql-analysis.yml/badge.svg?branch=develop)](https://github.com/Skyzi000/SkyziBackup/actions/workflows/codeql-analysis.yml)
[![LICENSE](https://img.shields.io/github/license/Skyzi000/SkyziBackup)](https://github.com/Skyzi000/SkyziBackup/blob/main/LICENSE)
[![Twitter](https://img.shields.io/twitter/follow/skyzi000?style=social)](https://twitter.com/skyzi000)

[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=alert_status)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=sqale_rating)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=reliability_rating)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=security_rating)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=sqale_index)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=ncloc)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)

ファイル単位で圧縮と暗号化のできる、インストール不要の無料ファイルバックアップソフトです。  
自分に合う無料のファイルバックアップソフトが見つからなかったので、C#の勉強もかねて自作しました。  
しかしながらこのソフトは未完成であり、致命的な不具合が存在する可能性が高い点を予めご了承ください。

## 公式サイト

公式サイトを作りました！  
<https://skyzibackup.skyzi.jp/>  
まだコンテンツは少ないですが、上記リンクから見ることができます。

## 特徴

- オープンソース
- インストール不要
- AesCngクラスを利用した高速かつ強力なAES256(CBCモード)暗号化
- 暗号化したファイルはOpenSSLを使って個別に復号可能
- ファイル単位で圧縮＆暗号化が可能
- データベースを利用することで、バックアップ先ドライブが低速な場合でも高速にファイルの比較が可能
- 作成日時・更新日時・ファイル属性をコピー可能(セキュリティ属性や、圧縮/暗号化/スパース属性は未対応)
- 削除または上書きされたファイルのバージョン管理機能を搭載
- 除外パターンで柔軟な除外設定が可能
- ハッシュ値(SHA1)によるファイルの比較が可能
- リパースポイント(シンボリックリンク/ジャンクション)の取り扱い方を選択可能
- 同時に複数のバックアップを実行可能
- 260字以上の長いファイルパスに対応
- 詳細なログ出力

## 削除方法

ファイル(F)メニューのデータ保存先を開くで開いた先のフォルダを削除  
%LOCALAPPDATA%\Skyzi000\ 以降のSkyziBackupで始まる名前のフォルダを削除  
ダウンロードしたフォルダを削除

## 動作環境

Windows 10, 11 (64bit)
最新の .NET デスクトップ ランタイム (x64) がインストールされている必要があります。  
必要なランタイムは以下のページからインストールできます。  
<https://dotnet.microsoft.com/ja-jp/download/dotnet/6.0>  
.NET "Desktop" Runtime でないと動かないので気を付けてください！

## ダウンロード

<https://github.com/skyzi000/SkyziBackup/releases/latest>

## コマンドライン引数

```cmd
SkyziBackup.exe バックアップ元フォルダ バックアップ先フォルダ
```

上の例のように引数を2つ与えることで、バックグラウンドで起動しバックアップした後、自動的にプログラムを終了します。  
タスクスケジューラに登録すると自動的にバックアップできるので便利です。  
(暗号化する場合は一度手動でバックアップして暗号化パスワードを記録しておく必要があります)  

## リストア(復元)

表示(V)メニューからリストアウィンドウを開いて復元します。  
復元時はバックアップ時と同じ設定にしてください。  
オプション(O) > ローカル設定(L)から手動で設定するか、ファイル(F) > 設定をファイルからインポート(I)で設定ファイル(.json)を読み込むことができます。  

### このアプリケーションを使わずに復元する方法(推奨はしません)

このアプリケーションを使って暗号化したファイルは、OpenSSLで復号することもできます。(OpenSSL 1.1.1kで確認)  
例）

```cmd
openssl enc -d -aes256 -pbkdf2 -in 復号したいファイル -out 復号後のファイル -k "password"
```

また、圧縮と暗号化を両方有効にしている場合は、復号してから解凍します。  
PowerShellを利用して解凍することができます。  
(例: <https://gist.github.com/skyzi000/2c3b8710aea35f0fd7d5f97fdfbda16c> )  

## ビルド

Visual Studio 2022 が必要です。  

## 連絡先

- [Googleフォーム(匿名)](https://forms.gle/WevPFNfJR5FRphi37)
- [新規Issue](https://github.com/Skyzi000/SkyziBackup/issues/new/choose)
- [Discussions](https://github.com/Skyzi000/SkyziBackup/discussions)
- [暗号化メールフォーム(FlowCrypt)](https://flowcrypt.com/me/skyzi000)

必ずしも対応できるとは限りません。予めご了承ください。  

[よくある質問ページ](https://skyzibackup.skyzi.jp/faq)もどうぞ。

## ライセンス

このアプリケーションはMIT Licenseのもとで公開されています。  
<https://github.com/skyzi000/SkyziBackup/blob/develop/LICENSE>

## サードパーティーライセンス

このアプリケーションは下記のライブラリを使用しています。  

### [ModernWPF UI Library](https://github.com/Kinnara/ModernWpf)

Copyright (c) 2019 Yimeng Wu  
<https://github.com/Kinnara/ModernWpf/blob/master/LICENSE>

### [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning)

Copyright (c) .NET Foundation and Contributors  
<https://github.com/dotnet/Nerdbank.GitVersioning/blob/master/LICENSE>

### [NLog](https://github.com/NLog/NLog)

Copyright (c) 2004-2021 Jaroslaw Kowalski &lt;jaak@jkowalski.net&gt;, Kim Christensen, Julian Verdurmen  
<https://github.com/NLog/NLog/blob/master/LICENSE.txt>

### [github-changelog-generator](https://github.com/github-changelog-generator/github-changelog-generator)

Copyright (c) 2016-2019 Petr Korolev  
<https://github.com/github-changelog-generator/github-changelog-generator/blob/master/LICENSE>
