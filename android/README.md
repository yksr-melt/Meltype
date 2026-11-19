<!-- meltype-flash-platform:start -->
<p align="center"><img src="../assets/branding/readme/logo.svg" alt="Meltype-FLASH" width="520"></p>
<a id="meltype-for-android"></a>
<p align="center"><img src="../assets/branding/readme/android/hero.svg" alt="Meltype-FLASH for Android" width="1000"></p>
<p align="center"><b>稲妻のように、言葉の壁を駆け抜ける日本語入力。</b></p>
<p align="center"><a href="https://github.com/appipinopi/Meltype-Flash/releases/latest"><img src="https://img.shields.io/badge/Flash-v1.1.7-ffe34a" alt="Flash v1.1.7"></a> <img src="https://img.shields.io/badge/status-正式版-666973" alt="正式版"></p>
<p align="center"><a href="#インストール">ダウンロード・導入</a> · <a href="#使い方">使い方</a> · <a href="#困ったとき">困ったとき</a> · <a href="https://github.com/appipinopi/Meltype-Flash/issues/new/choose">不具合の報告・提案</a></p>
<p align="center">
  <a href="../rust/WINDOWS-PACKAGE.md"><img src="../assets/branding/readme/cards/os-windows.svg" alt="Windows" width="32%"></a>
  <a href="../linux/README.md"><img src="../assets/branding/readme/cards/os-linux.svg" alt="Linux" width="32%"></a>
  <a href="../mac/README.md"><img src="../assets/branding/readme/cards/os-mac.svg" alt="Mac" width="32%"></a>
</p>
<p align="center">
  <a href="README.md"><img src="../assets/branding/readme/cards/os-android-active.svg" alt="Android" width="32%"></a>
  <a href="../chromeos/README.md"><img src="../assets/branding/readme/cards/os-chromeos.svg" alt="ChromeOS" width="32%"></a>
  <a href="../ios/README.md"><img src="../assets/branding/readme/cards/os-ios.svg" alt="iOS" width="32%"></a>
</p>
<p align="center"><a href="../README.md">トップのREADME</a></p>
<!-- meltype-flash-platform:end -->

Androidの画面キーボードとして動く、Rust Core + JNIの日本語入力プレビューです。
変換・入力状態・フリック判定・設定画面・背景画像の処理はRustで実装しています。Android本体の手書きJava/Kotlinソースはありません。OSが要求する `InputMethodService` / Activity・リスナーなどの接続クラスは、Rustの [bridge.rs](bridge.rs) からビルド時に生成します。APKにはこの生成された接続クラスが含まれます。
**公開中のdebug APKは開発検証用です。Play Store配布・正式な配布署名・物理端末での入力確認は未対応です。**

<a id="内容"></a>
<h2><img src="../assets/branding/readme/android/headings/df8764ebcf5d.svg" alt="内容" width="1000"></h2>

QWERTYローマ字入力、かな・補助辞書による変換、候補タップ、確定・削除・英数切替、外付けキーボードの入力に対応する実装です。
AIモデルは「使用しない / shunti」から選べます。shuntiを選択すると導入ボタンを表示し、確認に同意した場合だけ辞書・モデルを別ダウンロードします。パス入力は不要です。Zenzは搭載しません。通常の変換とAI変換は端末内で処理し、入力内容を送信しません。
設定画面から、入力設定・ユーザー辞書・定型文・学習削除・プロファイル・バックアップを扱います。
「キーボードの背景・テーマ」から、システム・ライト・ダーク・ブルー・ピンク・ミントの配色と、好きな背景画像を選べます。プレビューで確認し、キーボードを開き直すと反映されます。フリックとQWERTYで共通です。
画像はドキュメント選択画面で選び、長辺1280ピクセル以下へ縮小してアプリ専用領域に保存します。表示は中央基準で切り抜きます。「背景画像を外す」は画像だけを削除し、テーマ・辞書・学習は保持します。背景設定と画像は現在のCoreバックアップには含みません。
公開APKと開発ブランチの機能が同じとは限らないため、試す版のリリース説明も確認してください。

