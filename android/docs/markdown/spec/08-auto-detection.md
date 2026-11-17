# 08 日英自動判別（Android統合試作版）

**状態：QWERTYローマ字配列で実装済み・エミュレーター回帰試験済み。**

HTML正本の[08仕様](../../spec/08-auto-detection.html)と同期した実装後の仕様です。旧来の「Kotlin移植予定」の設計は採用していません。

## 目的

手動で英数モードを切り替えず、同じQWERTYローマ字配列から日本語・英語・日英混在を入力する。判断を誤っても未確定の候補から訂正できること。

## 構成

- **判別器:** デスクトップ版と同じC# `Meltype.Core` の入力の仕組み（`ImeHost`）。NativeAOT（linux-bionic）で `libMeltypeNative.so` を生成し、C JNIブリッジ（`meltype_ime_create` / `meltype_ime_handle` / `meltype_ime_destroy`）とKotlinの `MeltypeCoreHost` 経由で呼び出す。キーボード全体で1つの `ImeHost` を持つ。
- **やり取り:** Android は入力欄の事実とキーを出来事（`ImeEvent` のJSON）で送り、返ってきた指示（`ImeOutput` のJSON）をそのまま入力欄に適用する。JSON を作る・読むのは `core/ImeJson.kt` だけ。文字のキーは文字だけを送り、仮想キーコードは Core が決める。確定は `removeBefore` の長さだけキャレットの前を消して入れる。入力欄の事実は、アプリのパッケージ名（`field.app`）、キャレットの前 4000 字・後ろ 20 字（Mac・Linux と同じ。`field.before` には変換中の文字を含めない）、選択している文字。自分の文字を持たない入力欄（`TYPE_NULL`）では前後の文字を送らず、`canRemove` を false にする。
- **キャレットの移動:** キーボード自身が Core のために書き換えた分（確定・変換中の文字・Core が通したキー）では、キャレットが動いたことにしない（`onUpdateSelection` は遅れて届き、まとめて届くこともあるので、書き換えで動くはずの位置を覚えて照らし合わせる）。それ以外の移動（タップ、矢印キー、アプリの書き換え）は `CaretMoved` で伝え、Core は前の語を確定し直さない。変換中なら、出ている文字のまま確定して `CommittedByApp` を送る。入力欄を離れるときも、出ている文字のまま確定する。
- **候補表示:** Androidの `CandidateBarView` を再利用する。Coreの一覧は1つだけ（変換中は変換の候補、入力中は予測変換か入力中の変換の候補）で、それだけを表示する。Coreが候補を返さないあいだは何も出さない。
- **候補選択:** Coreの一覧の番号を保持し、タップは `CandidateClicked` で送る。予測変換の候補はそれで確定し、変換の候補は選んだあと Enter で確定する。
- **プライバシー:** Android IMEのネットワークアクセスは要求しない。入力欄の事実だけを Core に伝える（パスワード欄は `Password`、`IME_FLAG_NO_PERSONALIZED_LEARNING` は `Private`）。学習するかは Core がデスクトップ版と同じ規則で決める。
- **適用範囲:** 共有Coreによる日英自動判別は日本語QWERTYのみ。英字QWERTY・数字記号と変換無効の入力欄は文字を直接入力する。12キー・フリックは未提供（[Issue #1885](https://github.com/yksr-melt/Meltype/issues/1885)）。

## 設定

| 設定 | 選択肢 | 初期値 |
| --- | --- | --- |
| 日本語入力時のスペース | 全角／半角 | 半角 |
| 絵文字 | オン／オフ | オフ |
| ライブ変換 | オン／オフ | オン |

オフにした絵文字は候補から除外し、絵文字キーとパネルを非表示にする。ライブ変換は Core の設定で、ほかの OS と同じ `config.json`（アプリ専用の領域の `meltype/config.json`）に書く。Core は変わった `config.json` を読み直すので、キーボードを閉じなくても次のキーから効く。そのほかの設定はアプリ専用のSharedPreferencesに永続化する。

## 検証

- 日本語 `arigatou`、`konnichiwa`、英語 `hello`、`meeting`、混在 `hello arigatou` の入力をAndroidで検証。
- Coreの一覧の表示と番号、予測候補のクリックでの確定、Coreが候補を返さないときに何も出さないこと、絵文字の候補フィルタ、応答の適用（`removeBefore`・`moveCaretBack`・壊れた応答）、キャレットの移動と入力欄の出入り、`config.json` への書き込みと次のキーからの反映、設定の保存をテスト（[QA-TESTING](../../QA-TESTING.md)）。
- 2026-10-25のQAビルドではAndroid 15エミュレーターのInstrumentation **18件すべてが成功**。Pixel 10 Pro Fold（Android 17）で実機確認済み。[CI実行結果](https://github.com/KoheiTakada/Meltypetest/actions/runs/40163689812)。この確認は全端末・全アプリの互換性を保証しない。

## 制限と保留

- QWERTY以外にCoreの自動判別を広げることは今回の対象外。
- 既存のスペル補正エンジンの大型変更は本家のPR・Issueに任せ、重複実装しない。
- 本家の正式なAndroidアプリID、正式署名鍵、リポジトリ配置はメンテナー判断待ち。
- Android版Mozcは動作確認済みのコミットを維持。デスクトップ版と統一するにはAndroid JNIの再ビルドと実機検証が必要。
- エミュレーターの成功は全機種、TalkBack、全入力先アプリへの互換性を保証しない。
