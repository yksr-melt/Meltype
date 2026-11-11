詳しい開発手順は [docs/dev/DEVELOPMENT.md](docs/dev/DEVELOPMENT.md)、貢献の決まりは [CONTRIBUTING.md](CONTRIBUTING.md) を見てください。

## 構成

| 場所                         | 中身                                                                                         |
| -------------------------- | ------------------------------------------------------------------------------------------ |
| `src/Meltype.Core`         | OS 非依存の判定・変換コア（Composition / Detection / Input / Config / Diagnostics） |
| `src/Meltype.Core.Tests`   | コアのテスト（どの OS でも動く）                                                                         |
| `src/Meltype.Windows`      | Windows 版（TSF・UI・更新）                                                                       |
| `src/Meltype.Windows.Tests` | Windows 版のテスト（Windows 専用。Core のテストもまとめて流せる）                                                |
| `src/Meltype.Mac.Native`   | Mac 版から呼ぶコアの出口                                                                             |
| `native/tip`・`native/mozc` | ネイティブ（TSF の TIP・Mozc）                                                                      |
| `mac/`                     | Mac 版（Swift）                                                                               |
| `linux/`                   | Linux 版（fcitx5・ibus）とパッケージ                                                                 |
| `web/`                     | Web（Cloudflare Workers・報告フォーム）                                                             |
| `dictionaries/`            | 辞書                                                                                         |
| `tools/`                   | 辞書生成・検査・ベンチ・テスト用スクリプト                                                                      |
| `docs/`                    | 利用者向けの文書（使い方・OS ごとの説明）。`docs/release-notes/` はリリースノート                                              |
| `docs/dev/`                | 開発者向けの文書（開発・設計・リリースの手順）                                                                     |

## 入力のフロント

入力の判断は `src/Meltype.Core/Ime/` (`ImeSession`) がする。フロントは OS の事実を送り、返ってきた指示を適用する。

| フロント | 場所 |
| --- | --- |
| Windows: Meltype IME (TSF) | `native/tip/` (DLL)・`src/Meltype.Windows/Tip/TipServer.cs` (Meltype.exe 側) |
| Windows: Meltype キーボード | `src/Meltype.Windows/Keyboard/KeyboardFront.cs` |
| macOS | `mac/Sources/MeltypeIME/InputController.swift` |
| Linux: IBus | `linux/ibus-engine-meltype` |
| Linux: fcitx5 | `linux/fcitx5/meltype.cpp` |
| Android | `android/app/src/main/kotlin/io/github/ninjin0802/keyboard/app/core/ImeJson.kt`（出来事と応答の JSON）・`KeyboardService.kt` |

## OS ごとのコード

`src/Meltype.Windows`・`src/Meltype.Mac.Native`・`mac/`・`linux/` は OS の出入口で、OS の事実を読む・結果を書く・画面を描くだけにする。

- 機能の判断と処理は、1 つの OS でしか使わない機能も `src/Meltype.Core` に書く。OS の出入口からは呼ぶだけにする
- OS の API を呼ぶ機能は、Core でビルドできれば (P/Invoke・COM の関数表を直接呼ぶ。NativeAOT で動く) Core に置く。機能のフォルダーに OS の名前を付けて並べ (`NowPlaying/WindowsMediaSession.cs`)、`OperatingSystem.IsWindows()` などで選ぶ。Windows の API には `[SupportedOSPlatform("windows")]` を付ける
- Core にもう口がある OS の部品 (`IKanjiConverter`・`IWordChecker`) と、WinForms・組み込みの COM (`[ComImport]`) を使うものは、OS の出入口に置く
- 設定・辞書・学習データは、どの OS も `MeltypeRuntime` で作る

## ビルドとテスト

```bash
dotnet build Meltype.sln

# コアのテスト（Linux / macOS / Windows どれでも）
dotnet run --project src/Meltype.Core.Tests
dotnet run --project src/Meltype.Core.Tests -- CompositionTests # 名前に一致するテストだけ
dotnet run --project src/Meltype.Core.Tests -- --eval # 品質テスト（正解率と外れた例）
dotnet run --project src/Meltype.Core.Tests -- --repro kyouhagoogle enter # 打ったときの見え方と確定結果
dotnet run --project src/Meltype.Core.Tests -- --explain konnichiwa hello # 1文字ずつの判定理由
dotnet run --project src/Meltype.Core.Tests -- --segments kyouhagoogle # 単位と英日区間分け
dotnet run --project src/Meltype.Core.Tests -- --expect src/Meltype.Core.Tests/data/issue-cases.tsv out.json # 報告された誤変換
dotnet run --project src/Meltype.Core.Tests -- --mixed-bench out.json # 日本語文中の英単語の区切り
dotnet run --project src/Meltype.Core.Tests -- --version

# Windows 専用（src/Meltype.Windows.Tests。Windows のスペルチェッカーも使う）
dotnet run --project src/Meltype.Windows.Tests
dotnet run --project src/Meltype.Windows.Tests -- CompositionTests
dotnet run --project src/Meltype.Windows.Tests -- --explain konnichiwa hello
dotnet run --project src/Meltype.Windows.Tests -- --convert きょうはいいてんき # 変換結果と文節区切り
dotnet run --project src/Meltype.Windows.Tests -- --context この本は:あつい # 文脈付き変換

# 辞書を変えたとき
node tools/check-dictionaries.mjs
```

