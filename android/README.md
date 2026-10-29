# Meltype for Android — 日英自動判別対応の試作版

Android用の仮想キーボード（IME）。日本語QWERTYでは **日英自動判別が実装済み**。既存のC# `Meltype.Core` を **NativeAOT + JNI** で共有し、Kotlinで判別ロジックを再実装していません。12キーのかな／英字フリック入力は既存のKotlinエンジンを継続使用します。

このブランチは、[ninjin0802さんのAndroid試作PR #621](https://github.com/yksr-melt/Meltype/pull/621) を土台にした [本家向け統合PR #1149](https://github.com/yksr-melt/Meltype/pull/1149) の提出ソースです。元のAndroid実装への貢献・クレジットを維持し、レビューの一本化を提案しています。正式リリースではなく、実機検証済みの開発・QA版です。

## 実装済みの主な機能

- **日英自動判別（日本語QWERTY）**: キーを手動で切り替えずに日本語・英語・混在テキストを入力。判別と未確定文字の処理はC# Coreを共有。判定ミスを英字・かな・カタカナの訂正候補で修正可能。
- **ライブ変換・候補バー**: かな漢字変換はローカルのOSS版Mozcを使用。通常の変換候補、予測候補、訂正候補を表示・選択。
- **入力配列**: 日本語QWERTY、かな12キーフリック、英字QWERTY、英字12キーフリック、2ページの数字・記号配列。日本語QWERTYに `、` `。` の専用キーを残し、`!` `?` も入力可能。
- **配列切り替え**: QWERTYではスペース長押しで言語を選択。12キーの入力方式は引き続き独立。
- **画面適応**: 画面の論理幅・高さ・アスペクト比でキー寸法と分割配列を決定。中央テンキーは幅と高さに十分な余裕がある画面だけ表示。縦横回転と折りたたみ端末に対応。
- **操作・設定**: 触覚フィードバック、絵文字パネル、候補の選択、全角・半角スペース、ライブ変換のオン／オフ。設定画面はシステムバー余白と戻る操作に対応。

## 対応範囲と未対応機能

| 対象 | 状態 |
| --- | --- |
| 日本語QWERTYでの日英自動判別 | **実装済み**。C# `Meltype.Core` を使用 |
| かな／英字12キーフリック | 実装済み。Kotlinの既存エンジンを使用 |
| フリック配列での日英自動判別 | 未対応（今回の対象外） |
| 読みから英単語を直接推薦する独立機能（`ぐーぐる` → `Google` 等） | 未対応。QWERTYの日英自動判別とは別機能 |
| ユーザー辞書編集、音声入力、カスタムテーマ | 未対応 |
| 全端末・全アプリでの互換性、TalkBack対応 | 未検証／保証なし |

**再現可能なQA手順（入力・期待値・18件のテストコード・実機チェックリスト）：[docs/QA-TESTING.md](docs/QA-TESTING.md)**。CI定義は [android-qa.yml](../.github/workflows/android-qa.yml)。

詳しくは [08 日英自動判別](docs/spec/08-auto-detection.html)、[03 構成](docs/spec/03-architecture.html)、[11 テスト](docs/spec/11-testing.html)。これらのHTML仕様書を規範とし、`docs/markdown/spec/` に対応するレビュー用Markdownを置きます。

## プライバシーと制約

- ネットワーク権限なし。端末の入力内容を送信する機能なし。
- パスワード欄など、変換・学習を抑止すべき入力欄では、日英自動判別Coreを使用しない。
- 学習データはアプリ専用の領域で扱い、通常のログに入力内容を含めない。
- 触覚フィードバック用に `VIBRATE` 権限を要求する（「権限が一つもない」わけではない）。

## 対応環境と検証

- 最低Android 8.0（API 26）、Android用ABIは `arm64-v8a`・`x86_64`。
- **本家向け統合コミット `965dbf3`** で Android 15（API 35）のInstrumentation **18/18件合格**、JVM単体 **444/444件合格**。[CI結果・各テストのJUnit XMLとログ](https://github.com/KoheiTakada/Meltypetest/actions/runs/40182600362)。Mozcを固定ソースから再ビルドする[独立したAndroid CI](https://github.com/KoheiTakada/Meltype/actions/runs/40182575249)も成功。テスト定義と再現手順は [QA-TESTING.md](docs/QA-TESTING.md) に固定。
- **Pixel 10 Pro Fold／Android 17** で2026-10-25に**統合版APK（ソース `965dbf3`）の実機確認完了との本人報告**。手動QA M01〜M08の個別ログ・実測値やスクリーンショットは未収集。再現する場合は[手動テスト手順](docs/QA-TESTING.md)を使用。
- 旧開発ソース `7a44ba5` の[過去のQA](https://github.com/KoheiTakada/Meltypetest/actions/runs/40163689812)は参考履歴。最新統合版の合否は上記 `965dbf3` のCIと実機確認に基づく。
- [2026-10-25の検証記録](docs/verification/2026-10-25-bilingual.html) が現行の日英自動判別と実機確認の証跡です。`docs/verification/2026-10-13-stage1.html` は**当時の第1段階のみの履歴記録**であり、最新QAの結果ではありません。

## ビルド

必要: JDK 17、Android SDK/NDK、.NET SDK 10、Python 3、Git、Bazelisk（Mozc用）。ABIごとのC# NativeAOT共有ライブラリを先にビルドします。

```sh
cd android
bash tools/build-mozc.sh
ANDROID_NDK_HOME="$ANDROID_HOME/ndk/28.2.13676358" bash tools/build-meltype-native.sh
./gradlew :engine:test :app:testDebugUnitTest :app:assembleDebug
```

Android APK: `android/app/build/outputs/apk/debug/app-debug.apk`。端末テストには別途Androidのテスト用APKとエミュレーター／実機が必要です。

MozcのJNI／辞書成果物やC# NativeAOTの生成物、配布署名鍵はソース管理しません。正式なアプリID・リリース署名・配布方針は本家メンテナー判断待ち。デバッグ／QA署名済みAPKを公式リリースと扱わないでください。

## リポジトリ構成

| パス | 役割 |
| --- | --- |
| `android/app/` | Android IME・画面・設定・候補バー・C JNIブリッジ |
| `android/engine/` | フリックと従来入力用のKotlinエンジン |
| `android/mozc/` | ローカルMozcのJNIと辞書 |
| `src/Meltype.Core/` | Windows等と共有するC#日英判別・未確定文字の処理 |
| `android/tools/build-meltype-native.sh` | C# NativeAOTのAndroid向けビルド |

本家の既存ライセンス（GPL-3.0-or-later）と同梱物の著作権表示を維持します。今回の実装・テスト・文書の編集ではChatGPTによるAI支援を使用しています。
