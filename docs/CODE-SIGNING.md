<a name="code-signing-policy"></a>

# コード署名の方針

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

署名があっても Windows の SmartScreen の警告が必ず消えるわけではありません。

## 現在の署名経路

| 経路 | 実装と対象 |
| --- | --- |
| Windows の通常配布 | [windows.yml](../.github/workflows/windows.yml) が `SIGN_PFX_BASE64`・`SIGN_PASSWORD` があるときに PFX で署名。対象は `Build-Package.ps1` のアプリ・Core・設定アプリ・Mozc ヘルパー・TSF DLL と setup.exe。秘密が無ければ未署名で作成 |
| SignPath のテスト | [signpath-test.yml](../.github/workflows/signpath-test.yml) を main で手動実行。Meltype.exe・Meltype.dll・Meltype.Core.dll の 3 個をテスト署名し、検証した成果物だけを Actions に保存。Releases には添付しない |
| Android | [android-release.yml](../.github/workflows/android-release.yml) で固定した秘密鍵を使って APK を署名。[署名と配布](../android/docs/SIGNING-DISTRIBUTION.md) を参照 |

個別の公開ファイルに署名があるかは、そのファイルの署名を確認してください。この文書は、すべての配布物が SignPath で署名済みだと示すものではありません。

## SignPath の対象

- このリポジトリから GitHub-hosted runner でビルドした Meltype だけを署名します。
- 現在の SignPath テストで署名するのは Meltype 自身のファイル (`Meltype.exe`・`Meltype.dll`・`Meltype.Core.dll`) です。
- 同梱する .NET・Visual C++ のランタイムや、上流の Mozc のバイナリを Meltype の証明書で一括署名しません。

## 役割

- 作者・レビュアー: リポジトリの書き込み権限を持つメンテナー。外部からの変更はメンテナーがレビューします。
- 署名の承認責任者: リポジトリ所有者の [@yksr-melt](https://github.com/yksr-melt)。SignPath に登録する Approver も、この責任に合わせます。
- メンテナーと署名担当者は GitHub・SignPath の両方で多要素認証を使います。

## プライバシー

通信・データの扱いは [SECURITY.md のプライバシーに関する説明](../SECURITY.md) を参照してください。自動更新の確認、使う人が選んだ外部連携などをここで説明しています。

署名の設定手順は [SIGNPATH-SETUP.md](SIGNPATH-SETUP.md) にあります。
