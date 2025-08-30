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

<!-- フィーチャーセクション -->
<div class="card-grid fade-in">
  <div class="feature-card">
    <div class="feature-icon">
      🔐
    </div>
    <h3 class="feature-title">強力な暗号化</h3>
    <p class="feature-description">AES256(CBCモード)による高速で安全な暗号化。OpenSSLとの互換性も確保。</p>
    <a href="./encryption" class="feature-link">
      詳細を見る
      <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
        <path d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
      </svg>
    </a>
  </div>

  <div class="feature-card">
    <div class="feature-icon">
      ⚡
    </div>
    <h3 class="feature-title">高速バックアップ</h3>
    <p class="feature-description">データベースを活用した高速ファイル比較。大容量データも効率的に処理。</p>
    <a href="./manual" class="feature-link">
      使い方を見る
      <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
        <path d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
      </svg>
    </a>
  </div>

  <div class="feature-card">
    <div class="feature-icon">
      📦
    </div>
    <h3 class="feature-title">インストール不要</h3>
    <p class="feature-description">ポータブル設計でどこでも使用可能。レジストリを汚さずクリーンな運用。</p>
    <a href="./screenshots" class="feature-link">
      スクリーンショット
      <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
        <path d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
      </svg>
    </a>
  </div>

  <div class="feature-card">
    <div class="feature-icon">
      🎯
    </div>
    <h3 class="feature-title">柔軟な設定</h3>
    <p class="feature-description">除外パターン、バージョン管理、圧縮設定など豊富なカスタマイズオプション。</p>
    <a href="./faq" class="feature-link">
      FAQ
      <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
        <path d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
      </svg>
    </a>
  </div>
</div>

## 主な機能

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">セキュリティ機能</h3>
  </div>
  <ul>
    <li><strong>AES256暗号化</strong>: AesCngクラスによる高速かつ強力な暗号化</li>
    <li><strong>OpenSSL互換</strong>: 暗号化ファイルをOpenSSLで個別復号可能</li>
    <li><strong>パスワード保護</strong>: 暗号化パスワードの安全な管理</li>
  </ul>
</div>

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">パフォーマンス機能</h3>
  </div>
  <ul>
    <li><strong>高速比較</strong>: データベースによる効率的なファイル比較</li>
    <li><strong>並列処理</strong>: 同時に複数のバックアップを実行可能</li>
    <li><strong>長いパス対応</strong>: 260字以上のファイルパスに対応</li>
    <li><strong>ハッシュ値比較</strong>: SHA1ハッシュによる正確なファイル比較</li>
  </ul>
</div>

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">運用機能</h3>
  </div>
  <ul>
    <li><strong>バージョン管理</strong>: 削除・上書きファイルの履歴保持</li>
    <li><strong>除外設定</strong>: 柔軟な除外パターンの設定</li>
    <li><strong>属性保持</strong>: 作成日時・更新日時・ファイル属性のコピー</li>
    <li><strong>リパースポイント対応</strong>: シンボリックリンク・ジャンクションの取り扱い選択</li>
    <li><strong>詳細ログ</strong>: 充実したログ出力機能</li>
  </ul>
</div>

## 目次

- auto-gen TOC:
{:toc}

## 削除方法

ファイル(F)メニューのデータ保存先を開くで開いた先のフォルダを削除  
%LOCALAPPDATA%\Skyzi000\ 以降のSkyziBackupで始まる名前のフォルダを削除  
ダウンロードしたフォルダを削除

## 動作環境

Windows 10 Version 1903 (64bit) 以降  
最新の .NET Desktop Runtime 5.0 (x64) がインストールされている必要があります。  
必要なランタイムは以下のページからインストールできます。  
<https://dotnet.microsoft.com/download/dotnet/5.0>  
.NET "Desktop" Runtime でないと動かないので気を付けてください！