Linux / macOS では `src/Meltype.Windows.Tests` は動かない。`src/Meltype.Core.Tests` で確認する。

`--repro`・`--perf` などで漢字変換するには、環境変数 `MELTYPE_MOZC` に Mozc 変換ヘルパーのパスを指定する（未指定だと判定のみで、かなのまま確定する。`--expect` は未指定だとエラーになる）。ヘルパーは main の `linux.yml` 成果物 `meltype-linux` の zip 内 `Meltype-linux/mozc/meltype_mozc_helper` を使う。`native/mozc/build-mozc-helper.sh` で手元ビルドするのは、ヘルパーのソースや `MOZC_COMMIT` を変えたときだけ（初回 30〜40 分かかる）。

その他のフラグ（`--perf`・`--judge-corpus`・`--henkan` など）は `src/Meltype.Core.Tests/Program.cs` のコメントを見てください。

## 作業後の確認

```bash
# フォーマットを直す（CI は --verify-no-changes で同じ検査をする）
dotnet format whitespace --folder

# 静的解析（CI は警告が 1 件でもあれば失敗する。EnableWindowsTargeting で Linux / macOS でも Windows 版を調べられる）
dotnet build Meltype.sln -c Release -p:AnalysisLevel=latest-recommended -p:TreatWarningsAsErrors=true -p:EnableWindowsTargeting=true
```

## 判定・変換を直すとき

- 辞書の追加で直るなら、辞書の追加で直す。
- 報告された入力は症状の 1 例。原因を突き止め、同じ原因で起きる入力をまとめて直す。
- 条件を過剰に一般化しない。`i18n`・`p2p` を英語にするために「英字 + 数字 + 英字」を英語と判定したら、`huusoku6me-toru` が `ふうそku6meーとる` になった。

## テストの書き方

判定・変換を変えたら、次をテストに足す。

- 報告された例
- リグレッションのありそうなもの（直した語の逆向きで似た入力。`issueni` → `issueに` に対して `issune` → `いっすね` など）
- エッジケース
- 将来の挙動を保証したいもの

報告された例は `src/Meltype.Core.Tests/data/issue-cases.tsv` にも `打ったもの<Tab>期待する結果` の形で足す。

実装の写しのようなテストは書かない。

テストを通すために実装を曲げない。既存のテストのケースが仕様として説明できないなら、実装を合わせずにテストを直し、PR にそう書く。

## PR の「確かめること」

`打ったもの → 期待する結果` の行を、チェック「報告の再現」が PR のコードで確かめる。`Fixes` で閉じる Issue の報告例と、直した語の逆向きで似た入力を書く。

```
- issueni → issueに
- issune → いっすね
- kyouhagoogle → 今日はgoogle
- きょうは → 今日は
```

英字の入力は打つキーとして扱い、Enter で確定した結果か Space で変換した結果が期待と完全一致すれば通る。かなの入力は読みとして扱い、変換の候補に期待が含まれれば通る。

`→`・`->` を含む行はすべて例として読まれる。補足を同じ行に書くと期待に混ざるので、別の行に書く。

## 設定を足すとき

ON と OFF のそれぞれに意味がある設定だけを足す。設定はユーザーの好みであって、判別をサボる理由にはならない。すべての設定項目の追加はユーザーの負担になるので、設定の追加には慎重になる。

## 守ること

- 新しいソースファイルの先頭にライセンスと著作権を入れる。既存ファイルを真似て `Yukishiro` にせず、変更を頼んだ人（PR を出す人）の名前にする。分からなければ聞く。

```
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 あなたの名前
```

- PR は [テンプレート](.github/PULL_REQUEST_TEMPLATE.md) どおりに書く。
- PR のタイトルの先頭に、触った場所のタグを付ける（`Core/Composition:`・`native/tip:`・`packaging:`・`bot:` など）。
- AI を使ったことを PR に書く。テンプレートの「AI を使った」にチェックを入れ、使った部分を書く。
- Issue・PR の本文・コメントと文書は日本語で書く。
- PR と関係ない場所を変更しない。
- PR に関係する箇所の文書が古ければ、利用者向けでも開発者向けでも直す。利用者向けの文書（ルートの `README.md`・`SECURITY.md` と `docs/*.md`）は常時メンテ対象で、挙動を変える PR では影響する箇所を探して同じ PR で直す。
- 利用者が知る必要がないことを利用者向けの文書に書かない。
- 機能実装や枠組みを変える変更は、先に Issue で相談する。
- OS 間の差分はできるだけ作らない。
- `tools/make-*` の出力は手で直さず、スクリプトを流して作り直す。辞書は先頭のコメントに作成元のスクリプトが書いてある。
- 著作権名・仕様の解釈など、分からないことは推測で進めず聞く。
