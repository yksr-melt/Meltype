詳しい開発手順は [docs/dev/DEVELOPMENT.md](docs/dev/DEVELOPMENT.md)、貢献の決まりは [CONTRIBUTING.md](CONTRIBUTING.md) を見てください。

## 構成（どこを触るか）

| 場所                         | 中身                                                                                         |
| -------------------------- | ------------------------------------------------------------------------------------------ |
| `src/Meltype.Core`         | OS 非依存の判定・変換コア（Composition / Detection / Input / Config / Diagnostics）。判定・変換を直すときはまずここ |
| `src/Meltype.Core.Tests`   | コアのテスト（どの OS でも動く）                                                                         |
| `src/Meltype`              | Windows 版（TSF・UI・更新）                                                                       |
| `src/Meltype.Tests`        | Windows 版のテスト（Windows 専用。Core のテストもまとめて流せる）                                                |
| `src/Meltype.Mac.Native`   | Mac 版から呼ぶコアの出口                                                                             |
| `native/tip`・`native/mozc` | ネイティブ（TSF の TIP・Mozc）                                                                      |
| `mac/`                     | Mac 版（Swift）                                                                               |
| `linux/`                   | Linux 版（fcitx5・ibus）とパッケージ                                                                 |
| `web/`                     | Web（Cloudflare Workers・報告フォーム）                                                             |
| `dictionaries/`            | 辞書                                                                                         |
| `tools/`                   | 辞書生成・検査・ベンチ・テスト用スクリプト                                                                      |
| `docs/`                    | 利用者向けの文書（使い方・OS ごとの説明）。`docs/release-notes/` はリリースノート                                              |
| `docs/dev/`                | 開発者向けの文書（開発・設計・リリースの手順）                                                                     |

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

# Windows 専用（src/Meltype.Tests。Windows のスペルチェッカーも使う）
dotnet run --project src/Meltype.Tests
dotnet run --project src/Meltype.Tests -- CompositionTests
dotnet run --project src/Meltype.Tests -- --explain konnichiwa hello
dotnet run --project src/Meltype.Tests -- --convert きょうはいいてんき # 変換結果と文節区切り
dotnet run --project src/Meltype.Tests -- --context この本は:あつい # 文脈付き変換

# 辞書を変えたとき
node tools/check-dictionaries.mjs
```

- Linux / macOS では `src/Meltype.Tests` は動かない。`src/Meltype.Core.Tests` で確認する。
- `--repro`・`--perf` などで漢字変換するには、環境変数 `MELTYPE_MOZC` に Mozc 変換ヘルパーのパスを指定する（未指定だと判定のみで、かなのまま確定する。`--expect` は未指定だとエラーになる）。
- ヘルパーは main の `linux.yml` 成果物 `meltype-linux` の zip 内 `Meltype-linux/mozc/meltype_mozc_helper` を使う。
- `native/mozc/build-mozc-helper.sh` で手元ビルドするのは、ヘルパーのソースや `MOZC_COMMIT` を変えたときだけ（初回 30〜40 分かかる）。

その他のフラグ（`--perf`・`--judge-corpus`・`--henkan` など）は `src/Meltype.Core.Tests/Program.cs` のコメントを見てください。

## 作業後の確認

```bash
# フォーマットを直す（CI は --verify-no-changes で同じ検査をする）
dotnet format whitespace --folder
```

## 判定・変換を直すとき

- 報告された入力は症状の 1 例。原因を突き止め、同じ原因で起きる入力をまとめて直す。
- 条件を過剰に一般化しない。`i18n`・`p2p` を英語にするために「英字 + 数字 + 英字」を英語と判定したら、`huusoku6me-toru` が `ふうそku6meーとる` になった。

## テストの書き方

判定・変換を変えたら、次をテストに足す。

- 報告された例
- リグレッションのありそうなもの（直した語の逆向きで似た入力。`issueni` → `issueに` に対して `issune` → `いっすね` など）
- エッジケース
- 将来の挙動を保証したいもの

実装の写しのようなテストは書かない。

テストを通すために実装を曲げない（特定の入力だけを通す分岐など）。テストの期待が誤っているならテストを直し、PR にそう書く。

## 設定を足すとき

- ON と OFF のそれぞれに意味がある設定だけを足す。
- 設定はユーザーの好みであって、判別をサボる理由にはならない。
- すべての設定の組み合わせがメンテナンス対象で、2^n のオーダーで増える。

## PR の「確かめること」

`打ったもの → 期待する結果` の行を、チェック「報告の再現」が PR のコードで確かめる。`Fixes` で閉じる Issue の報告例と、直した語の逆向きで似た入力を書く。

```
- issueni → issueに
- issune → いっすね
- kyouhagoogle → 今日はgoogle
- きょうは → 今日は
```

- `→`・`->` を含む行はすべて例として読まれる。補足を同じ行に書くと期待に混ざるので、別の行に書く。
- 英字の入力は Enter で確定した結果と完全一致で比べる。短い語は Enter だとかなのまま確定する（`midika` は `みぢか`）ので、変換の結果を確かめるならかなの入力で書く（`みぢか → 身近`）。

## 守ること

- 新しいソースファイルの先頭にライセンスと著作権を入れる。既存ファイルを真似て `Yukishiro` にせず、変更を頼んだ人（PR を出す人）の名前にする。分からなければ聞く。

```
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 あなたの名前
```

- PR は [テンプレート](.github/PULL_REQUEST_TEMPLATE.md) どおりに書く。
- PR のタイトルの先頭に、触った場所のタグを付ける（`Core/Composition:`・`native/tip:`・`packaging:`・`bot:` など）。
- AI を使ったことを PR に書く。テンプレートの「AI を使った」にチェックを入れ、使った部分を書く。
- PR の本文・コメント・文書は日本語で書く。
- PR と関係ない場所を変更しない。
- PR に関係する箇所の文書が古ければ、利用者向けでも開発者向けでも直す。利用者向けの文書（ルートの `README.md`・`SECURITY.md` と `docs/*.md`）は常時メンテ対象で、挙動を変える PR では影響する箇所を探して同じ PR で直す。
- 機能実装や枠組みを変える変更は、先に Issue で相談する。
- `tools/make-*` の出力は手で直さず、スクリプトを流して作り直す。辞書は先頭のコメントに作成元のスクリプトが書いてある。
- 著作権名・仕様の解釈など、分からないことは推測で進めず聞く。
