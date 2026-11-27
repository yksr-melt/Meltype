# Meltype for Android — 日英自動判別対応の試作版

Android用の仮想キーボード（IME）。日本語QWERTYでは **日英自動判別が実装済み**。既存のC# `Meltype.Core` を **NativeAOT + JNI** で共有し、Kotlinで判別ロジックを再実装していません。現在提供する配列は QWERTY です。12キーのかな／英字フリック入力は選択できず、体験設計とCore統合を [Issue #1885](https://github.com/yksr-melt/Meltype/issues/1885) で検討します。

Android版は、[ninjin0802さんのAndroid試作PR #621](https://github.com/yksr-melt/Meltype/pull/621) を土台に、[統合PR #1149](https://github.com/yksr-melt/Meltype/pull/1149) で本家に取り込みました。元のAndroid実装への貢献・クレジットを維持しています。開発中のプレビュー版です。

インストールとキーボードの操作は [利用者向けの Android ガイド](../docs/ANDROID.md)、全体の構成は [開発文書](../docs/dev/DEVELOPMENT.md#動作の仕組み) にあります。

## 実装済みの主な機能

- **日英自動判別（日本語QWERTY）**: キーを手動で切り替えずに日本語・英語・混在テキストを入力。判別と未確定文字の処理はC# Coreを共有。
- **ライブ変換・候補バー**: かな漢字変換はローカルのOSS版Mozcを使用。変換候補か予測候補を表示・選択。予測候補のタップで確定し、変換候補のタップでいま選んでいる文節までを確定する（残りの文節は未確定のまま続けて変換でき、最後の文節ならすべて確定）。変換中にスペースを長押しして左右に動かすと、選んでいる文節の区切りが伸び縮みする。
- **入力配列**: 日本語QWERTY、英字QWERTY、2ページの数字・記号配列。日本語QWERTYに `、` `。` の専用キーを残し、`!` `?` も入力可能。
- **配列切り替え**: 日本語／英語の言語モードはCoreが持ち、文字の配列はそれに合わせて日本語QWERTY／英字QWERTYになる。あaキーで切り替え、スペースを長押しして吹き出しを上下スワイプして指を離すと選択。メールアドレス・数字の欄では、パスワード欄と同じくCoreが受け持たず、打ったとおりに入る。12キー・フリックは未実装。
- **画面適応**: 画面の論理幅・高さ・アスペクト比でキー寸法と分割配列を決定。中央テンキーは幅と高さに十分な余裕がある画面だけ表示。縦横回転と折りたたみ端末に対応。
- **操作・設定**: 触覚フィードバック、絵文字パネル、候補の選択、全角・半角スペース、ライブ変換のオン／オフ。設定画面はシステムバー余白と戻る操作に対応。
- **ユーザー辞書**: 設定の「ユーザー辞書」で、登録した単語の一覧を見て、読みと単語を登録・編集・削除する。Coreのユーザー辞書（`userdict.txt`、Windows・Mac・Linuxと同じ形式）を読み書きし、キーボードを開き直さなくても変換に使われる。
- **障害時の退避**: Coreの初期化・応答処理に失敗した場合は未確定文字を保護し、Android標準のIME選択画面を直接開ける最小限の退避UIを表示する。通常のキーボードには独自のIME切り替えボタンを設けない。IMEサービス自体が起動できない場合は、OSが提供するIME切り替え機能を使用する。

## 対応範囲と未対応機能

| 対象 | 状態 |
| --- | --- |
| 日本語QWERTYでの日英自動判別 | **実装済み**。C# `Meltype.Core` を使用 |
| かな／英字12キーフリック | **未提供**。選択UIを撤去し、[Issue #1885](https://github.com/yksr-melt/Meltype/issues/1885) で体験設計・Core統合を検討 |
| 読みから英単語を直接推薦する独立機能（`ぐーぐる` → `Google` 等） | 未対応。QWERTYの日英自動判別とは別機能 |
| 音声入力、カスタムテーマ | 未対応 |
| 全端末・全アプリでの互換性、TalkBack対応 | 未検証／保証なし |

## プライバシーと制約

- ネットワーク権限なし。端末の入力内容を送信する機能なし。
- パスワード欄では、Coreが入力を受け持たず、打った文字をそのまま入れる（学習しない）。
- 学習データはアプリ専用の領域で扱い、通常のログに入力内容を含めない。
- 触覚フィードバック用に `VIBRATE` 権限を要求する（「権限が一つもない」わけではない）。

## 対応環境

- 最低Android 8.0（API 26）、Android用ABIは `arm64-v8a`・`x86_64`。

## ビルド

必要: JDK 17、Android SDK/NDK、.NET SDK 10、Python 3、Git、Bazelisk（Mozc用）。ABIごとのC# NativeAOT共有ライブラリを先にビルドします。

```sh
cd android
bash tools/build-mozc.sh
ANDROID_NDK_HOME="$ANDROID_HOME/ndk/28.2.13676358" bash tools/build-meltype-native.sh
./gradlew :app:testDebugUnitTest :app:assembleDebug
```

Android APK: `android/app/build/outputs/apk/debug/app-debug.apk`。

MozcのJNI／辞書成果物やC# NativeAOTの生成物、配布署名鍵はソース管理しません。配布用APKは、下の「APK配布と署名」にあるActionsで生成します。

## リポジトリ構成

| パス | 役割 |
| --- | --- |
| `android/app/` | Android IME・画面・設定・候補バー・C JNIブリッジ |
| `android/mozc/` | ローカルMozcのJNIと辞書 |
| `src/Meltype.Core/` | Windows等と共有するC#日英判別・未確定文字の処理 |
| `android/tools/build-meltype-native.sh` | C# NativeAOTのAndroid向けビルド |

本家の既存ライセンス（GPL-3.0-or-later）と同梱物の著作権表示を維持します。統合時の実装・テスト・文書の作成には ChatGPT による AI 支援を使用しています。

## APK配布と署名

- 配布用Application ID: `app.meltype.keyboard`（ユーザー名・元実装者名は含めない）
- APK名: `Meltype.apk`
- 公開先: [GitHub Releases の Android プレビュー](https://github.com/yksr-melt/Meltype/releases)。[Android preview release](https://github.com/yksr-melt/Meltype/actions/workflows/android-release.yml) を `main` で手動実行して公開する。
- 更新時の互換性: **同じApplication ID、同じ秘密署名鍵、増加するversionCode** を維持する。毎回変わるCIデバッグ鍵は配布には使わない。
- 設定・操作方法: [署名と配布手順](docs/SIGNING-DISTRIBUTION.md)。署名鍵はGitHub Secretsに登録し、Git履歴へは置かない。
- 一時検証用のデバッグAPKは `.debug` を付けた別パッケージIDで配布版と分離する。
