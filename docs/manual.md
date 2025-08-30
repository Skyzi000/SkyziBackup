---
title: SkyziBackup マニュアル
description: SkyziBackupの使い方と設定方法を詳しく解説
layout: default
permalink: manual
---

SkyziBackupの基本的な使い方から高度な設定まで、詳しく説明します。

## 目次

- auto-gen TOC:
{:toc}

## クイックスタート

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">🚀 5ステップで簡単バックアップ</h3>
    <div class="content-card-meta">初回使用時の基本操作</div>
  </div>
  
  <div class="card-grid-2">
    <div class="feature-card">
      <div class="feature-icon">📁</div>
      <h4 class="feature-title">1. バックアップ元を指定</h4>
      <p class="feature-description">保護したいファイルが入っているフォルダを選択します。</p>
    </div>
    
    <div class="feature-card">
      <div class="feature-icon">💾</div>
      <h4 class="feature-title">2. バックアップ先を指定</h4>
      <p class="feature-description">バックアップファイルを保存する場所を選択します。</p>
    </div>
    
    <div class="feature-card">
      <div class="feature-icon">🔐</div>
      <h4 class="feature-title">3. 暗号化設定（任意）</h4>
      <p class="feature-description">セキュリティが必要な場合はパスワードを入力します。</p>
    </div>
    
    <div class="feature-card">
      <div class="feature-icon">▶️</div>
      <h4 class="feature-title">4. バックアップ開始</h4>
      <p class="feature-description">バックアップ開始ボタンを押して処理を実行します。</p>
    </div>
  </div>
  
  <div class="content-card" style="margin-top: var(--space-6); text-align: center;">
    <h4>✅ 5. バックアップ完了</h4>
    <p>進行状況バーが100%になったら完了です。ログで詳細を確認できます。</p>
    <a href="../screenshots#main" class="btn btn-primary btn-sm">画面を見る</a>
  </div>
</div>

## 自動バックアップの設定

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">⏰ Windowsタスクスケジューラの活用</h3>
    <div class="content-card-meta">定期的な自動バックアップを実現</div>
  </div>
  
  <p>SkyziBackupには自動バックアップ機能が未実装のため、Windows標準のタスクスケジューラを使用します。</p>
  
  <h4>設定手順</h4>
  
  <div class="content-card" style="margin: var(--space-4) 0;">
    <h5>1. タスクスケジューラを開く</h5>
    <img src="https://user-images.githubusercontent.com/38061609/140870691-e742225b-88a0-4653-bb4a-e9db167ca203.png" alt="タスクスケジューラ" style="width: 100%; border-radius: 8px; box-shadow: var(--shadow-md); margin: var(--space-3) 0;">
    <p>スタートメニューから「タスクスケジューラ」を検索して開きます。</p>
  </div>
  
  <div class="content-card" style="margin: var(--space-4) 0;">
    <h5>2. フォルダを作成（任意）</h5>
    <img src="https://user-images.githubusercontent.com/38061609/140871071-a555dc98-d60b-4323-8dee-afe9177a5610.png" alt="新しいフォルダー" style="width: 100%; border-radius: 8px; box-shadow: var(--shadow-md); margin: var(--space-3) 0;">
    <p>管理しやすくするため、専用フォルダを作成することをお勧めします。</p>
  </div>
  
  <div class="content-card" style="margin: var(--space-4) 0;">
    <h5>3. 基本タスクを作成</h5>
    <img src="https://user-images.githubusercontent.com/38061609/140871460-6a6e4a62-5bf7-46e7-a936-6322800508e1.png" alt="タスクの作成" style="width: 100%; border-radius: 8px; box-shadow: var(--shadow-md); margin: var(--space-3) 0;">
    <p>フォルダを選択した状態で、「操作」→「基本タスクの作成」を選択します。</p>
  </div>
  
  <div class="content-card" style="margin: var(--space-4) 0;">
    <h5>4. プログラムの設定</h5>
    <img src="https://user-images.githubusercontent.com/38061609/140872392-d5925f6c-eb58-4939-8add-98744c3417fd.png" alt="プログラムの開始画面" style="width: 100%; border-radius: 8px; box-shadow: var(--shadow-md); margin: var(--space-3) 0;">
    <p>名前とトリガーを設定し、「プログラムの開始」を選択します。</p>
  </div>
  
  <div class="content-card" style="margin: var(--space-4) 0; background: var(--color-gray-50);">
    <h5>⚙️ コマンドライン引数の設定</h5>
    <p><strong>プログラム/スクリプト</strong>: <code>SkyziBackup.exe</code>のパスを指定</p>
    <p><strong>引数の追加</strong>:</p>
    <pre><code>&lt;バックアップ元フォルダのパス&gt; &lt;バックアップ先フォルダのパス&gt;</code></pre>
    <p><small>※ v0.3時点の仕様であり、将来のバージョンで変更される可能性があります。</small></p>
  </div>
  
  <div class="content-card" style="margin: var(--space-4) 0; border-left: 4px solid var(--color-warning);">
    <h5>⚠️ 暗号化利用時の注意点</h5>
    <p>自動バックアップで暗号化を使用する場合は、事前に手動でバックアップを実行してパスワードを保存しておく必要があります。設定ファイルは自動的に読み込まれます。</p>
  </div>
  
  <div class="btn-group" style="margin-top: var(--space-6);">
    <a href="../screenshots#backup-settings" class="btn btn-secondary btn-sm">設定画面を見る</a>
    <a href="#advanced" class="btn btn-ghost btn-sm">高度な設定</a>
  </div>
</div>

## 設定ファイル

v0.3時点ではデフォルト設定とローカル設定の2種類があります。

ローカル設定はデフォルト設定をもとに自動的に作成され、暗号化されたパスワードなどの情報も記録されます。

設定ウィンドウではツールチップに詳しい説明を書いているので、設定時の参考にしてください。

---
※以下の説明は執筆予定です。

### バックアップ設定

### 除外設定

### 比較設定

### 暗号化設定

### 圧縮設定

### 上級者向け設定
