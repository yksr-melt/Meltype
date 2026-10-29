# 08 日英自動判別（Android統合試作版）

**状態：QWERTYローマ字配列で実装済み・エミュレーター回帰試験済み。**

HTML正本の[08仕様](../../spec/08-auto-detection.html)と同期した実装後の仕様です。旧来の「Kotlin移植予定」の設計は採用していません。

## 目的

手動で英数モードを切り替えず、同じQWERTYローマ字配列から日本語・英語・日英混在を入力する。判断を誤っても未確定の候補から訂正できること。

## 構成

- **判別器:** デスクトップ版と同じC# `Meltype.Core`。NativeAOT（linux-bionic）で `libMeltypeNative.so` を生成し、C JNIブリッジとKotlinの `MeltypeCoreSession` 経由で呼び出す。
- **候補表示:** Androidの `CandidateBarView` を再利用する。Coreの通常候補 `candidates`、予測候補 `predictions`、原文 `raw`、全かな読み `kana` を組み合わせる。候補の重複は表示しない。
- **候補選択:** ネイティブの元インデックスを保持する。予測候補は予測選択APIへ、漢字変換候補は通常変換APIへ渡す。英字・かな・カタカナの訂正候補は既存CoreのF6/F7/F10相当の選択後に確定する。
- **プライバシー:** Android IMEのネットワークアクセスは要求しない。パスワード欄は変換・学習を停止する。
- **適用範囲:** 日本語QWERTY（ローマ字）配列のみ。他のキー配列は既存Kotlinエンジンを継続して使用する。

## 設定

| 設定 | 選択肢 | 初期値 |
| --- | --- | --- |
| 日本語入力時のスペース | 全角／半角 | 全角 |
| 絵文字 | オン／オフ | オン |

オフにした絵文字は候補から除外し、絵文字キーとパネルを非表示にする。設定値はアプリ専用のSharedPreferencesに永続化する。

## 検証

- 日本語 `arigatou`、`konnichiwa`、英語 `hello`、`meeting`、混在 `hello arigatou` の入力をAndroidで検証。
- 予測候補の表示、ネイティブ予測候補選択、英字・かな訂正候補、絵文字の候補フィルタ、設定の保存をテスト。
- 2026-10-25のQAビルドではAndroid 15エミュレーターのInstrumentation **18件すべてが成功**。Pixel 10 Pro Fold（Android 17）で実機確認済み。[CI実行結果](https://github.com/KoheiTakada/Meltypetest/actions/runs/40163689812)。この確認は全端末・全アプリの互換性を保証しない。

## 制限と保留

- QWERTY以外にCoreの自動判別を広げることは今回の対象外。
- 既存のスペル補正エンジンの大型変更は本家のPR・Issueに任せ、重複実装しない。
- 本家の正式なAndroidアプリID、正式署名鍵、リポジトリ配置はメンテナー判断待ち。
- Android版Mozcは動作確認済みのコミットを維持。デスクトップ版と統一するにはAndroid JNIの再ビルドと実機検証が必要。
- エミュレーターの成功は全機種、TalkBack、全入力先アプリへの互換性を保証しない。
