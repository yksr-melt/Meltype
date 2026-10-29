<!-- Automatically converted from spec/10-licensing.html. Check upstream edits before submitting. -->

# 10 ライセンス

確定

## このアプリのライセンス

GPL-3.0-or-later。

段階3で Meltype（GPL-3.0）のコードを Kotlin に移植して取り込むため、アプリ全体が GPL-3.0 になる。最初からこのライセンスにしておく。

公開するときは、配布する APK に対応するソースコードを一緒に公開する。

## 同梱・利用するもの

| もの | ライセンス | 使う段階 | 必要なこと |
| --- | --- | --- | --- |
| [Mozc](https://github.com/google/mozc)（エンジン本体と辞書） | BSD-3-Clause。辞書や依存ライブラリはそれぞれの条件 | 段階1 | 著作権表示とライセンス文を同梱する |
| Meltype の辞書 `loanwords.txt`（JMdict から作られたもの） | CC BY-SA 4.0 | 段階2 | 出典（JMdict / Electronic Dictionary Research and Development Group）とライセンスを表示する。手を加えたものも同じ条件で公開する |
| [Meltype](https://github.com/yksr-melt/Meltype) のコードと辞書 | GPL-3.0。辞書はファイルごとの条件 | 段階3 | 元の著作権表示を残す。移植したファイルには、元のファイルと移植したことを明記する |
| Unicode の絵文字の一覧 [`emoji-test.txt`](https://unicode.org/Public/18.0.0/emoji/emoji-test.txt)（Unicode Emoji 18.0。形を変えたものを `app/src/main/assets/emoji.txt` として入れる） | Unicode License v3 | 段階1 | 著作権表示と許諾の文を同梱する。版を上げるときは、`tools/make-emoji-asset.py` で作り直し、この行と `THIRD-PARTY-NOTICES.md` の版を直す |
| Kotlin や Android の標準ライブラリ、Protocol Buffers | Apache-2.0、BSD-3-Clause など | 段階1 | ライセンス文を同梱する |

## 表記の置き場所

- リポジトリの直下に `LICENSE`（GPL-3.0 の全文）と `THIRD-PARTY-NOTICES.md`（同梱物の一覧と各ライセンス文）を置く。
- アプリの導入画面から、同じ内容を見られるようにする。
- Mozc とその依存ライブラリの表記は、Mozc のビルドのスクリプトが集める（[06](06-conversion-mozc.md)）。

## 新しく何かを取り込むとき

取り込む前に、GPL-3.0 と一緒に配布できるライセンスかを確かめ、上の表に行を足す。
