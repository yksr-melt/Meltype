# Android署名・APK配布（個人テスト版）

## 固定する識別子

- 表示名: **Meltype**
- Application ID: `app.meltype.keyboard`（このアプリとして更新する間、変えない）
- GitHub Releasesで配るファイル名: **`Meltype.apk`**
- 署名鍵の別名: `meltype`、形式: PKCS12
- リポジトリの配布ページ: https://github.com/KoheiTakada/Meltype/releases

Androidのコードパッケージや元作者の著作権表示はApplication IDと独立している。

## 最初の1回だけ：GitHub Secretsに鍵を登録する

1. **秘密のバックアップ** `Meltype-signing-backup.zip` を安全な場所（1Password等）に保存する。ZIP内の `.p12` とパスワードは決してGitへコミットしない。**バックアップを失うと同じアプリを更新するための署名ができない。**
2. リポジトリの **Settings > Secrets and variables > Actions > New repository secret** を開く。
3. ZIP内 `Meltype-signing-setup.txt` に記載された次の2つを、それぞれ **Repository secret** として設定する。
   - `MELTYPE_SIGNING_KEYSTORE_BASE64` : .p12のBase64文字列
   - `MELTYPE_SIGNING_PASSWORD` : キーストア・秘密鍵共通パスワード
4. 秘密鍵やパスワードをIssue、PR、Gitのコード、Actionsのログ、Releaseの添付ファイルへ書かない。

署名情報を登録しなくても、通常のQAやデバッグビルドは動く。ただしそれらは**永続的な配布用署名ではない**。

## 更新APKを作る

1. [Publish signed Meltype Android APK](https://github.com/KoheiTakada/Meltype/actions/workflows/android-release.yml) を開く。
2. **Run workflow** を実行。ソースとして `feat/android-bilingual-adaptive-ime` を指定する。
3. CIがソースSHAを記録し、QA確認済みのJNI/Mozcを復元して最新ソースから**署名付きrelease APK**を生成する。ソースのネイティブ部分が基準コミットから変わっていた場合は安全のため停止するので、新しいフルQAに基準を更新する。
4. 署名とApplication IDを検証した後、[Releases](https://github.com/KoheiTakada/Meltype/releases) へ `Meltype.apk` をアップロードする。ビルドごとにバージョンコードが増える。
5. 同じApplication ID・同じ署名鍵・より新しいバージョンコードなら、以後のAPKを上書きインストールできる。

**注**: 初回の `app.meltype.keyboard` 版は、過去の `.bilingualtest` や `.swipepreview` アプリと別アプリ。設定・辞書が自動移行されるわけではない。

## なぜAPKをGitに直接コミットしないか

ビルド生成物をGitの履歴に積むと、更新のたびにバイナリが増えて履歴が肥大化する。Releasesなら同じリポジトリ上でファイル名 `Meltype.apk` のまま配布でき、タグ・ソースSHA・チェックサムを対応付けられる。GitHub ActionsのArtifactsは短期QA用、Releasesは配布用という使い分けにする。

## 鍵を失った場合

元の秘密鍵からの復元ができなければ、Androidは既存アプリへの上書き更新を拒否する。別アプリIDとして新規配布するか、既存アプリをアンインストールする必要がある。鍵は必ずバックアップする。
