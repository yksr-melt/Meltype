# 03 構成（Android日英自動判別統合版）

**状態：実装済み（2026-10-25）**

## 対応環境

| 項目 | 値 |
| --- | --- |
| 対応OS | Android 8.0（API 26）以降 |
| 対応ABI | arm64-v8a（実機）、x86_64（エミュレーター） |
| UI・QWERTY配列 | Kotlin、Android View、Gradle |
| 日英自動判別・未確定文字 | 共有C# `src/Meltype.Core/` をAndroid NativeAOTでビルド |
| ネイティブ連携 | Android NDK / JNI（`libMeltypeNative.so`） |
| かな漢字変換 | 端末内のOSS版Mozc |
| アプリID・正式署名 | 本家メンテナーの判断待ち |

## 処理の分岐

```text
KeyboardService (Kotlin IME)
  ├─ 日本語QWERTY → MeltypeCoreHost → JNI → C# Meltype.Core (NativeAOT)
  │                                      └→ Mozcの変換コールバック
  └─ 英字QWERTY・数字記号・変換無効の入力欄 → InputConnectionへの直接入力
       ↓
Android InputConnection / CandidateBarView
```

日本語QWERTYでは **C#判別エンジンをKotlinへ移植しない**。既存の`Meltype.Core`をAndroid用にビルドし、入力・候補をJNI経由で受け渡す。Core障害時は未確定文字を保護して入力を停止し、Android標準のIME選択画面を開ける退避UIを表示する。Kotlinのローマ字変換へはフォールバックしない。

変換・予測の候補と元インデックスは`CoreCandidateChoices`で保持し、`CoreInputAdapter`が`InputConnection`へ未確定文字と確定文字を反映する。12キーフリックは未提供で、設計を[Issue #1885](https://github.com/yksr-melt/Meltype/issues/1885)に分離する。

## モジュール・ディレクトリ

| パス | 役割 |
| --- | --- |
| `android/app/` | IME、候補・設定画面、Kotlinアダプター、C JNI |
| `android/mozc/` | JNIとローカル辞書 |
| `src/Meltype.Core/` | 共有日英判別・未確定文字・変換制御 |
| `android/tools/build-meltype-native.sh` | CoreのAndroid NativeAOTビルド |

## セキュリティ・テスト

IMEはネットワーク権限を要求せず、入力内容を外部送信しない。パスワード等の変換を禁止する入力欄では共有Coreを使用しない。Android 15のInstrumentation 18/18件合格、Pixel 10 Pro Fold（Android 17）実機確認完了。[仕様08](08-auto-detection.md)・[テスト11](11-testing.md)も参照。
