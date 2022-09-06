---
title: SkyziBackup
description: 暗号化方式について
layout: default
permalink: encryption
og_image: https://user-images.githubusercontent.com/38061609/188533108-20200a7c-d74d-4277-8a16-f498fd0a7427.png
---


## 目次

- auto-gen TOC:
{:toc}

## ファイルの暗号化・圧縮の流れ

実際のソースコードは[こちら](https://github.com/Skyzi000/SkyziBackup/blob/main/src/Skyzi000/Cryptography/CompressiveAesCryptor.cs)から確認できます。

### 1.ソルトの作成

[RNGCryptoServiceProvider](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.rngcryptoserviceprovider?view=net-5.0)(.NET 6では非推奨になったので対応予定)を利用して暗号強度の高いランダム値を作成し、ソルトとします。

```cs
Salt = GenerateSalt(8);
```

### 2.暗号化キーと初期化ベクトルの生成

ユーザが入力したパスワードと、[1.](#1.ソルトの作成)で生成したソルトをもとに、[Rfc2898DeriveBytes](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.rfc2898derivebytes?view=net-5.0)を利用して暗号化キーと初期化ベクトルを生成します。  
現在は[OpenSSL](https://www.openssl.org/)のデフォルト値と合わせるため、繰り返し回数は10000、ハッシュアルゴリズムはSHA256に固定しています。  

```cs
(key, Iv) = GetKeyAndIV(GenerateKIV(Salt, password, HashAlgorithm, 10000, keySize / 8 + blockSize));
```

なお、このクラスでは[RFC 2898](https://www.ietf.org/rfc/rfc2898.txt)で定義されたPBKDF2のアルゴリズムを使用しています。  
これは、オープンソースのパスワードマネージャとして有名な[Bitwarden](https://bitwarden.com/)でも使われている([参照](https://bitwarden.com/help/article/what-encryption-is-used/#pbkdf2))、一般的かつ強力なアルゴリズムです。

### 3.ソルトの書き込み

暗号化ファイルの先頭にプレフィックス`Salted__`とソルトを書き込みます。  
プレフィックスを書き込んでいるのは[OpenSSL](https://www.openssl.org/)のものと合わせるためです。

```cs
output.Write(Encoding.UTF8.GetBytes(prefix));
output.Write(Salt);
```

### 4.ICryptoTransform・CryptoStreamの作成

暗号化キーと初期化ベクトルを使用して[AesCng](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.aescng)から対称AES暗号化オブジェクト([ICryptoTransform](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.icryptotransform))を作成し、それをもとに[CryptoStream](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.cryptostream?view=net-6.0)を作成します。  
なお、[AesCng](https://docs.microsoft.com/ja-jp/dotnet/api/system.security.cryptography.aescng)は[Cipher Block Chaining (CBC)](https://ja.wikipedia.org/wiki/%E6%9A%97%E5%8F%B7%E5%88%A9%E7%94%A8%E3%83%A2%E3%83%BC%E3%83%89#Cipher_Block_Chaining_(CBC))モードで予め生成しています。

```cs
ICryptoTransform cryptoTransform = aes.CreateEncryptor(key, Iv);
using var cryptoStream = new CryptoStream(output, cryptoTransform, CryptoStreamMode.Write);
```

### 5.DeflateStream/GZipStreamの作成

圧縮するように設定されている場合、この時点で圧縮用のDeflateStream又はGZipStreamを作成します。

```cs

using var deflateStream = new DeflateStream(cryptoStream, CompressionLevel);
```

### 6.コピー

作成したStreamを通してファイルの内容を変換しながらコピーします。
