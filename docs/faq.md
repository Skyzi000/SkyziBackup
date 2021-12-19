---
title: SkyziBackup
description: FAQ(よくある質問)
layout: default
permalink: faq
---

## 目次

- auto-gen TOC:
{:toc}

## どうしてこのソフトを作ったのですか？

- 無料で使える
- ファイル単位で暗号化できる
  - 暗号化したファイルを別のソフトでも復号できる
- ファイル単位で圧縮できる
  - 圧縮しないファイルを拡張子で指定できる
- パフォーマンスが良い
- GUIが応答なしにならない
- リパースポイントで無限ループに陥らない
- リソースを食いすぎない(バックグラウンドで動かしていても気にならない)
- 260字以上の長いファイルパス名に対応している

……これらを満たすファイルバックアップソフトが見つからなかったからです。

## どのような技術を使っていますか？

.NET 5.0のWPFアプリケーションとして、C#で開発しています。

[Cryptography Next Generation (CNG)](https://docs.microsoft.com/ja-jp/windows/win32/seccng/cng-portal)を使用する[AesCng](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.aescng)クラスを利用することで、高速かつ強力な暗号化処理を実現しています。  
環境によるとは思いますが、単純に暗号化するだけの処理を手元でいくらか試したところ[OpenSSL](https://www.openssl.org/)(`openssl enc -e -aes256 -pbkdf2`)より速かったです。

しかしながら、AES暗号化に必要な初期化ベクトルやソルトの処理などは[OpenSSL](https://www.openssl.org/)でも用いられるものと同じ方法を使っているので、[OpenSSL](https://www.openssl.org/)で復号することも可能です。  
具体的な暗号化方法は[こちら](#暗号化・圧縮はどのようにしていますか？)を参照してください。

更に、GitHub Actionsを用いて自動ビルド環境等も整えています。

今後は.NET 6.0やMAUIなどへの移行も視野に入れていくつもりです。

## バックアップできる容量に制限はありますか？

意図的な制限は設けていません。

データベースを利用する設定であっても、500MBのメモリで約50万のファイルを扱えるはずです。

## アップデートはどうしたらいいですか？

実行ファイルを新しいバージョンで上書きしてください。

設定ファイル等は自動で引き継がれるはずです。

なお、自動アップデート機能は検討中です。

## 再配布などは可能ですか？

著作権の表示など、[ライセンス](https://github.com/skyzi000/SkyziBackup/blob/develop/LICENSE)に従って頂けるなら問題ありません。

## バグ報告や機能の提案はどこからできますか？

現在は[Twitter(@skyzi000)](https://twitter.com/skyzi000)のDMや、GitHubの[Issue](https://github.com/Skyzi000/SkyziBackup/issues)、[Discussions](https://github.com/Skyzi000/SkyziBackup/discussions)ページなどで受け付けています。

GoogleフォームやDiscord、Gitter等の利用も検討中です。

## 「予期しない例外」が発生したのですがどうしたらいいですか？

一度プログラムを終了して、再度バックアップを実行してください。

## 一部のファイルだけを復元することはできますか？

バックアップした中の一つのフォルダだけを復元することはできますが、一部のファイルだけを復元する機能は未実装です。

今後のアップデートで実装される予定です。

## 暗号化・圧縮はどのようにしていますか？

実際のソースコードは[こちら](https://github.com/Skyzi000/SkyziBackup/blob/main/src/Skyzi000/Cryptography/CompressiveAesCryptor.cs)から確認できます。

### ファイルの暗号化・圧縮の流れ

#### 1.ソルトの作成

[RNGCryptoServiceProvider](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.rngcryptoserviceprovider?view=net-5.0)(.NET 6では非推奨になったので対応予定)を利用して暗号強度の高いランダム値を作成し、ソルトとします。

```cs
Salt = GenerateSalt(8);
```

#### 2.暗号化キーと初期化ベクトルの生成

ユーザが入力したパスワードと、[1.](#1.ソルトの作成)で生成したソルトをもとに、[Rfc2898DeriveBytes](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.rfc2898derivebytes?view=net-5.0)を利用して暗号化キーと初期化ベクトルを生成します。  
現在は[OpenSSL](https://www.openssl.org/)のデフォルト値と合わせるため、繰り返し回数は10000、ハッシュアルゴリズムはSHA256に固定しています。  

```cs
(key, Iv) = GetKeyAndIV(GenerateKIV(Salt, password, HashAlgorithm, 10000, keySize / 8 + blockSize));
```

なお、このクラスでは[RFC 2898](https://www.ietf.org/rfc/rfc2898.txt)で定義されたPBKDF2のアルゴリズムを使用しています。  
これは、オープンソースのパスワードマネージャとして有名な[Bitwarden](https://bitwarden.com/)でも使われている([参照](https://bitwarden.com/help/article/what-encryption-is-used/#pbkdf2))、一般的かつ強力なアルゴリズムです。

#### 3.ソルトの書き込み

暗号化ファイルの先頭にプレフィックス`Salted__`とソルトを書き込みます。  
プレフィックスを書き込んでいるのは[OpenSSL](https://www.openssl.org/)のものと合わせるためです。

```cs
output.Write(Encoding.UTF8.GetBytes(prefix));
output.Write(Salt);
```

#### 4.ICryptoTransform・CryptoStreamの作成

暗号化キーと初期化ベクトルを使用して[AesCng](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.aescng)から対称AES暗号化オブジェクト([ICryptoTransform](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.icryptotransform))を作成し、それをもとに[CryptoStream](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.cryptostream?view=net-6.0)を作成します。

```cs
ICryptoTransform cryptoTransform = aes.CreateEncryptor(key, Iv);
using var cryptoStream = new CryptoStream(output, cryptoTransform, CryptoStreamMode.Write);
```

#### 5.DeflateStream/GZipStreamの作成

圧縮するように設定されている場合、この時点で圧縮用のDeflateStream又はGZipStreamを作成します。

```cs

using var deflateStream = new DeflateStream(cryptoStream, CompressionLevel);
```

#### 6.コピー

作成したStreamを通してファイルの内容を変換しながらコピーします。
