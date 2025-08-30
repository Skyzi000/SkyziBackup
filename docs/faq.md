---
title: SkyziBackup よくある質問
description: SkyziBackupのFAQ（よくある質問）と回答集
layout: default
permalink: faq
---

SkyziBackupについてよく寄せられる質問にお答えします。お探しの情報が見つからない場合は、お気軽にお問い合わせください。

## 目次

- auto-gen TOC:
{:toc}

## 📋 基本的な質問

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">どうしてこのソフトを作ったのですか？</h3>
    <div class="content-card-meta">開発動機について</div>
  </div>
  
  <p>以下の条件を満たすファイルバックアップソフトが見つからなかったため、自作することにしました。</p>
  
  <div class="card-grid-2">
    <div class="feature-card">
      <div class="feature-icon">💰</div>
      <h4 class="feature-title">コスト・ライセンス</h4>
      <ul style="text-align: left;">
        <li>無料で使える</li>
        <li>オープンソース</li>
        <li>商用利用可能</li>
      </ul>
    </div>
    
    <div class="feature-card">
      <div class="feature-icon">🔐</div>
      <h4 class="feature-title">暗号化・圧縮</h4>
      <ul style="text-align: left;">
        <li>ファイル単位で暗号化</li>
        <li>OpenSSL互換の暗号化</li>
        <li>柔軟な圧縮設定</li>
      </ul>
    </div>
    
    <div class="feature-card">
      <div class="feature-icon">⚡</div>
      <h4 class="feature-title">パフォーマンス</h4>
      <ul style="text-align: left;">
        <li>高速処理</li>
        <li>GUIが応答なしにならない</li>
        <li>低リソース消費</li>
      </ul>
    </div>
    
    <div class="feature-card">
      <div class="feature-icon">🛠️</div>
      <h4 class="feature-title">技術的特徴</h4>
      <ul style="text-align: left;">
        <li>リパースポイント対応</li>
        <li>長いファイルパス対応（260字以上）</li>
        <li>無限ループ回避</li>
      </ul>
    </div>
  </div>
</div>

## 🔧 技術的な質問

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">どのような技術を使っていますか？</h3>
    <div class="content-card-meta">開発技術スタック</div>
  </div>
  
  <p>.NET 5.0のWPFアプリケーションとして、C#で開発しています。</p>
  
  <h4>🔐 暗号化技術</h4>
  <p><a href="https://docs.microsoft.com/ja-jp/windows/win32/seccng/cng-portal">Cryptography Next Generation (CNG)</a>を使用する<a href="https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.aescng">AesCngクラス</a>により、高速かつ強力な暗号化処理を実現しています。</p>
  
  <div class="content-card" style="margin: var(--space-4) 0; background: var(--color-gray-50);">
    <h5>⚡ パフォーマンス比較</h5>
    <p>環境により異なりますが、単純な暗号化処理では<a href="https://www.openssl.org/">OpenSSL</a> (<code>openssl enc -e -aes256 -pbkdf2</code>)より高速でした。</p>
  </div>
  
  <div class="content-card" style="margin: var(--space-4) 0; border-left: 4px solid var(--color-primary-start);">
    <h5>🔗 OpenSSL互換性</h5>
    <p>初期化ベクトルやソルトの処理にOpenSSLと同じ方法を採用しているため、OpenSSLでの復号も可能です。</p>
    <a href="./encryption" class="btn btn-text btn-sm">暗号化方式の詳細</a>
  </div>
  
  <h4>🚀 開発・デプロイ</h4>
  <ul>
    <li><strong>自動ビルド</strong>: GitHub Actionsによる自動化</li>
    <li><strong>継続的インテグレーション</strong>: コード品質管理</li>
    <li><strong>将来計画</strong>: .NET 6.0やMAUIへの移行検討</li>
  </ul>
</div>

## 💾 使用・運用に関する質問

