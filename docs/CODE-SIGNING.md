# Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

現在は **テスト署名の導入段階** です。本番の `release-signing` は有効にしていません。
テスト署名の成果物を GitHub Releases に添付したり、正式版として配布したりしません。
署名があっても Windows の SmartScreen の警告が必ず消えるわけではありません。

## 対象と役割

- このリポジトリから GitHub-hosted runner でビルドした Meltype だけを署名します。
- テストでは `Meltype.exe`・`Meltype.dll`・`Meltype.Core.dll` の 3 ファイルを署名します。MSI / MSIX は使っていません。
- 同梱する .NET・Visual C++ のランタイムや、上流の Mozc のバイナリを Meltype の証明書で一括署名しません。
- 作者・レビュアー: リポジトリの書き込み権限を持つメンテナー。外部からの変更はメンテナーがレビューします。
- 署名の承認責任者: リポジトリ所有者の [@yksr-melt](https://github.com/yksr-melt)。SignPath に登録する Approver も、この責任に合わせます。
- メンテナーと署名担当者は GitHub・SignPath の両方で多要素認証を使います。
- 通信・データの扱いは [SECURITY.md のプライバシーに関する説明](../SECURITY.md) を参照してください。自動更新の確認、使う人が選んだ外部連携などをここで説明しています。

## 今のビルドと配布

調査時点の main: `16154284926ca0510ffebc7f9ab24a203ab7168d`。

| 場所 | 現在の動き |
| --- | --- |
| `build.yml` | Windows の .NET 10 ビルド・テスト、Linux / macOS の共通テスト、TSF の C++ DLL、インストーラーの台本、Mozc のビルド |
| `Build-Package.ps1` | .NET ランタイム同梱・自己診断・通常版 ZIP・ポータブル版 ZIP を作る |
| `packaging/Meltype.iss` | 通常版から Inno Setup の `Meltype-<版>-setup.exe` を作る |
| `build.yml` の `package` | タグ `v*` と手動実行でパッケージを作る。Defender の検査後に Artifacts へ保存し、タグでは `softprops/action-gh-release` で添付する |
| `mac.yml` / `linux.yml` | 同じタグの Release に、各 OS の成果物を添付する |
| `bot.yml` と `meltype-bot.mjs` | `/release`・`/pre-release` でタグとリリースの下書きを作り、各ビルドを始める |
| `winget.yml` | リリース公開時の後続処理。テスト署名からは呼ばない |
| `quality.yml` / `codeql.yml` / `pr-checks.yml` | 形式・静的解析・依存関係・品質の比較・辞書などを検査する |
| main の ruleset | PR のレビュー 1 件、Windows の test・Linux / macOS の core・CLA・精度の比較、force push の禁止 |

既存の `SIGN_PFX_BASE64` / `SIGN_PASSWORD` は任意です。未設定なら、既存の配布経路は未署名でも進みます。
**今回のテスト導入はその経路を変更しません。本番署名の必須化はまだ完了していません。**

## テスト署名の設定

Organization ID、Project の表示名と slug、既存 Artifact Configuration の slug は別の値です。
画面で確認した値を使い、表示名 `Meltype` から slug を推測しません。
2026-10-29 に共有された状態では本番証明書は `CSR PENDING`、テスト証明書は `VALID` です。
実行前にも SignPath で最新の状態を確かめてください。

### 1. SignPath

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

### 2. GitHub

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

### 3. 実行して確かめる

1. PR の `signpath-test / build` が成功することを確認します。ここでは自己署名の一時テスト証明書を使った検証テストと、通常の Windows ビルド・テストだけを行います。SignPath の秘密は使いません。
2. レビュー後にこのワークフローを main に取り込みます。新しい `workflow_dispatch` のワークフローはデフォルトブランチに置く必要があります。PR ブランチから署名させるために制限を緩めません。
3. Actions → **signpath-test** → Run workflow → Branch: **main** を選びます。
4. Environment / SignPath で承認待ちがあれば、対象 commit・Artifact Configuration・ポリシー `test-signing` を確認して承認します。
5. 成功した run から次を取得します。
   - `signpath-test-signed-NOT-FOR-RELEASE-...`: 検証を通ったテスト署名済みファイル。
   - `signpath-test-verification-.../report.json`: 各ファイルの署名前後の SHA-256、証明書 SHA-256・Subject、CMS の暗号検証、Authenticode のハッシュ検証、Windows の検証状態、チェーンの信頼結果。
6. `verified: true` と 3 ファイルの `cmsSignatureValid`・`authenticodeHashValid`・`originalContentPreserved` が true であることを確認します。`chainTrusted: false` / `UntrustedRoot` はテスト証明書では起こり得ます。信頼チェーンの成功として扱いません。失効検証はテスト専用に `NoCheck` と記録しています。

この成果物はランタイムを同梱しない署名検証用の publish 出力です。通常版・ポータブル版のリリースパッケージではありません。
このテストではインストーラー・TSF DLL・PowerShell スクリプトへの署名までは確認しません。

秘密は署名 Action の入力にだけ渡します。ビルド・テストとは別の GitHub-hosted runner で署名し、署名ジョブでは成果物のコードを実行しません。
信頼ストアへテスト証明書を入れません。ファイルの増減、対象外ファイルの変更、署名対象のコードの変更、未署名、証明書違い、暗号署名の不一致で停止します。
検証失敗時は、署名済み成果物を公開する step は動かず、失敗を含む検証レポートだけを保存します。

## 本番証明書が VALID になった後

本番への切り替えは別の PR とし、SignPath の証明書が VALID、`release-signing` が有効になったことを実画面で確認してから進めます。
今回のワークフローにポリシー切り替え入力はありません。

1. `build.yml` の既存の Windows 配布経路を置き換えます。同じタグのファイルを公開する 2 本目の経路は作りません。
2. 通常版をビルドして自己診断・既存テストを通した後、自前の PE だけを署名し、本番証明書を照合して Windows の検証が `Valid` であることを必須にします。タイムスタンプと失効状態も検証します。テスト用の未信頼許容や `NoCheck` は使いません。
3. TSF DLL は現在、アプリとは独立した `FileVersion` / `ProductVersion=1.0.0` を持ちます。FileVersion と更新検出の意味を保ちつつ、製品の版を揃える方法を先に設計します。署名後に `MeltypeTip.dll.package.sha256` を再計算し、署名前の同一性用 `.sha256` と区別します。
4. 署名を検証した app から通常版 ZIP・ポータブル版 ZIP・Inno Setup の EXE を作ります。ZIP の中の自前ファイルと、インストーラー自体を別々に検証します。setup.exe の製品名・版も Artifact Configuration で制限します。
5. `release-signing` によるインストーラー署名も完了した後に Defender 検査・最終ファイルの SHA-256 を生成します。署名後に中身を書き換えたり、再ビルドしたりしません。
6. 公開専用ジョブだけに `contents: write` を与え、検証済み Artifact の ID を明示して取得します。署名・検証・スキャン・必須テストをすべて依存先にし、失敗・キャンセル・スキップ時には未署名版へフォールバックしません。
7. SignPath 側でも対象リポジトリ・保護された main 由来の正式タグ・GitHub-hosted runner に制限し、別の `signpath-release` Environment と本番専用 Token を使います。PR・fork・任意の ref を署名する入力は作りません。Foundation のリリースごとの承認を残します。
8. `/release` が作る下書きと Windows・Mac・Linux の添付の順序、`winget.yml` の起動タイミングを確認します。署名中に他の OS の処理が公開してしまうことがないよう、最終公開をひとつのゲートにまとめます。
9. README と実際のダウンロード / リリースページに、この **Code signing policy** へのリンクを載せます。本番で署名したことを確認してから、導入済みと案内します。

正式リリースの作成・公開には、リポジトリ所有者の明示的な承認が必要です。

## 公式資料

- [GitHub 連携・Artifact ID・runner の要件](https://docs.signpath.io/trusted-build-systems/github)
- [Artifact Configuration の XML・ZIP と PE の例](https://docs.signpath.io/artifact-configuration/examples)
- [SignPath Foundation の条件と手動承認](https://signpath.org/terms)
- [WinVerifyTrust のハッシュ検証フラグ](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_data)
