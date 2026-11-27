## ほかの文書

配布用のパッケージを作成する・Windows にインストールして検証するときは [docs/dev/DEVELOPMENT.md](docs/dev/DEVELOPMENT.md) を読む。
機能を追加するか判断するときは [CONTRIBUTING.md](CONTRIBUTING.md) の「運営の方針」、PR を出すときは「Pull Request を送る場合」を読む。
`native/tip/`・`src/Meltype.Windows/Tip/` を変更するときは、先に [docs/dev/TSF_DESIGN.md](docs/dev/TSF_DESIGN.md) を読む。
コード入力 (`src/Meltype.Core/Ime/LineContext.cs`・`ImeSession` のコードの行の判定) を変更するときは、先に [docs/dev/CODE_INPUT.md](docs/dev/CODE_INPUT.md) を読み、変更した仕様を同じ PR で更新する。
ゲームのチャット欄の判定 (`ImeTrait.ImeContext`・`ImeSession.IsGame`・`KeyboardFront.PollSystemIme`) を変更するときは、先に [docs/dev/GAME_CHAT.md](docs/dev/GAME_CHAT.md) を読む。

## 構成

入力の判定は `src/Meltype.Core` の `ImeHost` が行う。フロントエンドは OS の事実を `ImeEvent` で送り、返ってきた `ImeOutput` を適用する (`src/Meltype.Core/Ime/ImeProtocol.cs`)。

| フロントエンド | 場所 | Core との接続 |
| --- | --- | --- |
| Windows: Meltype IME (TSF) | `native/tip/` | アプリのプロセスに読み込まれる DLL。名前付きパイプで Meltype.exe (`src/Meltype.Windows/Tip/TipServer.cs`) に JSON を送る |
| Windows: Meltype キーボード | `src/Meltype.Windows/Keyboard/KeyboardFront.cs` | Meltype.exe の中で `ImeHost` を呼ぶ |
| macOS | `mac/Sources/MeltypeIME/InputController.swift` | `src/Meltype.Core.CApi` を NativeAOT でビルドした `libMeltypeNative.dylib` を dlopen する |
| Linux: IBus | `linux/ibus-engine-meltype` | `src/Meltype.Core.CApi` を Linux 向けにビルドした `libMeltypeNative.so` を dlopen する |
| Linux: fcitx5 | `linux/fcitx5/meltype.cpp` | `libMeltypeNative.so` を dlopen する |
| 端末 (pty) | `tui/meltype-tty` | 同じフォルダーの `libMeltypeNative.so` を dlopen する。`tui/build.sh` が同梱する |
| Android | `android/app/src/main/kotlin/app/meltype/keyboard/KeyboardService.kt`・`core/ImeJson.kt` (イベントと応答の JSON) | `src/Meltype.Core.CApi` を Android 向けにビルドした `libMeltypeNative.so` を JNI (`android/app/src/main/cpp/meltype_jni.cpp`) から呼ぶ |

変換エンジンは Core が別プロセスで実行する。Mozc は `meltype_mozc_helper`、Zenzai は llama.cpp の `llama-server`。Android だけは Mozc をアプリのプロセスで実行し、`meltype_mozc_helper` と同じ 1 行の要求に JNI のコールバック (`core/CoreMozcCallbacks.kt`) で応答する。

`src/Meltype.Core` の namespace は `Ime` → `Composition` (と `Composition.Utility`) → `Detection`・`Learning`・`Engine`・`Dictionary`・`Notation` → `Libraries.Chars`・`Libraries.Lexicon` の向きにだけ参照する。`Detection`・`Learning`・`Engine`・`Dictionary`・`Notation` どうしと、`Libraries` の中どうしは参照しない。どこからでも使えるのは `Config`・`Input`・`Diagnostics`。

`CompositionController` はイベントの入口だけを担当し、キー・変換・確定・表示を別の class に分離する。

## 暗黙知