<div class="card-grid fade-in">
  <div class="content-card">
    <div class="content-card-header">
      <h3 class="content-card-title">バックアップ容量の制限</h3>
    </div>
    <p><strong>意図的な制限は設けていません。</strong></p>
    <p>データベース利用時でも、500MBのメモリで約50万ファイルを処理可能です。</p>
  </div>
  
  <div class="content-card">
    <div class="content-card-header">
      <h3 class="content-card-title">アップデート方法</h3>
    </div>
    <p>実行ファイルを新しいバージョンで上書きしてください。</p>
    <p>設定ファイル等は自動で引き継がれます。</p>
    <p><small>※ 自動アップデート機能は検討中です。</small></p>
  </div>
  
  <div class="content-card">
    <div class="content-card-header">
      <h3 class="content-card-title">再配布の可否</h3>
    </div>
    <p>著作権表示など、<a href="https://github.com/skyzi000/SkyziBackup/blob/develop/LICENSE">ライセンス</a>に従っていただければ問題ありません。</p>
    <p>MITライセンスで公開されています。</p>
  </div>
</div>

## 🐛 トラブルシューティング

<div class="card-grid fade-in">
  <div class="content-card">
    <div class="content-card-header">
      <h3 class="content-card-title">「予期しない例外」が発生</h3>
    </div>
    <p><strong>対処方法：</strong></p>
    <ol>
      <li>一度プログラムを終了</li>
      <li>再度バックアップを実行</li>
    </ol>
    <p>それでも解決しない場合は、お問い合わせください。</p>
  </div>
  
  <div class="content-card">
    <div class="content-card-header">
      <h3 class="content-card-title">一部ファイルのみ復元</h3>
    </div>
    <p><strong>現在の機能：</strong> フォルダ単位での復元のみ対応</p>
    <p><strong>今後の予定：</strong> ファイル単位の復元機能を実装予定</p>
  </div>
  
  <div class="content-card">
    <div class="content-card-header">
      <h3 class="content-card-title">暗号化・圧縮の詳細</h3>
    </div>
    <p>技術的な詳細については専用ページをご確認ください。</p>
    <a href="./encryption" class="btn btn-primary btn-sm">暗号化方式の詳細</a>
  </div>
</div>

## 📞 サポート・お問い合わせ

<div class="content-card fade-in">
  <div class="content-card-header">
    <h3 class="content-card-title">バグ報告・機能提案</h3>
    <div class="content-card-meta">お気軽にお問い合わせください</div>
  </div>
  
  <p>以下の方法でバグ報告や機能提案を受け付けています。お好きな方法をご利用ください。</p>
  
  <div class="btn-group">
    <a href="https://github.com/Skyzi000/SkyziBackup/issues/new/choose" class="btn btn-primary">
      <svg class="btn-icon" width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
        <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z"/>
      </svg>
      GitHub Issues
    </a>
    <a href="https://github.com/Skyzi000/SkyziBackup/discussions" class="btn btn-secondary">
      💬 GitHub Discussions
    </a>
    <a href="https://forms.gle/WevPFNfJR5FRphi37" class="btn btn-ghost">
      📝 Googleフォーム
    </a>
    <a href="https://flowcrypt.com/me/skyzi000" class="btn btn-text">
      🔐 暗号化メール
    </a>
  </div>
  
  <div class="content-card" style="margin-top: var(--space-6); border-left: 4px solid var(--color-warning);">
    <h5>⚠️ サポートに関する注意点</h5>
    <p>対応が遅くなったり、必ずしも対応できるとは限らない場合があります。予めご了承ください。</p>
  </div>
</div>

## 🔗 関連ページ

<div class="card-grid-2 fade-in">
  <div class="feature-card">
    <div class="feature-icon">📖</div>
    <h3 class="feature-title">使い方ガイド</h3>
    <p class="feature-description">基本操作から応用設定まで詳しく解説</p>
    <a href="./manual" class="feature-link">
      マニュアルを見る
      <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
        <path d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
      </svg>
    </a>
  </div>

  <div class="feature-card">
    <div class="feature-icon">🔐</div>
    <h3 class="feature-title">暗号化技術</h3>
    <p class="feature-description">AES256暗号化の技術的詳細</p>
    <a href="./encryption" class="feature-link">
      暗号化の詳細
      <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
        <path d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
      </svg>
    </a>
  </div>
</div>
