# SkyziBackup
ファイル単位で圧縮と暗号化のできる、インストール不要の無料ファイルバックアップソフトです。  
自分に合う無料のファイルバックアップソフトが見つからなかったので、C#の勉強もかねて自作しました。  
ちなみに、作者は現在このソフトで4TB以上のデータを毎日バックアップしています。  
しかしながらこのソフトは未完成であり、致命的な不具合が存在する可能性が高い点を予めご了承ください。

## ダウンロード
https://github.com/skyzi000/SkyziBackup/releases/latest

## 特徴
- オープンソース
- インストール不要
- AesCngクラスを利用した高速/強力なAES256(CBCモード)暗号化
- 暗号化したファイルはOpenSSLを使って復号可能(多分)
- ファイル単位で圧縮＆暗号化が可能
- データベースを利用することで、バックアップ先ドライブが低速な場合でも高速にファイルの比較が可能
- 作成日時・更新日時・ファイル属性をコピー可能(セキュリティ属性や、圧縮/暗号化/スパース属性は未対応)
- 削除または上書きされたファイルのバージョン管理機能を搭載
- 除外パターンで柔軟な除外設定が可能
- ハッシュ値(SHA1)によるファイルの比較が可能
- リパースポイント(シンボリックリンク/ジャンクション)の取り扱い方を4種類から選択可能
- 同時に複数のバックアップを実行可能
- 260字以上の長いファイルパスに対応
- 詳細なログ出力

## 削除方法
ファイル(F)メニューのデータ保存先を開くで開いた先のフォルダを削除  
%LOCALAPPDATA%\Skyzi000\ 以降のSkyziBackupで始まる名前のフォルダを削除  
ダウンロードしたフォルダを削除

## 動作環境
Windows 10 Version 1903 (64bit) 以降  
最新の.NET Desktop Runtime 5.0 (x64)がインストールされている必要があります。
必要なランタイムはこちらからインストールできます。
https://dotnet.microsoft.com/download/dotnet/5.0

## コマンドライン引数
```
SkyziBackup.exe バックアップ元フォルダ バックアップ先フォルダ
```
- 上の例のように引数を2つ与えることで、バックグラウンドで起動しバックアップした後、自動的にプログラムを終了します。  
タスクスケジューラに登録すると自動的にバックアップできるので便利です。(暗号化する場合は一度手動でバックアップして暗号化パスワードを記録しておく必要があります)

## リストア(復元)
表示(V)メニューからリストアウィンドウを開いて復元します。  
復元時はバックアップ時と同じ設定にしてください。  

### このアプリケーションを使わずに復元する方法(推奨はしません)
このアプリケーションを使って暗号化したファイルは、OpenSSLで復号することもできます。(OpenSSL 1.1.1kで確認)  
例）
```
openssl enc -d -aes256 -pbkdf2 -in 復号したいファイル -out 復号後のファイル -k "password"
```
また、圧縮と暗号化を両方有効にしている場合は、復号してから解凍します。  
PowerShellを利用して解凍することができます。(例: https://gist.github.com/skyzi000/2c3b8710aea35f0fd7d5f97fdfbda16c )

## ビルド
Visual Studio 2019 最新版が必要です。

## 連絡先
Twitter: https://twitter.com/skyzi000  
必ずしも対応できるとは限りません。ご了承ください。

## ライセンス
このアプリケーションはMIT Licenseのもとで公開されています。  
https://github.com/skyzi000/SkyziBackup/blob/develop/LICENSE

## サードパーティーライセンス
このアプリケーションは下記のライブラリを使用しています。

ModernWPF UI Library  
Copyright (c) 2019 Yimeng Wu  
https://github.com/Kinnara/ModernWpf/blob/master/LICENSE

Nerdbank.GitVersioning  
Copyright (c) .NET Foundation and Contributors  
https://github.com/dotnet/Nerdbank.GitVersioning/blob/master/LICENSE

NLog  
Copyright (c) 2004-2021 Jaroslaw Kowalski &lt;jaak@jkowalski.net&gt;, Kim Christensen, Julian Verdurmen  
https://github.com/NLog/NLog/blob/master/LICENSE.txt