- Mac・Linux 版の Core は NativeAOT で動作し、テストは JIT で動作する。reflection に依存するコードはテストが通っても配布版で動作しないので、JSON はソース生成の `JsonSerializerContext` (`CoreJsonContext` など) で読み書きする。
- キーを処理するスレッドでは待機せず、I/O もしない。時間のかかる処理はバックグラウンドのスレッドで実行し、キーの処理では結果を即座に返す。`ImeHost` は 1 つのスレッドから呼ぶ前提で排他制御しない。
- 設定と学習データは、前の版が書いたファイルも読む。前の版にあった設定の値は今の値に変換し、前の版が誤って学習したものは読み込むときに破棄する。
- Windows の 2 つのフロントエンドは、Meltype.exe の `CompositionService` を共有する。Meltype IME でも、入力モードの表示 (`ModeIndicatorWindow`) は Meltype.exe が出し、DLL が読めない入力欄の情報 (UI Automation の要素) は Meltype.exe の `FocusInspector` が読む。変換中の表示と候補の一覧は別実装で、Meltype キーボードは `CompositionWindow`、Meltype IME は DLL の `native/tip/CandidateWindow.cpp` が描画するので、見た目を変更するときは両方を修正する。
- マージした PR のブランチは Mergify が削除し、GitHub はそのブランチに向いた PR の base を変更せずに閉じる (#1909)。PR は main に向けて作成する。ほかの PR に依存する変更は、main に向けた draft の PR で出す。
- 曖昧なテキスト
  - 日英の区切りが 2 通り以上ある: `bottoshuuseisita` → botと修正した / ボット修正した
  - 文脈依存: `nijigadeta` → 虹が出た / (AI に) 自我出た
  - `tone` / `soretone`
  - `dottonetto` / `dottonet`
- `tools/make-*` の出力は手で編集せず、スクリプトを実行して再生成する。辞書は先頭のコメントに生成元のスクリプトが書いてある。

## Issue に取り組むとき

- XY 問題
- 機能・設定を追加する要望は、たいてい追加せずに解決できる。
- 新しい仕組み (bot・設定など) を作る前に、既存の仕組みで解決できないか検討する。

## コードを書くとき

- シンプルさを何よりも優先する。
- 分岐・状態・ルールは追加せず、既存のものを削減して解決する。追加するのは削減で解決できないときだけにする。ルールを追加するなら、リンター (`.editorconfig` の `dotnet_diagnostic`) で強制できるものはリンターで設定する。
- 同じ機能に複数の経路を作らない。
- 不具合の原因のある構造の整理は、1 つの機能の中ならそのまま PR にしてよい。
- 新しいソースファイルの先頭にライセンスと著作権を入れる。既存ファイルを模倣して `Yukishiro` にせず、変更を依頼した人（PR を出す人）の名前にする。不明なら質問する。

```
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 あなたの名前
```

### 語彙

- 状態マシン
- 代数的データ型
- 疎結合
- 関心の分離
- 純粋関数
- SSOT
- 根本原因分析
- fail-fast
- Parse, don't validate
- stringly typed

### コメント

既存のコメントがこの決まりに合っていなければ、模倣せずにこの決まりで書く。

コードから読めないことは、必ずコメントに書く。数値・分岐・順序には、たいてい読めないことがある (なぜその値か・どの入力のためか)。

- ファイルに決まりを入れるときは、そのファイルのコメントに理由も書く。
- ワークアラウンドには `ワークアラウンド:` を付ける。
- コードを削除したら、そのコードに言及したコメントも削除する。「〜はもう無い」とだけ書いたコメントに置き換えない。
- Issue 番号は、不具合を修正したコードにだけ書く。機能を追加したコードには書かない。

### OS ごとのコード

OS 間の差分はできるだけ作らない。

`src/Meltype.Windows`・`src/Meltype.Core.CApi` (Core を C の関数で公開する層)・`native/tip/`・`mac/`・`linux/`・`android/` はプラットフォーム層で、OS の事実を読む・結果を書く・画面を描画するだけにする。

- 機能の判定と処理は、1 つの OS でしか使わない機能も `src/Meltype.Core` に書く。プラットフォーム層からは呼ぶだけにする
- OS の API を呼ぶ機能は、Core でビルドできれば (P/Invoke・COM の関数表を直接呼ぶ。NativeAOT で動作する) Core に置く。機能のフォルダーに OS の名前を付けて並べ (`NowPlaying/WindowsMediaSession.cs`)、`OperatingSystem.IsWindows()` などで選択する。Windows の API には `[SupportedOSPlatform("windows")]` を付ける
- WinForms・組み込みの COM (`[ComImport]`) を使うものは、プラットフォーム層に置く
- 英単語の判定は全 OS で同梱の一覧 (`english-words.txt`) だけ (#1508)
- 設定・辞書・学習データと変換エンジンは、どの OS も `MeltypeRuntime` で生成する
- 変換エンジンは全 OS で Mozc と Zenzai だけ (#1740)

## ビルドとテスト

```bash
dotnet build Meltype.sln

# コアのテスト（Linux / macOS / Windows どれでも）
dotnet run --project src/Meltype.Core.Tests
dotnet run --project src/Meltype.Core.Tests -- CompositionTests # 名前に一致するテストだけ
dotnet run --project src/Meltype.Core.Tests -- --eval # 品質テスト（正解率と外れた例）
dotnet run --project src/Meltype.Core.Tests -- --repro kyouhagoogle enter # 打ったときの表示と確定結果
dotnet run --project src/Meltype.Core.Tests -- --repro 26niti space --learn "niti → （日）" # 先に学習させてから打つ
dotnet run --project src/Meltype.Core.Tests -- --explain konnichiwa hello # 1文字ずつの判定理由
dotnet run --project src/Meltype.Core.Tests -- --segments kyouhagoogle # 単位と英日区間分け
dotnet run --project src/Meltype.Core.Tests -- --expect src/Meltype.Core.Tests/data/issue-cases.tsv out.json # 報告された誤変換
dotnet run --project src/Meltype.Core.Tests -- --mixed-bench out.json # 日本語文中の英単語の区切り
dotnet run --project src/Meltype.Core.Tests -- --version

# Windows 専用（src/Meltype.Windows.Tests）
dotnet run --project src/Meltype.Windows.Tests
dotnet run --project src/Meltype.Windows.Tests -- CompositionTests
dotnet run --project src/Meltype.Windows.Tests -- --explain konnichiwa hello

# 辞書を変更したとき
node tools/check-dictionaries.mjs
```

`--repro`・`--perf` などで漢字変換するには、環境変数 `MELTYPE_MOZC` に Mozc 変換ヘルパーのパスを指定する（未指定だと判定のみで、かなのまま確定する。`--expect` は未指定だとエラーになる）。ヘルパーは最新のリリースの zip から取得する（Linux は `Meltype-<版>-linux.zip` 内 `Meltype-linux/mozc/meltype_mozc_helper`、Mac は `Meltype-<版>-mac.zip` 内 `Meltype-mac/Meltype.app/Contents/Resources/mozc/meltype_mozc_helper`、Windows は `Meltype-<版>-windows-portable.zip` 内 `meltype_mozc_helper.exe`）。1.7.1 以前のヘルパーは同梱の語句を受け取れず、`--repro`・`--expect` がエラーになるので、次のリリースまでは main の `linux`・`mac` のワークフローの Artifacts の zip から取得する。`native/mozc/build-mozc-helper.sh` で手元ビルドするのは、ヘルパーのソースや `MOZC_COMMIT` を変更したときだけ（初回 30〜40 分かかる）。`--repro` は、`MELTYPE_ZENZAI_SERVER` (llama-server) と `MELTYPE_ZENZAI_MODEL` (zenz の .gguf) も指定すれば、Zenzai も使って打った結果を `zenzai` に出力する (Windows のリリースの zip の `zenzai\` にある)。

その他のフラグ（`--perf`・`--judge-corpus`・`--henkan` など）は `src/Meltype.Core.Tests/Program.cs` のコメントを参照する。

## 作業後の確認

この変更で使わなくなったコードを削除する。

```bash
# フォーマットを修正する（CI は --verify-no-changes で同じ検査をする）
dotnet format whitespace --folder

# 静的解析（CI は警告が 1 件でもあれば失敗する。EnableWindowsTargeting で Linux / macOS でも Windows 版を検査できる）
dotnet build Meltype.sln -c Release -p:AnalysisLevel=latest-recommended -p:TreatWarningsAsErrors=true -p:EnableWindowsTargeting=true
```

## 判定・変換を修正するとき

- 報告された入力は症状の 1 例。原因を特定し、同じ原因で起きる入力をまとめて修正する。
- 条件を過剰に一般化しない。`i18n`・`p2p` を英語にするために「英字 + 数字 + 英字」を英語と判定したら、`huusoku6me-toru` が `ふうそku6meーとる` になった。

## テストと CI

テストには、実装に依存しない挙動の期待と過去のバグだけを書く。実装を検証するテスト・CI の検査は入れない。

CI は、前提 (署名の鍵などのシークレット) が無くて手順を実行できないときも、手順をスキップせずに失敗にする。

判定・変換を変更したら、次をテストに追加する。

- 報告された例
- リグレッションのありそうなもの（修正した語の逆向きで似た入力。`issueni` → `issueに` に対して `issune` → `いっすね` など）

報告された例は `src/Meltype.Core.Tests/data/issue-cases.tsv` にも `打ったもの<Tab>期待する結果` の形で追加する。

今の実装で満たせない期待はテストにしない。残すならコメントに書く。

テストを通すために実装を曲げない。既存のテストのケースが仕様として説明できないなら、実装を合わせずにテストを修正し、PR にそう書く。

## PR の「期待する打鍵と変換」

`打ったもの → 期待する結果` の行を、チェック「報告の再現」が PR のコードで検証する。`Fixes` で閉じる Issue の報告例と、変更した条件に該当する語を書く。

```
- issueni → issueに
- issune → いっすね
- kyouhagoogle → 今日はgoogle
- きょうは → 今日は
```

英字の入力は打つキーとして扱い、Enter で確定した結果か Space で変換した結果が期待と完全一致すれば通る。かなの入力は読みとして扱い、変換の候補に期待が含まれれば通る。

`→`・`->` を含む行はすべて例として読まれる。補足を同じ行に書くと期待に混入するので、別の行に書く。

## 設定

設定は最小限にする。削除できる設定は削除する。

全員が ON にして困るケースがない機能は、設定にせず常に ON にする。

設定はユーザーの好みであって、判別を省略する理由にはならない。

## 守ること

- PR は自己完結させる。後の PR で修正する前提の不具合やビルドの通らない状態を残さない。作業範囲の外で更新したほうがいいものがあれば、PR の本文の末尾に `## マージ後の TODO` の節を作り、1 行に 1 つ書く。Issue にするかはメンテナーが決める。
- PR は [テンプレート](.github/PULL_REQUEST_TEMPLATE.md) どおりに書く。
- PR のタイトルの先頭に、変更した場所のタグを付ける（`Core/Composition:`・`native/tip:`・`packaging:`・`bot:` など）。
- AI を使ったことを PR に書く。テンプレートの「AI を使った」にチェックを入れ、使った部分を書く。
- Issue・PR の本文・コメントと文書は日本語で書く。
- PR に関係する箇所の文書が古ければ、利用者向けでも開発者向けでも更新する。利用者向けの文書（ルートの `README.md`・`SECURITY.md` と `docs/*.md`）は常時メンテ対象で、挙動を変更する PR では影響する箇所を探して同じ PR で更新する。
- 利用者が知る必要がないことを利用者向けの文書に書かない。
- 機能実装・枠組みの変更・状態や設定の追加は、先に Issue で相談する。
