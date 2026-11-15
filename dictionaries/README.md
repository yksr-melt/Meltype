# 共同編集する辞書

<!-- meltype-flash-document-banner:start -->
<p align="center"><img src="../assets/branding/readme/documentation.svg" alt="Meltype-FLASH — 稲妻のように、言葉の壁を駆け抜ける日本語入力。" width="1000"></p>
<!-- meltype-flash-document-banner:end -->

このディレクトリーの **txtが編集元** です。GitHubのPull Requestで変更してください。`cargo build` がビルド時にFST索引と値データを生成し、Rust実行ファイルへ埋め込みます。FSTバイナリーや利用者のSQLiteをPull Requestへ入れる必要はありません。

- `english.txt` / `japanese.txt`: ローマ字の単語。空白・改行区切り、`#`以降はコメント。
- `propernouns.txt`: 固有名詞。元の大文字小文字を保持してください。
- `candidates.txt` / `loanwords.txt` / `brands.txt`: 読みと候補を空白で区切ります。候補の並びも動作の一部です。
- `emoji*.txt`: 読みと候補をタブで区切ります。
- `countries.txt`: 英語として扱う国名の語。`propernouns.txt` と同じ形式で、読み込むときにローマ字として読める語などは外します。
- `domain-*.txt`: 分野の辞書。読みと表記をタブで区切り、表記は複数書けます。変換の候補では、ひらがな・カタカナの後ろに並びます。上流のファイルをそのまま同梱しています。
- `greetings.txt`: 予測変換に出す定型句を `読み<TAB>語句<TAB>出し始める読み` で書きます。
- `contexts.txt`: `読み 候補 : 文脈の語`。同点の場合は先のルールが優先されます。
- 意味・翻訳・読みなどの生成辞書は、各txt冒頭の出典と対応する `tools/make-*.mjs` を確認してください。既存のライセンス・出典表記を保持してください。
- `katakana.txt` は JMdict 由来のカタカナ読み (CC BY-SA 4.0)、`readings-extra.txt` は補足の読み (GPL-3.0-or-later) です。詳細は `THIRD-PARTY-NOTICES.md` を参照してください。

変更後は `cargo test -p meltype-core` と候補互換試験を実行してください。ビルド時FSTはUTF-8の全バイトと行順を保持するため、表示順を変える編集もレビュー対象です。元版と意図的に異なる候補を追加した場合、Rust出力をそのまま元版の期待値へ書き換えないでください。

利用者自身の登録語はプロファイルの `userdict.sqlite3` に保存されます。アプリの辞書画面・取り込み/書き出しを使い、利用中のDBの直接編集は避けてください。従来のtxt/JSON/plistは取り込み・互換バックアップで利用できます。
