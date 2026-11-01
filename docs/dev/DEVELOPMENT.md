<a name="meltype-の開発"></a>
<img src="images/headings/development/title.svg" alt="Meltype の開発" height="80">


ソースからのビルド、テスト、配布用のパッケージ、動作の仕組みです。
使い方は [USAGE.md](USAGE.md)、貢献の方法は [CONTRIBUTING.md](../CONTRIBUTING.md)、リリースの手順は [RELEASE.md](RELEASE.md) にあります。

<br>

<a name="フォルダーの構成"></a>
<img src="images/headings/development/01.svg" alt="フォルダーの構成" height="53"><br>


| フォルダー | 中身 |
|---|---|
| `src/Meltype.Core/` | OS に依存しない部分 (Windows・Mac・Linux 共通)。英語 / 日本語の判定・ローマ字・変換ボックスの中身・辞書・学習・設定 |
| `src/Meltype/` | Windows 版 (キーボードフック・変換ボックスの画面・Microsoft IME の変換エンジン・IMM32 / TSF・UI Automation・スペルチェッカー・トレイ) |
| `src/Meltype.Mac.Native/` | Mac 版の IME から呼ぶ C の関数 (Meltype.Core を NativeAOT で dylib にする) |
| `mac/` | Mac 版の IME (Swift、Input Method Kit。漢字変換は azooKey) |
| `linux/` | Linux 版 (IBus・fcitx5 のエンジン) |
| `native/` | Meltype IME (Windows の TSF の DLL) と Mozc のヘルパー ([native/mozc/README.md](../native/mozc/README.md)。Mozc-UT の辞書は [MOZC-UT.md](MOZC-UT.md)) |
| `src/Meltype.Core.Tests/` | 共通部分のテスト (判定・変換ボックス・学習・品質テスト)。Mac・Linux でも動く |
| `src/Meltype.Tests/` | Windows 版のテストと調査用の道具 (共通部分のテストもまとめて流す) |
| `dictionaries/` | 組み込みの辞書 |
| `packaging/` | 配布用のファイル (インストーラー・Install.cmd など) |

`src/Meltype.Core/` の中は、`Composition/` (変換ボックスの中身: 英語の区間の判定・変換の流れ・候補・文脈・学習・ユーザー辞書・もしかして)、`Detection/` (ローマ字・英語・辞書・かな・打ち間違いの判定)、`Input/` (キーの表し方・コードの行の判定)、`Config/`・`Diagnostics/` (設定・ログ) に分かれています。

<br>

<a name="windows-でビルドして試す"></a>
<img src="images/headings/development/02.svg" alt="Windows でビルドして試す" height="53"><br>


```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Meltype.ps1
```

ビルドしてスタートアップに登録し、起動します。タスクトレイに「あ」のアイコンが出れば動いています。
`-ExecutionPolicy Bypass` はこのコマンドにだけ効き、PC 全体の設定は変えません。