[動く環境](#動く環境) · [インストール](#インストール) · [使い方](#使い方) · [困ったとき](#困ったとき) · [アンインストール](#アンインストール) · [開発者向け](#開発者向け)

<a id="動く環境"></a>
<h2><img src="../assets/branding/readme/android/headings/f378dd1f4672.svg" alt="動く環境" width="1000"></h2>

- Android 8.0（API 26）以降。
- arm64-v8a / x86_64向けの開発用APK。
- キーボードの追加とAPKの導入を許可できる端末。

32bit専用のAndroid端末はこのAPKの対象ではありません。管理端末では組織のポリシーに従ってください。

<a id="インストール"></a>
<h2><img src="../assets/branding/readme/android/headings/4f36b3b314d8.svg" alt="インストール" width="1000"></h2>

1. [最新正式版](https://github.com/appipinopi/Meltype-Flash/releases/latest) から `Meltype-flash-Android-debug.apk` を取得します。
2. 取得したAPKを開き、Androidの案内に従って導入します。必要な場合は、取得に使ったアプリについてインストールを許可します。
3. Androidの「設定 → システム → キーボード → 画面キーボード」などからMeltypeを有効にします。
4. 文字入力欄を開き、キーボードの切替からMeltypeを選びます。

OSやメーカーによって設定名は異なります。画面の名前に `Meltype Rust Preview` が残っている版もあります。
debug署名はCIの生成版ごとに変わる場合があるため、別のAPKへの上書き更新ができないことがあります。
その場合も、必要な辞書・バックアップを保存する前にアプリを削除しないでください。

<a id="使い方"></a>
<h2><img src="../assets/branding/readme/android/headings/936b17951891.svg" alt="使い方" width="1000"></h2>

| ボタン | 操作 |
| --- | --- |
| 文字キー | ローマ字を入力 |
| 変換/空白 | 変換・候補切替。未確定入力がなければ空白 |
| 候補 | タップして確定 |
| 確定/↵ | 未確定入力の確定、または入力欄のEnter動作 |
| ⌫ / 取消 | 削除 / 取消 |
| あ/A | 日本語と直接入力の切替 |
| ← / →、縮める / 伸ばす | 文節選択・境界調整 |
| 戻す / 再変換 | 対応する入力欄で確定Undo / 選択文字の再変換 |
| 🌐 / 設定 | 入力方式の切替 / 管理画面 |

パスワード・数字入力は直接入力を使います。読みを取得できない選択文字は再変換しません。
各アプリのInputConnectionの対応により、再変換・Undo・選択範囲の操作などに差が出る場合があります。

<a id="プライバシー"></a>
<h2><img src="../assets/branding/readme/android/headings/733cbe4ad684.svg" alt="プライバシー" width="1000"></h2>

ネットワーク権限は、AIモデル（shunti）の任意ダウンロードだけに使います。ストレージ全体へのアクセス権限は要求しません。通常の学習・辞書はアプリ専用領域に保存します。
パスワードや学習禁止欄では保存しない方針です。辞書・バックアップの入出力はAndroidのドキュメント選択を使います。
復元・学習の全消去は確認画面で内容を確認してから実行します。
初回起動の同意画面で同意したあと、Aptabaseへ匿名の利用回数（アプリの版・OSの版・起動回数）を送ります。入力内容・入力履歴・辞書・設定値は送りません。この送信は設定から停止できません。

<a id="困ったとき"></a>
<h2><img src="../assets/branding/readme/android/headings/e959b3efbcc9.svg" alt="困ったとき" width="1000"></h2>

<details>
<summary>キーボードが出てこない</summary>

APKを入れるだけでは有効になりません。システムのキーボード一覧で有効化し、入力欄で選び直してください。
画面の名前にRust Previewと表示される版もあります。通常のキーボードに戻せることを確認してから試してください。

</details>

<details>
<summary>候補が少ない・別のアプリで削除や再変換が動かない</summary>

1.1.25以降のAndroid版はMozcの語彙・接続コストを同梱し、入力中の読みをSpaceで漢字に変換できます。辞書の追加インストールは不要です。アプリごとの入力欄実装にも違いがあります。
OS・メーカー・APKの版・アプリ名・入力したキーと期待する結果を [Issues](https://github.com/appipinopi/Meltype-Flash/issues/new/choose) に記載してください。

</details>

<a id="アンインストール"></a>
<h2><img src="../assets/branding/readme/android/headings/e6db153b59cc.svg" alt="アンインストール" width="1000"></h2>

Androidの入力方式を別のキーボードへ切り替えてから、設定のアプリ一覧でMeltypeをアンインストールします。
アプリ専用領域の設定・学習・辞書も失われるため、必要なものを先にバックアップしてください。

<a id="開発者向け"></a>
<h2><img src="../assets/branding/readme/android/headings/98935a8c563d.svg" alt="開発者向け" width="1000"></h2>

Rust 1.88以上、JDK 17、Gradle 8.13、Android SDK platform 35 / build-tools 35.0.0、NDK 27.2.12479018が必要です。

```sh
rustup target add aarch64-linux-android x86_64-linux-android
cargo install cargo-ndk --version 4.1.2 --locked
export ANDROID_NDK_HOME="$ANDROID_HOME/ndk/27.2.12479018"
CARGO_NDK_PLATFORM=26 cargo ndk -t arm64-v8a -t x86_64 -o android/app/src/main/jniLibs build -p meltype-native --release --locked
gradle -p android assembleDebug lintDebug --no-daemon
```

Windowsでは環境変数をPowerShellの `$env:ANDROID_NDK_HOME` / `$env:CARGO_NDK_PLATFORM` で指定します。
GradleはRustコンパイラーでアダプター生成ツールを作り、`android/app/build/generated` 以下へビルド種別ごとにOS接続クラスを出力します。生成されたファイルは編集せず、接続の定義は `android/bridge.rs`・`android/bridge_events.rs`、実際の処理は `rust/native/src/android/` を変更します。RustをPATHから実行できる必要があります。
`ANDROID_NDK_ROOT` / `ANDROID_NDK` を使っている場合も同じNDKに揃えてください。

共通Coreの試験、デスクトップJVMでのJNI試験、Androidフレームワーク上のinstrumentation試験、実機での他アプリ入力は別の検証です。
詳しいJVMコマンド・エミュレーター成果物・実装経緯は [開発・検証記録](DEVELOPMENT.md) を参照してください。

<a id="ライセンス"></a>
<h2><img src="../assets/branding/readme/android/headings/bad2dd3c907b.svg" alt="ライセンス" width="1000"></h2>

[GPL-3.0-or-later](../LICENSE)。辞書などの表示は [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) を参照してください。
