# SignPath のテスト署名の設定

[Code signing policy](CODE-SIGNING.md) に沿って、Windows 版を SignPath でテスト署名するための設定手順です。
テスト署名の成果物は GitHub Releases に添付せず、正式版として配布しません。既存の配布経路 (`release.yml` から呼ぶ `windows.yml` の `package`) は変えません。

Organization ID、Project の表示名と slug、既存 Artifact Configuration の slug は別の値です。
画面で確認した値を使い、表示名 `Meltype` から slug を推測しません。
実行前に、SignPath で証明書の状態 (テスト証明書が `VALID` か) を確かめてください。

## 1. SignPath

1. SignPath ダッシュボードの Organization の情報から **Organization ID** を控えます。
2. Projects → Meltype を開き、**Project slug** を控えます。
3. Trusted build systems の GitHub.com が ACTIVE で、この Project にリンクされていることを確認します。Trusted Build System Required と Origin Verification は有効のままにします。
4. Artifact configurations → Add でテストアプリ用の設定を作ります。名前は `Meltype test application`、新しく作る slug の例は `test-application` です。**これは提案する新規設定名で、既存の実値ではありません。**
5. Customize / XML の編集画面に [test-application.xml](../packaging/signpath/test-application.xml) の内容を入れ、検証して保存します。`version` パラメーターを定義した ZIP の中に 3 個の PE がある設定です。各ファイルの製品名と製品の版を制限します。
6. Signing policies → **test-signing** で、有効な **Test certificate 2026** とこの Artifact Configuration が使えることを確かめます。証明書は `CN=Test certificate for 'Meltype [OSS]'` です。本番の証明書や `release-signing` に置き換えません。
7. Origin Verification のソースリポジトリを `https://github.com/yksr-melt/Meltype`、許可するブランチを `main` に制限します。Pipeline Policy で GitHub-hosted runner の必須化も確認します。
8. Certificates → Test certificate 2026 → 公開証明書のダウンロードから `.cer` を取得します。秘密鍵は不要です。次で **証明書の DER データの SHA-256** を取得します。画面の一般的な Thumbprint (SHA-1 / 40 桁) とは違います。

   ```powershell
   $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path ./test-certificate.cer).Path)
   $cert.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
   ```

9. Users / CI users で、`test-signing` の Submitter に必要な権限だけを持つ CI user の API Token を用意します。テスト用 Token に本番署名の権限を付けません。画面の名称が異なる場合は、Project / Signing Policy の Submitters 設定から対応する CI user を確認します。
10. 承認が必要なポリシーでは、署名リクエストのソース・commit・成果物を確認して SignPath 上で承認します。待ち時間は最大 30 分です。本番署名の手動承認は Foundation の条件なので、自動承認で迂回しません。

SignPath GitHub App を使う場合はこのリポジトリだけにアクセスを限定します。公式ドキュメントでは監査ログの評価に必要です。Origin Verification の代わりにはしません。

## 2. GitHub

Settings → Environments → New environment で **`signpath-test`** を作ります。
Deployment branches and tags は **Selected branches and tags → Branch: `main`** とし、必要なら Required reviewers に署名担当者を指定します。
次の値を、この Environment の Variables / Secrets に登録します。Token をチャットやコミットに貼りません。

| 種類 | 名前 | 入れる値 |
| --- | --- | --- |
| Variable | `SIGNPATH_ORGANIZATION_ID` | SignPath で確認した Organization ID (GUID) |
| Variable | `SIGNPATH_PROJECT_SLUG` | Meltype の Project slug の実値 |
| Variable | `SIGNPATH_TEST_ARTIFACT_CONFIGURATION_SLUG` | 上で作成・検証した Artifact Configuration の slug |
| Variable | `SIGNPATH_TEST_CERTIFICATE_SHA256` | 上で取得した公開テスト証明書の SHA-256、区切りなし 64 桁 |
| Secret | `SIGNPATH_API_TOKEN` | test-signing 専用の Submitter の API Token |

## 3. 実行して確かめる

1. PR の `signpath-test / build` が成功することを確認します。ここでは通常の Windows ビルド・テストだけを行います。SignPath の秘密は使いません。
2. レビュー後にこのワークフローを main に取り込みます。新しい `workflow_dispatch` のワークフローはデフォルトブランチに置く必要があります。PR ブランチから署名させるために制限を緩めません。
3. Actions → **signpath-test** → Run workflow → Branch: **main** を選びます。
4. Environment / SignPath で承認待ちがあれば、対象 commit・Artifact Configuration・ポリシー `test-signing` を確認して承認します。
5. 成功した run から次を取得します。
   - `signpath-test-signed-NOT-FOR-RELEASE-...`: 検証を通ったテスト署名済みファイル。
   - `signpath-test-verification-.../report.json`: 各ファイルの署名前後の SHA-256、証明書の SHA-256・Subject、Windows の検証状態。
6. `verified: true` で、3 ファイルの `windowsStatus` が `Valid`、`certificateSha256` が登録した値であることを確認します。署名の検証が `Valid` 以外なら、理由を問わず失敗します。

この成果物はランタイムを同梱しない署名検証用の publish 出力です。通常版・ポータブル版のリリースパッケージではありません。
このテストではインストーラー・TSF DLL・PowerShell スクリプトへの署名までは確認しません。

秘密は署名 Action の入力にだけ渡します。ビルド・テストとは別の GitHub-hosted runner で署名し、署名ジョブでは成果物のコードを実行しません。
テスト署名の検証中に限り、SHA-256 で固定した自己署名テスト証明書を使い捨て Windows runner の CurrentUser Root に一時登録し、検証終了時に削除します。本番署名では信頼ストアを変更しません。`UnknownError` は許可しません。ファイルの増減、対象外ファイルの変更、署名対象のコードの変更、未署名、証明書違い、暗号署名の不一致で停止します。
検証失敗時は、署名済み成果物を公開する step は動かず、失敗を含む検証レポートだけを保存します。

## 公式資料

- [GitHub 連携・Artifact ID・runner の要件](https://docs.signpath.io/trusted-build-systems/github)
- [Artifact Configuration の XML・ZIP と PE の例](https://docs.signpath.io/artifact-configuration/examples)
- [SignPath Foundation の条件と手動承認](https://signpath.org/terms)
- [WinVerifyTrust のハッシュ検証フラグ](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_data)
