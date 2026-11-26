# Android署名・APK配布（プレビュー版）

利用者のインストール・更新手順は [Android ガイド](../../docs/ANDROID.md) にあります。このページは公開用 APK を作るメンテナー向けです。

## 固定する識別子

- 表示名: **Meltype**
- Application ID: `app.meltype.keyboard`（このアプリとして更新する間、変えない）
- GitHub Releasesで配るファイル名: **`Meltype.apk`**
- 署名鍵の形式: PKCS12（`.p12`・`.pfx`）。秘密鍵を1つだけ入れる。配布用 Actions は別名を自動で選ぶので、Windows PowerShell で作った鍵も使える
- タグ: `android-v<版>`（例: `android-v0.1.0-preview.1`）
- リポジトリの配布ページ: https://github.com/yksr-melt/Meltype/releases

Androidのコードパッケージや元作者の著作権表示はApplication IDと独立している。

## 最初の1回だけ：GitHub Secretsに鍵を登録する

1. 配布用の `.p12` とパスワードを安全な場所（パスワード管理アプリ等）に保存する。Gitへコミットしない。**バックアップを失うと同じアプリを更新するための署名ができない。**
2. リポジトリの **Settings > Secrets and variables > Actions > New repository secret** を開く。
3. 次の2つを、それぞれ **Repository secret** として設定する。
   - `MELTYPE_SIGNING_KEYSTORE_BASE64` : .p12のBase64文字列
   - `MELTYPE_SIGNING_PASSWORD` : キーストア・秘密鍵共通パスワード
4. 秘密鍵やパスワードをIssue、PR、Gitのコード、Actionsのログ、Releaseの添付ファイルへ書かない。

署名情報を登録しなくても、通常のQAやデバッグビルドは動く。ただしそれらは**永続的な配布用署名ではない**。

## 更新APKを作る

1. [Android preview release](https://github.com/yksr-melt/Meltype/actions/workflows/android-release.yml) を開く。
2. **Run workflow** でブランチを `main` にし、版番号を `0.1.0-preview.1` の形式で指定して実行する。次の公開では `0.1.0-preview.2` のように新しい版を指定する。
3. 固定した Mozc と、実行時のコミットの共有 Core をソースからビルドする。Core・JVM のテストが通ったら、release APK を配布用の鍵で署名する。
4. 署名・Application ID・版番号・16 KiB 対応を確認し、[Releases](https://github.com/yksr-melt/Meltype/releases) に **Pre-release** として公開する。APK・SHA-256・署名証明書の情報・ソースのコミットを添付する。既存のタグがある版は失敗にし、配布物を上書きしない。
5. Android の `versionCode` には、公開するコミットまでの全履歴のコミット数（`git rev-list --count HEAD`）を使う。新しい版は前の公開より進んだ `main` のコミットから作る。同じコミットの再実行では同じ番号になり、ワークフローを改名しても番号は戻らない。同じ Application ID・同じ署名鍵・より大きい `versionCode` の APK は上書きインストールできる。

Gradle は未署名の release APK を作り、配布用 Actions の `apksigner` で署名する。リリースノートには、対応機能・制限を重複して並べず、公開するコミットの Android README へのリンクを載せる。

`main` 以外・署名情報の不足・鍵を開けない場合はビルド前に停止する。秘密鍵は確認と署名のときだけ一時ファイルに戻し、各ステップの終了時に削除する。成果物に秘密鍵を含めない。

**注**: 初回の `app.meltype.keyboard` 版は、過去の `.bilingualtest` や `.swipepreview` アプリと別アプリ。設定・辞書が自動移行されるわけではない。

## なぜAPKをGitに直接コミットしないか

ビルド生成物をGitの履歴に積むと、更新のたびにバイナリが増えて履歴が肥大化する。Releasesなら同じリポジトリ上でファイル名 `Meltype.apk` のまま配布でき、タグ・ソースSHA・チェックサムを対応付けられる。GitHub ActionsのArtifactsは短期QA用、Releasesは配布用という使い分けにする。

## 鍵を失った場合

元の秘密鍵からの復元ができなければ、Androidは既存アプリへの上書き更新を拒否する。別アプリIDとして新規配布するか、既存アプリをアンインストールする必要がある。鍵は必ずバックアップする。
