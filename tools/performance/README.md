<a name="性能の計測"></a>
<img src="../../docs/images/headings/performance/title.svg" alt="性能の計測" height="80">


Meltype の性能を測る仕組みは 2 つあります。

- **変換エンジンの計測 (`--perf`)**: Mozc を使って例文を打ち、遅延・段階ごとの時間・候補の数・控えの使い回し・メモリ・正解率を測ります。変換の精度を上げる機能を足す前と後の比較に使います (#279)
- **打鍵の処理の計測 (`benchmark-composition.py`)**: 変換エンジンを外して (候補を返さない偽物にして)、1 キーごとの処理だけを NativeAOT で測ります。下の [打鍵の処理の計測](#打鍵の処理の計測-benchmark-compositionpy)

どちらもアプリ本体には組み込みません。ふだんの入力では何も測らず、何もログに残しません。

<br>

<a name="変換エンジンの計測---perf"></a>
<img src="../../docs/images/headings/performance/01.svg" alt="変換エンジンの計測 (--perf)" height="53"><br>


```powershell
$env:MELTYPE_MOZC = "$PWD\native\mozc\bin\meltype_mozc_helper.exe"
dotnet run -c Release --project src/Meltype.Core.Tests -- --perf perf.json
```

```bash
MELTYPE_MOZC=/path/to/meltype_mozc_helper dotnet run -c Release --project src/Meltype.Core.Tests -- --perf perf.json
```

`--perf 出力ファイル [例文のファイル] [回数]` です。例文は既定で `tools/mozc-compare-cases.tsv` (1 行に「打ったもの<Tab>期待」)、回数は 5 回です。
`MELTYPE_MOZC` が無いと判定だけ (漢字にしない) で測るので、時間は Mozc ありのものと比べられません。
必ず `-c Release` で動かしてください (結果の `meta.configuration` に残ります)。

<a name="前と後を比べる"></a>
<img src="../../docs/images/headings/performance/s01.svg" alt="前と後を比べる" height="40">


```powershell
git worktree add ..\meltype-base main
cd ..\meltype-base
dotnet run -c Release --project src/Meltype.Core.Tests -- --perf $env:TEMP\perf-base.json
cd -
dotnet run -c Release --project src/Meltype.Core.Tests -- --perf $env:TEMP\perf-current.json
dotnet run -c Release --project src/Meltype.Core.Tests -- --perf-compare $env:TEMP\perf-base.json $env:TEMP\perf-current.json
```

`--perf-compare` は、項目ごとの前・後・倍率を Markdown の表で出し (そのまま Pull Request に貼れます)、正解・不正解が変わった例文と、品質テストで新しく外れた例を出します。
比べる 2 つの版は、どちらも `--perf` がある版 (#279 より後) にしてください。

時間はばらつきます。同じ版を 2 回測っても、p95 は 1.5 倍、p99 は 2 倍ほどずれることがあります。

- ほかに重い処理が動いていないときに、同じ PC・同じ電源の設定で測る
- 前 → 後 → 前 → 後 のように交互に 2 回ずつ測り、同じ版どうしのずれより大きいかを見る
- 回数を増やす (`--perf perf.json tools/mozc-compare-cases.tsv 20`) と p99 が安定する
- 回数・控えの使い回し率・候補の数・正解率はばらつかないので、変わっていればそれは変更のせいです

<a name="測るもの"></a>
<img src="../../docs/images/headings/performance/s02.svg" alt="測るもの" height="40">


1 つの例文を、変換の控えを捨ててから打つ (**cold**) のと、すぐにもう一度打つ (**warm**、控えが残っている) のを交互に繰り返します。最初の 1 周は JIT と変換ヘルパーの起動を済ませるためだけに打ち、数えません。
時間はミリ秒で、p50 / p95 / p99 は小さい方から 50% / 95% / 99% の位置の値 (nearest-rank 法) です。

| 結果の項目 | 意味 |
|---|---|
| `latency.keyCold` / `keyWarm` | 1 キーを押してから変換ボックスの表示ができるまで (ライブ変換で Mozc に頼む時間を含む) |
| `latency.conversionCold` / `conversionWarm` | Space を押してから変換の結果と候補が出るまで |
| `stages.cold` / `warm` | 1 文 (例文を打って Space で変換するまで) あたりの時間の内訳。`ConverterMs` は変換エンジン (Mozc) を呼んだ時間、`CandidatesMs` は候補の一覧を引いた時間、`OtherMs` はそれ以外 (判定・ローマ字・文節の組み立て・辞書の候補)、`ConverterCalls` は変換エンジンを呼んだ回数 |
| `cache` | Mozc の控えの使い回し。`ConversionHitRate` は同じ読みの変換を使い回した割合、`CandidateHitRate` は候補の一覧を使い回した割合、`HelperMsPerCall` は変換ヘルパーとの 1 回のやり取りの時間 |
| `candidates` | Space で変換したときの、選んでいる文節の候補の数 |
| `memory` | `managedHeapStartBytes` / `EndBytes` は辞書を読んだ後・測り終えた後の .NET のヒープ (常駐のメモリ)、`peakWorkingSetBytes` はプロセスの最大のメモリ、`helperWorkingSetBytes` は変換ヘルパーのメモリ、`allocatedBytesPerKey` は 1 キーあたりに一時的に確保したメモリ |
| `accuracy.conversion` | 例文の変換が期待どおりになった割合 (空白と大文字・小文字は区別しない) |
| `accuracy.brokenEnglish` | 誤った修正の割合: 期待にある英単語が、結果では英字のまま残っていない (かなにした・つづりを変えた) 例文の数 |
| `accuracy.quality` | 品質テスト (`--eval`、判定の正解率) の全体とカテゴリーごとの正解率 |
| `results` | 例文ごとの結果 (結果の文字列・正解か・候補の数・1 キーの p95・変換の時間) |

変換の控えの回数とヘルパーとのやり取りの時間は、`MozcConverter.Statistics()` で読みます。控えを引くときに回数を足しているだけなので、ふだんの入力は遅くなりません (アプリはこれを読みません)。

<a name="入力した文字について"></a>
<img src="../../docs/images/headings/performance/s03.svg" alt="入力した文字について" height="40">


結果の JSON には、例文のファイルにある文と、それを打った結果だけが入ります。
例文のファイルには、自分が実際に打った文 (メール・チャットなど) を入れないでください。足すときは、作った文か、公開されている文にしてください。

<a name="測らないもの"></a>
<img src="../../docs/images/headings/performance/s04.svg" alt="測らないもの" height="40">


Windows のキーボードフック・TSF・IMK・IBus とのやり取りと、画面に描くまでの時間は含みません (`Meltype.Core` の中だけ)。
Microsoft IME で変換するときの時間も測りません (Mozc が無いときは判定だけで測ります)。

<br>

<a name="打鍵の処理の計測-benchmark-compositionpy"></a>
<a name="composition-performance-check"></a>
<img src="../../docs/images/headings/performance/02.svg" alt="打鍵の処理の計測 (benchmark-composition.py)" height="53"><br>

変換エンジンを外して、1 キーごとの処理 (判定・ローマ字・表示の組み立て) だけを NativeAOT で測ります。

```sh
python3 tools/benchmark-composition.py /path/to/baseline-checkout \
  --output /path/to/results
```

比べる元 (baseline) には、v1.0.0 のコミット `467255bfe3e36b803a3fd3f5a1480fe35d5058c9` を使います。
コマンドは、両方の版を同じ C# の計測用プログラム・同じ SDK・同じ NativeAOT の設定でビルドし、場面ごとにどちらの版を先に動かすかを入れ替えます。
場面ごとに 64・128・256 キーを打ち、3 回の慣らしの後に 15 回測ります。ほかに重い処理が動いていない PC で測ってください。

- `--common-dictionaries`: 今のリポジトリの辞書を両方の版に入れます (辞書の変更の影響を除いて比べるため)
- `--jit`: NativeAOT ではなく、Release の JIT のコードで動かします (段階的なコンパイルは切ります)

<a name="測る範囲"></a>
<img src="../../docs/images/headings/performance/s05.svg" alt="測る範囲" height="40">

時間に入るのは、`HandleKey`、結果の JSON への書き出し、Enter での確定です。辞書の読み込み・起動・セッションを作る時間は入りません。
両方の版とも、空の言語の学習を使います。変換エンジンは、候補を何も返さない偽物です。
記録するのは、合計の時間・スレッドが確保したメモリ・1 キーごとの p50 / p95 / max・確定した文字列の SHA-256 です。
URL 以外の確定した文字列は、baseline と同じでなければなりません (URL は打ったまま残すように変えたので、わざと違います)。

<a name="結果"></a>
<img src="../../docs/images/headings/performance/s06.svg" alt="結果" height="40">

`report.json` には、生の計測値・中央値・倍率・辞書とプログラムのハッシュ・SDK と PC の情報が入ります。
今の版の合計の時間の中央値が baseline の 50% を超えるか、変わってはいけない確定の文字列が変わると、コマンドは失敗します。
これは手元で動かす合成の性能の確認です。IMK・IBus・Windows の画面とのやり取りの遅延や、変換エンジンの時間は測りません。NativeAOT のビルドの診断は別に保存します。