アンインストールは次のとおりです。Meltype IME を登録してあれば、その登録も外します (管理者権限の確認が出ます)。

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall-Meltype.ps1            # 止めて、スタートアップから外す
powershell -ExecutionPolicy Bypass -File .\Uninstall-Meltype.ps1 -RemoveData # 設定と学習データも消す
```

必要なもの:

- Windows 10 / 11 (x64 / ARM64)
- .NET 10 SDK
- Microsoft IME (漢字変換に使います)
- Meltype IME も入れるときは、Visual Studio Build Tools の「C++ によるデスクトップ開発」(MSVC と Windows SDK)。無ければ Meltype IME を入れずに続けます (ARM64 では入れません)

<br>

<a name="maclinux-でビルドする"></a>
<img src="images/headings/development/03.svg" alt="Mac・Linux でビルドする" height="53"><br>


- Mac 版: [MAC-CLI.md](MAC-CLI.md)
- Linux 版 (WSL を含む): [WSL.md](WSL.md)

<br>

<a name="テスト"></a>
<img src="images/headings/development/04.svg" alt="テスト" height="53"><br>


```powershell
dotnet build Meltype.sln
dotnet run --project src/Meltype.Tests                                   # すべてのテスト (Windows)
dotnet run --project src/Meltype.Tests -- CompositionTests               # 名前に一致するテストだけ
dotnet run --project src/Meltype.Tests -- --explain konnichiwa hello      # 1 文字ずつの判定理由 (英数状態の検知)
dotnet run --project src/Meltype.Tests -- --convert きょうはいいてんき     # 変換エンジンの結果と文節の区切り
dotnet run --project src/Meltype.Tests -- --context この本は:あつい        # 文脈を渡したときの変換結果
dotnet run --project src/Meltype.Tests -- --eval                          # 品質テスト
```

<a name="maclinux-でのテスト"></a>
<img src="images/headings/development/s01.svg" alt="Mac・Linux でのテスト" height="40">


OS に依存しない部分 (`src/Meltype.Core`) とそのテストは、Mac・Linux でも動きます。.NET 10 SDK を入れて次を実行します。GitHub Actions でも、Ubuntu と macOS で毎回流しています。

```bash
dotnet run --project src/Meltype.Core.Tests                 # すべてのテスト
dotnet run --project src/Meltype.Core.Tests -- --eval       # 品質テスト (スペルチェッカーは使わない)
dotnet run --project src/Meltype.Core.Tests -- --version    # 版 (Meltype.Core)
dotnet run --project src/Meltype.Core.Tests -- --repro kyouhagoogle enter   # 打ったときの見え方と確定の結果
dotnet run --project src/Meltype.Core.Tests -- --mixed-bench out.json       # 日本語の文の中の英単語が正しく分かれるか (約 550 例)
dotnet run --project src/Meltype.Core.Tests -- --expect src/Meltype.Core.Tests/data/issue-cases.tsv out.json  # 報告された誤変換
dotnet run -c Release --project src/Meltype.Core.Tests -- --perf perf.json  # 変換エンジンの遅延 (p95 / p99)・候補の数・控え・メモリ・正解率 (MELTYPE_MOZC が要る)
```

Windows のアプリ本体・Mac / Linux の IME も同じ形で版を出せます (`Meltype.exe --version`、`Meltype.app/.../Meltype --version`、`ibus-engine-meltype --version`)。
Windows のアプリ本体は GUI アプリ (WinExe) なので、cmd では版がプロンプトの後に出ます。
コンソールアプリ (Exe) にすると、エクスプローラーやタスク スケジューラから起動したときにコンソールの窓が一瞬フォーカスを取り、その間に打ったキーが消えるので、WinExe のままにしています。

`src/Meltype.Core.Tests/data/issue-cases.tsv` は、GitHub の issue (open / closed) から「打ったもの」と「期待した結果」を取り出したコーパスです。判定の作り方に関係なく同じ物差しで比べられるので、判定を変えるときは `--eval` と合わせて `--expect` で流します。

`--mixed-bench` は `MELTYPE_BENCH_OFFSET=31` などで調べる語を変えられます。

機能を足す前と後で変換エンジンの速さ・精度を比べるときは、両方の版で `--perf` を動かして `--perf-compare 前.json 後.json` で比べます。測る項目と比べ方は [tools/performance](../tools/performance/README.md) にあります。

<a name="品質テスト"></a>
<img src="images/headings/development/s02.svg" alt="品質テスト" height="40">


`src/Meltype.Core.Tests/QualityTests.cs` に、日本語の文・英文・混在・英語とも日本語とも読める語・英語の後の短い語・記号と数字・小書き文字・大文字・かな入力・コードの行・文章ファイル・絵文字・もしかして の例をまとめてあります。
期待値は「理想の結果」で書いてあり、`--eval` でカテゴリーごとの正解率と外れた例を表示します。

- 通常のテストでは、全体 95% 以上・どのカテゴリーも 80% 以上を基準にしています (ある直しで別の場所が壊れたら気づけるように)
- Windows の英語のスペルチェッカーが使える環境では、それも使います (`MELTYPE_NO_SPELLCHECK=1` で使わずに測れます)
- Pull Request では「精度の比較」のチェックが main と比べ、新しく外れた例があれば失敗にします
- 新しい不具合の報告を受けたら、まずここに例を足してから直すのがおすすめです
- 直した語だけでなく、**逆向き・似た入力の例** も足してください。たとえば `midi` を英語にする直し (#1122) は、`midika` (身近) を `midiか` にしてしまいました (#1179)。英語にする直しなら、その語で始まる日本語の語を、日本語にする直しなら、その語を含む英単語を足します

<a name="よく使う語の判定の変化"></a>
**よく使う語の判定の変化 (影響範囲の確認)**: 品質テストの例は数百個なので、そこに無い語が変わっても気づけません。
「精度の比較」のチェックは、よく使う語の読み (`dictionaries/readings.txt` をローマ字にしたもの) と英単語 (`dictionaries/english-words.txt`) を 1 語ずつ打って確定し、main と見え方が変わった語を Summary に一覧にします。
変わった語が間違いとは限らない (直したかった語は変わって当然) ので失敗にはせず、意図しない語が変わっていないかを人が確かめるための参考にします。
辞書から消した語・足した語も比べられるように、変えたあとは変える前の一覧も渡して、その語も判定します (チェックでは main の側にも Pull Request の一覧を渡します)。
Summary には 200 語まで出し、全件はログと、成果物 `judged-words` の `changes.tsv` に出します。手元では次のように比べられます。

```
dotnet run --project src/Meltype.Core.Tests -c Release -- --judge-corpus before.tsv              # 変える前
dotnet run --project src/Meltype.Core.Tests -c Release -- --judge-corpus after.tsv before.tsv    # 変えたあと (変える前の語も判定する)
node .github/bot/pr-checks.mjs impact before.tsv after.tsv changes.tsv
```

<br>

<a name="配布用のパッケージを作る"></a>
<img src="images/headings/development/05.svg" alt="配布用のパッケージを作る" height="53"><br>


```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Package.ps1
```

`dist\` に zip ができます。中身はビルド済みの `app` フォルダーと、`Install.cmd` / `Uninstall.cmd` (ダブルクリックで実行)、`README.txt` です。インストーラー (`setup.exe`) は、GitHub Actions がこの zip の中身から Inno Setup で作ります。

- .NET のランタイムを `app\dotnet` に同梱しているので、使う人の PC に .NET は要りません (`Meltype.exe` がそこを使う: `AppHostDotNetSearch=AppRelative`)
- 小さくする (30MB 未満) ために、ランタイムから Meltype が使わない部品を削ります
  1. Meltype.dll が使う型から参照をたどり、要らないアセンブリを削る (`Meltype.Tests -- --runtime-closure`)
  2. 自己診断 (`Meltype.exe --selftest`) を走らせ、実際に読み込まれなかった大きなアセンブリを削る
  3. もう一度自己診断を走らせ、削りすぎていないことを確かめる (失敗したら zip を作らない)
- 自己診断は、設定・判定・辞書・変換エンジン・Windows の候補 API・UI Automation・各画面・タスクトレイ・データの保存を、キーボードフックを掛けずに一通り動かします
- インストール先は `%LOCALAPPDATA%\Programs\Meltype` で、管理者権限は要りません。配布用のファイルの元は [packaging/](../packaging/) にあります

ポータブル版は次で作れます。

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Package.ps1 -Portable
```

