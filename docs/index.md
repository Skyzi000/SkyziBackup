---
# Feel free to add content and custom Front Matter to this file.
# To modify the layout, see https://jekyllrb.com/docs/themes/#overriding-theme-defaults
title: SkyziBackup
description: ファイル単位で圧縮と暗号化のできる、インストール不要の無料ファイルバックアップソフト
layout: default
---

[![Release](https://img.shields.io/github/v/release/Skyzi000/SkyziBackup?sort=semver)](https://github.com/Skyzi000/SkyziBackup/releases)
[![Download](https://img.shields.io/github/downloads/Skyzi000/SkyziBackup/total)](https://github.com/Skyzi000/SkyziBackup/releases)
[![Last Commit](https://img.shields.io/github/last-commit/Skyzi000/SkyziBackup)](https://github.com/Skyzi000/SkyziBackup/commits)
[![Build](https://github.com/Skyzi000/SkyziBackup/actions/workflows/build.yml/badge.svg)](https://github.com/Skyzi000/SkyziBackup/actions/workflows/build.yml)
[![LICENSE](https://img.shields.io/github/license/Skyzi000/SkyziBackup)](https://github.com/Skyzi000/SkyziBackup/blob/main/LICENSE)
[![Twitter](https://img.shields.io/twitter/follow/skyzi000?style=social)](https://twitter.com/skyzi000)

[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=alert_status)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=sqale_rating)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=reliability_rating)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=security_rating)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=sqale_index)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=Skyzi000_SkyziBackup&metric=ncloc)](https://sonarcloud.io/dashboard?id=Skyzi000_SkyziBackup)


ファイル単位で圧縮と暗号化のできる、インストール不要の無料ファイルバックアップソフトです。  

## ページ
- [スクリーンショット](./screenshots.html)
- [マニュアル](./manual.html)
- [FAQ](./faq.html)

## 目次
* auto-gen TOC:
{:toc}

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
Windows 10 Version 1903 (64bit) 以降  
最新の .NET Desktop Runtime 5.0 (x64) がインストールされている必要があります。  
必要なランタイムは以下のページからインストールできます。  
https://dotnet.microsoft.com/download/dotnet/5.0

## ダウンロード
https://github.com/skyzi000/SkyziBackup/releases/latest

## コマンドライン引数
```
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
```
openssl enc -d -aes256 -pbkdf2 -in 復号したいファイル -out 復号後のファイル -k "password"
```
また、圧縮と暗号化を両方有効にしている場合は、復号してから解凍します。  
PowerShellを利用して解凍することができます。  
(例: https://gist.github.com/skyzi000/2c3b8710aea35f0fd7d5f97fdfbda16c )  

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
