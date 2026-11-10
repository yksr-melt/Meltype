# Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

署名があっても Windows の SmartScreen の警告が必ず消えるわけではありません。

## 対象

- このリポジトリから GitHub-hosted runner でビルドした Meltype だけを署名します。
- 署名するのは Meltype 自身のファイル (`Meltype.exe`・`Meltype.dll`・`Meltype.Core.dll`) です。
- 同梱する .NET・Visual C++ のランタイムや、上流の Mozc のバイナリを Meltype の証明書で一括署名しません。

## 役割

- 作者・レビュアー: リポジトリの書き込み権限を持つメンテナー。外部からの変更はメンテナーがレビューします。
- 署名の承認責任者: リポジトリ所有者の [@yksr-melt](https://github.com/yksr-melt)。SignPath に登録する Approver も、この責任に合わせます。
- メンテナーと署名担当者は GitHub・SignPath の両方で多要素認証を使います。

## プライバシー

通信・データの扱いは [SECURITY.md のプライバシーに関する説明](../SECURITY.md) を参照してください。自動更新の確認、使う人が選んだ外部連携などをここで説明しています。

署名の設定手順は [SIGNPATH-SETUP.md](SIGNPATH-SETUP.md) にあります。
