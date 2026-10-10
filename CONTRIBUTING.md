<a name="meltype-への貢献"></a>
<img src="docs/images/headings/contributing/title.svg" alt="Meltype への貢献" height="80">


不具合の報告・辞書の追加・改善の提案を歓迎します。
参加するときは [行動規範](CODE_OF_CONDUCT.md) を守ってください。

<br>

<a name="不具合の報告"></a>
<img src="docs/images/headings/contributing/01.svg" alt="不具合の報告" height="53"><br>


GitHub のアカウントが無い場合は、Meltype のトレイのメニュー「不具合の報告・提案...」から開くフォームで送れます (送った内容は Issue になります。仕組みは [tools/report-form](tools/report-form/README.md))。
自動の判定を自分で直したとき (F6〜F10・変換の候補で英字 / かなを選び直したとき) は、トレイの「直した誤判定を報告...」から、打ったもの・出たもの・直した結果が入った状態で「変換・判定の間違い」の報告を始められます (Meltype キーボード。記録は Meltype を終了すると消え、押すまで送りません)。

[Issue](https://github.com/yksr-melt/Meltype/issues/new/choose) のひな形 (不具合 / 変換・判定の間違い / 辞書 / 提案) から選んで書いてください。
Mac 版・Linux 版はプレビュー版です。気づいたことは小さなことでも報告してもらえると助かります。
どのひな形でも、大事なのは次の 3 つです。

1. どのアプリで (メモ帳 / Chrome / Discord など)
2. 何と打って (例: `kyouhagoogle` と打って Enter)
3. どうなったか (例: 「今日はごおｇぇ」になった)

タスクトレイのアイコンを右クリック →「ログ / 判定理由...」→「コピー」で取れるログも貼ってもらえると助かります
(ログには直前に打った文字の一部が含まれるので、見られて困る部分は消してください)。

<br>

<a name="辞書の追加"></a>
<img src="docs/images/headings/contributing/02.svg" alt="辞書の追加" height="53"><br>


`dictionaries/` の辞書 (英単語・固有名詞・同音異義語の候補・文脈の手がかり) への追加は、Pull Request か Issue でお送りください。

<br>

<a name="pull-request-を送る場合"></a>
<img src="docs/images/headings/contributing/07.svg" alt="Pull Request を送る場合" height="53"><br>

Issue を直す Pull Request を送るつもりの場合は、その Issue のコメントに自分が対応することを書いてから、修正に取りかかってください。
同じ Issue を何人もが同時に直してしまうのを防ぐためです。

<br>

<a name="コードの貢献と貢献者ライセンス同意-cla"></a>
<img src="docs/images/headings/contributing/03.svg" alt="コードの貢献と貢献者ライセンス同意 (CLA)" height="53"><br>


Meltype は GNU GPL v3 で公開していますが、GPL v3 の条件で使えない方 (非公開で利用したい方) には、作者が個別に相談して利用を認めることがあります。
これを続けられるように、初めて Pull Request を送る方には次の同意 (CLA) をお願いしています。
Pull Request を作ると bot がこの文面をコメントするので、同意していただける場合は、その Pull Request に **「CLA に同意します」** と 1 行だけコメントしてください (英語なら `I agree to the CLA`)。
同意は記録され (`cla-signatures` ブランチ)、次の Pull Request からは不要です。同意するまで、Pull Request の「CLA」のチェックは失敗のままになります。

> 私は、この Pull Request で提供する貢献 (コード・辞書・文書など) について、次のことに同意します。
>
> 1. 貢献は私自身が作成したもので、私にはそれを提供する権利がある。
> 2. 私は Meltype の作者に対し、貢献を複製・改変・配布・サブライセンスする、無償で取り消し不能な、世界的・非独占的な権利を許諾する。
>    これには、GNU GPL v3 以外の条件 (作者が個別に認める利用を含む) で配布することを含む。
> 3. 貢献の著作権は私に残り、私は自分の貢献を自由に利用できる。

同意のない Pull Request は取り込めません。

<a name="ソースファイルの先頭の表記"></a>
<img src="docs/images/headings/contributing/s01.svg" alt="ソースファイルの先頭の表記" height="40">


新しく作ったソースファイルの先頭には、ライセンスと著作権の行を入れてください。著作権は書いた人に残るので (上の 3)、名前は **自分の名前** (GitHub のユーザー名など) にしてください。既存のファイルをまねて作者の名前 (Yukishiro) のままにしないよう気をつけてください。

```
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 あなたの名前
```

Pull Request を作ると bot が確かめ、抜けや名前の間違いがあればコメントします。
(この同意書は簡易的なものです。貢献者が増えてきたら、専門家に確認した正式な CLA に切り替える予定です)

<br>

<a name="ai-の利用について"></a>
<img src="docs/images/headings/contributing/04.svg" alt="AI の利用について" height="53"><br>


Meltype の開発には AI (Claude Code など) を使っています (コミットの `Co-Authored-By` に書いています)。
貢献でも、AI を使うこと自体は歓迎します。そのうえで、次のことをお願いします。

<a name="pull-request"></a>
<img src="docs/images/headings/contributing/s02.svg" alt="Pull Request" height="40">


- **中身を理解して、責任を持てるものだけを送ってください。** 何をなぜ変えたかを自分の言葉で説明でき、質問に答えられること。
  AI が出したものを確かめずにそのまま送ることはしないでください。
- **自分で動かして確かめてください。** テストを流し、判定・変換を変えたなら実際に打って確かめてください (「## 確かめること」に例を書くと、チェックが確かめます)。
- **AI を使ったら、必ず Pull Request に書いてください。** どの部分に使ったか (コードの下書き・テスト・文書・辞書など) を一言で十分です。書いてあれば、使ったことを理由に断ることはありません。
  **書かずに送られた Pull Request が AI で作られたものに見える場合は、メンテナーの判断で、確認なしにクローズすることがあります。**
  使っていないのにクローズされた場合は、そう書いてもらえれば再オープンします。Issue も同じです (AI で作った報告をそのまま大量に送らないでください)。
- **CLA は AI を使った部分にも適用されます。** 「提供する権利がある」ことを確かめてください。AI がほかのプロジェクトのコードをそのまま出していないか (特に GPL と両立しないライセンスのもの) に気をつけてください。
- **新しい機能・設定の追加は、作る前に Issue (提案) で相談してください。** 方針に合わないと、作ってもらっても取り込めないことがあります。バグ修正・辞書の追加・小さな改善は、そのまま Pull Request で構いません。
- 大量の機械的な変更 (全体の書き換え・文体だけの直し) は、先に Issue で相談してください。

<a name="辞書"></a>
<img src="docs/images/headings/contributing/s03.svg" alt="辞書" height="40">


- **ほかの辞書・IME の辞書を AI に書き出させて追加しないでください。** 市販の IME (ATOK・Microsoft IME など) や、ライセンスが合わない辞書の内容を写したものは取り込めません。
- AI に語の候補を挙げてもらうのはかまいませんが、読み・書き方が正しいか、よく使われる語かは自分で確かめてください。
- 大きな辞書を作るときは、元にしたデータとライセンスを Pull Request に書き、作るためのスクリプト (`tools/`) も一緒に送ってください (今の辞書も JMdict・ウィクショナリー・Unicode CLDR から、出典を明記して作っています)。

<a name="issue報告"></a>
<img src="docs/images/headings/contributing/s04.svg" alt="Issue・報告" height="40">


- AI に文章を整えてもらうのはかまいませんが、**実際に起きたことを書いてください。** 打ったもの・出たもの・期待した結果は、AI に推測させずにそのまま書いてください。
- ログや打った文章を AI のサービスに貼る前に、見られて困るもの (パスワード・個人的な文章) が入っていないか確かめてください。

<a name="meltype-自体と-ai"></a>
<img src="docs/images/headings/contributing/s05.svg" alt="Meltype 自体と AI" height="40">


Meltype は、打った文字を外部のサービス (AI を含む) に送りません。判定・変換は手元の PC の中だけで行います。この方針を変える機能 (打った文字を外部に送るもの) は取り込みません。

<br>

<a name="bot-のコマンド"></a>
<img src="docs/images/headings/contributing/05.svg" alt="bot のコマンド" height="53"><br>


Issue・Pull Request のコメントの 1 行目に書くと、bot が GitHub Actions で実行して結果をコメントします。

| コマンド | すること | 使える人 |
|---|---|---|
| `/repro <打ったキー> [space\|enter\|none]` | 最新のコード (と最新のリリース) で打ってみて、結果を返す | だれでも |
| `/explain <打ったキー>` | IME 自動切替での 1 文字ずつの判定理由 | だれでも |
| `/ca <打ったキー>` | Space で変換して、文節の区切りと最初の文節の候補 (9 件まで) を出す (最新のコードと最新のリリース) | だれでも |
| `/test` | Pull Request のコードでテストを流す | メンテナー |
| `/pack` | Pull Request のコードで Windows のテスト版の zip を作る | メンテナー |
| `/help` | コマンドの一覧 | だれでも |

「変換・判定の間違い」の Issue を作ると、`/repro` と同じことを自動で行います。漢字の変換は再現しません (日本語 / 英語の判定だけを見ます)。

<a name="自動の仕分け"></a>
<img src="docs/images/headings/contributing/s06.svg" alt="自動の仕分け" height="40">


新しい報告には、bot が次のラベルを自動で付けます (どれも見当なので、作者が確認して直します)。

- `要トリアージ`: 作者がまだ見ていない (確認したら外す)
- OS・環境: `windows` `mac` `linux` `win11` `win10` `企業PC` `管理者で実行` `IME自動切替` `Mozcなし` `かな入力` `USキーボード` `高DPI`、版 (`v0.2.0` など)。Meltype の「不具合の報告・提案...」から報告すると入る実行環境から付けます
- 不具合の優先度: `優先: 高` (止まる・入力できない・文字が消えるなど) / `優先: 中`
- 起きたアプリ: `アプリ: チャット` `アプリ: コード` `アプリ: ブラウザー` `アプリ: ゲーム` `アプリ: Office`、`インストール`
- `情報待ち` (版や打ったキーが足りない)、`再現済み` (bot が再現した)

<br>

<a name="pull-request-のチェック"></a>
<img src="docs/images/headings/contributing/06.svg" alt="Pull Request のチェック" height="53"><br>


Pull Request を作ると、変わったものに合わせて次のチェックが自動で流れます。

| チェック | 流れるとき | 内容 |
|---|---|---|
| build / test | いつも | ビルドとテスト (Windows・Linux・Mac) |
| 辞書の形式 | `dictionaries/` が変わったとき | 読めない行・ひらがなでない読み・重複など (`node tools/check-dictionaries.mjs` で手元でも確かめられます) |
| 精度の比較 | 判定・変換の本体か辞書が変わったとき | main と Pull Request で品質テストを比べ、新しく外れた例があれば失敗。直った例も一覧に出ます |
| 報告の再現 | いつも | Pull Request の本文の「## 確かめること」の「入力 → 期待」と、`Fixes #12` で閉じる誤判定の報告を、Pull Request のコードで確かめます |
| CLA | 初めての方 | 貢献者ライセンス同意 (上の「コードの貢献と貢献者ライセンス同意」) |

結果は、チェックの「Details」→「Summary」に表で出ます。