通常版と分けて `dist\Meltype-test-<日時>-windows-portable.zip` を作ります。リリースビルドでは版番号を含む `Meltype-<版>-windows-portable.zip` になります。ZIPをフォルダーごと展開し、その中の `Meltype.exe` を実行してください。アプリと.NETランタイムを同梱するため、.NET未導入のPCでも起動できます。ユーザーデータは `Meltype.exe` の隣の `data` に保存します。IME登録、自動起動、アプリ内更新は含めません。

<a name="ウイルス対策ソフトの検査"></a>
<img src="images/headings/development/s03.svg" alt="ウイルス対策ソフトの検査" height="40">


GitHub Actions の配布ジョブは、公開する前に Microsoft Defender の定義を更新し、できたフォルダーと zip を検査します。検出・検査のエラー・Defender が無効のときは、公開を止めます。
手元で配布するときも、管理者の PowerShell で次を実行してください。

```powershell
tools/Scan-WindowsPackage.ps1 -Path @('dist/Meltype', 'dist/Meltype-<version>-windows.zip')
```

検査は駆除せずに行い、検出した内容をコマンドの出力に残します。検出されたら、対象のファイル・版・ハッシュ・Defender の定義の版を確かめ、誤検知が疑われるときは [Microsoft の審査窓口](https://www.microsoft.com/en-us/wdsi/filesubmission) に出してください。
検査に通ったことは、その時点の判定で、安全や将来の判定を保証するものではありません。検査に失敗したときの動きは `powershell -NoProfile -File tools/Test-WindowsPackageScan.ps1` で確かめられます。

<br>

<a name="動作の仕組み"></a>
<img src="images/headings/development/06.svg" alt="動作の仕組み" height="53"><br>


どの動作モードも、キーボードフック (`WH_KEYBOARD_LL`) を専用のスレッドで受けます。Meltype が送り直したキーには印 (`dwExtraInfo = "MELT"`) を付け、自分では判定しません。

<a name="meltype-キーボード"></a>
<img src="images/headings/development/s04.svg" alt="Meltype キーボード" height="40">


```
物理キー ─▶ KeyboardMonitor ─▶ CaptureGate (変換ボックスが開いている間は、キーとクリックをすべて順番どおりに保留)
                                   │
                                   ▼ (UI スレッド)
                           CompositionController ◀─ CompositionDetector (英語の区間の判定)
                                   │                  HybridConverter (Mozc / Microsoft IME の変換エンジン)
                                   │                  ContextRules / ConversionHistory / CandidateDictionary
                                   │                  FocusInspector (UI Automation: 入力欄か・カーソルの前後の文字)
                                   ▼
                  変換ボックスに表示 ─▶ Enter で確定した文字列を SendInput (Unicode) で入力欄へ
```

Meltype IME は、この変換ボックスの代わりに、TSF で入力欄に直接入力します ([TSF_DESIGN.md](TSF_DESIGN.md))。
