<!-- Automatically converted from spec/06-conversion-mozc.html. Check upstream edits before submitting. -->

# 06 変換（Mozc）

確定

## 変換の窓口（変換ヘルパーの要求）

共有Coreは、ほかの OS と同じく `MeltypeRuntime.CreateConverter` で変換エンジン（`HybridConverter` と `MozcConverter`）を作る。`MozcConverter` は `meltype_mozc_helper` と同じ1行の要求を送り、Android は JNI のコールバック1つ（`CoreMozcCallbacks`）で、アプリのプロセスの Mozc を使って答える。

| 要求 | 渡すもの | 返るもの |
| --- | --- | --- |
| `C`（変換） | 文脈と読み（ひらがな） | 文節の並び。各文節は、対応する読みと、候補の一覧（第一候補が先頭）を持つ。変換できない読みは空行 |
| `R`（逆変換） | 漢字かな混じりの文字列 | 読み（Mozc の `CONVERT_REVERSE`）。できなければ空行 |

文脈は、Core が入力欄の事実（`field.before`）から作って要求に入れる。

決まりごと:

- 返る文節の読みを順につなげると、渡した読みと一致する。一致しない結果は、変換できない読みとして空行を返す。
- 失敗（Mozc を開けなかった・時間切れ）したときは null を返す。例外を Core に投げない。
- 要求は Core の変換のスレッド（入力のスレッドとは別）から来る。Mozc の操作は1つずつ順に行う。変換の結果が届くと、キーボードが `ConversionFinished` を Core に送る。
- 1回の呼び出しは 2 秒で打ち切る。

## Mozc の組み込み

かな漢字変換には、オープンソース版の [Mozc](https://github.com/google/mozc) を端末の中で動かす。

同梱するもの:

| ファイル | 中身 |
| --- | --- |
| `libmozc.so` | 変換エンジン本体。arm64-v8a と x86\_64 の2種類 |
| `mozc.data` | 変換用の辞書 |

呼び出し方:

- Mozc が Android 用に用意している JNI の入口を使う。入口のクラス名とメソッドは Mozc の側で固定されているので、それに合わせる（`com.google.android.apps.inputmethod.libs.mozc.session.MozcJni`）。
- やり取りは Mozc の命令形式（Protocol Buffers）で行う。命令の組み立てと結果の読み取りには、Mozc の定義ファイルから生成したコードを使う。
- 変換は、セッションを作る → 読みを渡す → 変換を指示する → 文節と候補を読み取る → セッションを捨てる、の順で行う。この手順で動くことは、既存の移植（[Meltype-Android](https://github.com/AoneSenbongi/Meltype-Android)）で確認されている。

Mozc には学習させない（確定した文節の区切りを覚えさせると、読みを誤った位置で区切る変換を固定してしまう）。前の版が覚えさせた Mozc の学習データは、Mozc を開くときに消す。

### Mozc を開けなかったとき

Mozc の用意（辞書を写して初期化する）は、キーボードが起動したときに裏のスレッドで行い、プロセスにつき1回だけ開く。開けなければ要求に null を返し、かなの入力だけで動き続ける。ほかの OS と同じく、Core は Mozc の失敗が3回続くと Mozc を使うのをやめる。

## Mozc のビルド

Mozc は Git に入れず、手順をスクリプトにして再現できるようにする。

- ビルドする Mozc の版は、コミットを1つに固定してリポジトリに記録する。最初に使う版は `60fe4012e5eaa26805dbbb8e5548cbe6db4aaf98`（既存の移植が Android で動作を確かめた版）。
- スクリプト1本で、Mozc の取得、エンジン本体と辞書のビルド、`mozc` モジュール内の所定の場所への配置、ライセンス表記の収集までを行う。
- ビルドした成果物（`libmozc.so`、`mozc.data`）は Git に入れない。
- ビルドには Bazel（Bazelisk 経由）を使う。初回は依存の取得を含めて 30〜40 分かかる。
- 成果物（`libmozc.so` 2種類、`mozc.data`、ライセンス表記の `MOZC_NOTICES.txt`）のどれかがない状態でアプリをビルドしようとしたら、スクリプトを先に実行するよう伝えるメッセージを出して止める。ライセンス表記も含めるのは、それがない APK は Mozc に含まれるものの配布条件を満たさないため。

## 版を上げるとき

Mozc の版を上げるのは、変換の質や不具合で必要になったときだけ。上げたら [11](11-testing.md) の変換の確認をやり直す。
