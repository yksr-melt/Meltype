# Meltype for Android（試作版）

Android の仮想キーボード（IME）です。Kotlin で書いた独立した Gradle プロジェクトで、Windows 版・Mac 版・Linux 版のコードには手を入れていません。

> **試作版です。** Meltype の中心の機能である「日本語か英語かの自動判別」は、**まだ入っていません**。いま入っているのは、その土台になる日本語キーボード（フリック入力、ローマ字入力、ライブ変換）です。自動判別は、`Meltype.Core` の判別部分を Kotlin に移植して、次の段階で載せる計画です（[仕様 08](docs/spec/08-auto-detection.html)）。

## できること

- **12キーのフリック入力**: かなを打つと、変換キーを押さなくても打つそばから漢字になる（ライブ変換）。変換は端末の中の OSS 版 Mozc で行う
- **QWERTY のローマ字入力**: `kyouha` → `きょうは` → `今日は`。設定で、日本語を 12キーで打つか QWERTY で打つかを選べる
- **英字**: QWERTY と、12キーのフリック（`abc` `def` …）を設定で選べる
- **絵文字の画面**: 種類ごとの一覧、最近使ったもの
- 候補バー、空白キーでの候補選び、トグル入力（設定でオン・オフと待ち時間）、空白キーの長押しでカーソル移動、数字・記号の配列
- ライト/ダーク、横画面、ジェスチャーナビゲーション

## まだできないこと

- 日本語と英語の自動判別（上のとおり）
- 読みから英字を出す変換（`ぐーぐる` → `Google`。[仕様 07](docs/spec/07-kana-to-english.html)）
- ユーザー辞書の編集、テーマの変更、音声入力
- TalkBack での操作（キーに名前は持たせてあるが、読み上げにはまだ渡していない）
- 予測候補は、Mozc を既定の設定で呼んでいるため少ない

## プライバシー

- 権限を1つも要求しない。インターネットの権限もないので、打った内容は端末の外に出ない
- パスワードの欄では、変換も学習もしない。打った文字を未確定のまま持たない
- 学習データと絵文字の履歴は、アプリ専用の領域にだけ置く。クラウドへのバックアップと、端末から端末への引き継ぎの対象から外してある
- 打った文字、読み、候補を、ログに出さない

くわしくは [仕様 09](docs/spec/09-privacy-security.html)。

## 動く環境

- Android 8.0（API 26）以降
- arm64-v8a（実機）、x86_64（エミュレーター）

確かめた端末は、Pixel 9a（arm64）と、エミュレーター（API 35 と API 26、x86_64）です。最初の段階（フリックとライブ変換）について、確かめた内容と確かめられていない内容を [docs/verification/](docs/verification/2026-10-13-stage1.html) に書いてあります。そのあとに足した機能（Gboard 風の配列、絵文字の画面、配列の選択とローマ字入力）は、単体テストと、エミュレーターでの操作で確かめています。API 28〜34 の端末では確かめていません。

## ビルド

必要なもの: JDK 17、Android SDK（`ANDROID_HOME`）、Python 3、Git。Mozc のビルドには Bazelisk を使います（スクリプトが、固定した版を取得して SHA-256 を確かめます）。

```sh
cd android

# 1. Mozc（変換エンジン本体と辞書）をビルドして、mozc/ の下に置く。初回は 30〜40 分かかる
bash tools/build-mozc.sh

# 2. アプリをビルドする
./gradlew :app:assembleDebug

# 3. 端末を1台だけつないで入れる（端末を指定する）
adb -s <端末> install -r app/build/outputs/apk/debug/app-debug.apk
```

入れたあと、アプリ「Meltype」を開くと、キーボードを有効にして選ぶ手順が出ます。

- Mozc の版は `mozc/MOZC_COMMIT` に固定してあります（Windows 版の `native/mozc/MOZC_COMMIT` とは別の版です。Android 用の JNI が動くことを確かめた版にしてあります）。
- Mozc の成果物（`libmozc.so`、`mozc.data`）は Git に入れていません。ない状態でビルドすると、上のスクリプトを先に実行するようメッセージを出して止まります。
- `release` のビルドは、手元で確かめるためにデバッグ用の鍵で署名しています。**配布には使えません。**

## テスト

```sh
./gradlew :engine:test :app:testDebugUnitTest          # PC の上で動くテスト
ANDROID_SERIAL=<端末> ./gradlew :mozc:connectedDebugAndroidTest :app:connectedDebugAndroidTest   # 端末の上で動くテスト
```

## 構成

| モジュール | 役割 |
|---|---|
| `engine` | 入力の頭脳。読みと未確定文字の管理、ライブ変換の制御、ローマ字からかなへの組み立て。Android に依存しない純粋な Kotlin |
| `mozc` | OSS 版 Mozc を JNI で呼ぶ層 |
| `app` | キーボード本体（`InputMethodService`）、キーの描画、絵文字の画面、導入と設定の画面 |

仕様は [docs/spec/](docs/spec/index.html) にあります（HTML。ここが仕様の唯一の正で、仕様を先に直してからコードを直す決まりにしています）。

## ライセンスと同梱物

GPL-3.0-or-later。同梱しているもの（Mozc とその辞書、Protocol Buffers、Kotlin の標準ライブラリ、Unicode の絵文字の一覧）とそのライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にあります。アプリの「ライセンス」の画面からも見られます。

キーの絵（削除、確定、絵文字など）は、座標を書いて描いた自前のものです。ほかの製品の画像は使っていません。

## AI の利用

このフォルダーのコード・テスト・仕様書は、ほぼすべて Claude Code（Anthropic）が書きました（コミットの `Co-Authored-By` に書いています）。