## ダウンロード

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">最新版をダウンロード</h3>
    <div class="content-card-meta">オープンソース・無料・インストール不要</div>
  </div>
  <p>最新の安定版をGitHubリリースページからダウンロードできます。</p>
  <div class="btn-group">
    <a href="https://github.com/skyzi000/SkyziBackup/releases/latest" class="btn btn-primary">
      <svg class="btn-icon" width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
        <path d="M7.47 10.78a.75.75 0 001.06 0l3.75-3.75a.75.75 0 00-1.06-1.06L8.75 8.44V1.75a.75.75 0 00-1.5 0v6.69L4.78 5.97a.75.75 0 00-1.06 1.06l3.75 3.75zM3.75 13a.75.75 0 000 1.5h8.5a.75.75 0 000-1.5h-8.5z"/>
      </svg>
      最新版ダウンロード
    </a>
    <a href="https://github.com/Skyzi000/SkyziBackup" class="btn btn-secondary">
      <svg class="btn-icon" width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
        <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z"/>
      </svg>
      ソースコード
    </a>
  </div>
</div>

## 使い方

<div class="card-grid-2 fade-in">
  <div class="content-card">
    <div class="content-card-header">
      <h3 class="content-card-title">GUI での使用</h3>
    </div>
    <ol>
      <li>バックアップ元フォルダを指定</li>
      <li>バックアップ先フォルダを指定</li>
      <li>暗号化したい場合はパスワードを入力</li>
      <li>バックアップ開始ボタンを押す</li>
      <li>バックアップ完了</li>
    </ol>
    <a href="./manual" class="btn btn-ghost btn-sm">詳細マニュアル</a>
  </div>

  <div class="content-card">
    <div class="content-card-header">
      <h3 class="content-card-title">コマンドライン</h3>
    </div>
    <p>引数を2つ与えることで、バックグラウンドで起動しバックアップ後に自動終了します。</p>
    <pre><code>SkyziBackup.exe バックアップ元フォルダ バックアップ先フォルダ</code></pre>
    <p><small>タスクスケジューラに登録すると自動バックアップが可能です。</small></p>
  </div>
</div>

## リストア（復元）

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">復元方法</h3>
  </div>
  <p>表示(V)メニューからリストアウィンドウを開いて復元します。復元時はバックアップ時と同じ設定にしてください。</p>
  
  <h4>設定の復元方法</h4>
  <ul>
    <li><strong>手動設定</strong>: オプション(O) > ローカル設定(L)から設定</li>
    <li><strong>設定ファイル</strong>: ファイル(F) > 設定をファイルからインポート(I)で.jsonファイルを読み込み</li>
  </ul>

  <details>
    <summary><strong>OpenSSLを使った復元（上級者向け）</strong></summary>
    <p>このアプリケーションを使わずにOpenSSLで復号することも可能です（OpenSSL 1.1.1kで確認）:</p>
    <pre><code>openssl enc -d -aes256 -pbkdf2 -in 復号したいファイル -out 復号後のファイル -k "password"</code></pre>
    <p>圧縮と暗号化を両方有効にしている場合は、復号してから解凍が必要です。</p>
    <a href="https://gist.github.com/skyzi000/2c3b8710aea35f0fd7d5f97fdfbda16c" class="btn btn-text btn-sm">PowerShell解凍例</a>
  </details>
</div>

## サポート・連絡先

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">お問い合わせ</h3>
    <div class="content-card-meta">バグ報告・機能要望・質問など</div>
  </div>
  <p>以下の方法でお気軽にお問い合わせください。必ずしも対応できるとは限りませんが、可能な限りサポートいたします。</p>
  
  <div class="btn-group">
    <a href="https://forms.gle/WevPFNfJR5FRphi37" class="btn btn-primary btn-sm">
      📝 Googleフォーム（匿名）
    </a>
    <a href="https://github.com/Skyzi000/SkyziBackup/issues/new/choose" class="btn btn-secondary btn-sm">
      🐛 新規Issue
    </a>
    <a href="https://github.com/Skyzi000/SkyziBackup/discussions" class="btn btn-ghost btn-sm">
      💬 Discussions
    </a>
    <a href="https://flowcrypt.com/me/skyzi000" class="btn btn-text btn-sm">
      🔐 暗号化メール
    </a>
  </div>
  
  <p><a href="./faq" class="feature-link">よくある質問ページ</a>もあわせてご確認ください。</p>
</div>

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
