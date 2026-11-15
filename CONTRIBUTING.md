<a name="meltype-への貢献"></a>
<img src="docs/images/headings/contributing/title.svg" alt="Meltype への貢献" height="80">


不具合の報告・辞書の追加・改善の提案を歓迎します。
参加するときは [行動規範](CODE_OF_CONDUCT.md) を守ってください。

<br>

<a name="運営の方針"></a>
<img src="docs/images/headings/contributing/08.svg" alt="運営の方針" height="53"><br>


提案・Pull Request は、この方針に合うかで判断します。

**目指すこと**: ローマ字で打つだけで、日本語と英語を自分で切り替えなくていい入力にする。

**使う人**: 最終的には、一般の社会人もプログラマーも、だれでも。

<a name="原則"></a>
<img src="docs/images/headings/contributing/s07.svg" alt="原則" height="40">


1. 何もしなくても正しく動く (入れたらすぐ使える)
2. 設定で調整できる。でも、打ち方は変えさせない
3. 打った内容は記録しない。外部のサービスには、必要な最小限だけを送る
   - 例外: 学習データ (手元だけ)、診断ログ (既定は OFF・手元だけ・送るかは使う人が決める・古いものは消す)、異常終了と自動更新の記録 (手元だけ・送るかは使う人が決める)
4. Windows > Linux > Mac の順で重視する
5. IME が主役。でも、おしゃれさも大事にする

<a name="やらないこと"></a>
<img src="docs/images/headings/contributing/s08.svg" alt="やらないこと" height="40">


- 変換エンジンを一から作らない (MS IME・Mozc の上で動かす)
- クラウドの変換は使わない。AI の変換 (Zenzai) は PC の中で動かし、打った文字を外へ送らない (Zenzai は既定で ON。同梱のモデルを手元で使う)
- 有料版は作らない。広告はできるだけ避け、やむを得ないときは前もって知らせる

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

外部の辞書から作る分野の辞書 (`dictionaries/domain-*.txt`) は、ライセンスが GPL-3.0 と両立するもの (CC0・CC BY・CC BY-SA・Apache-2.0・GPL-2.0-or-later・修正 BSD など) だけを同梱します。分野ごとにファイルを分け、先頭のコメントと [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) に出典とライセンスを書きます。再配布できないもの・条件が分からないものは同梱せず、利用者が自分で `candidates.txt` の形式にしてデータのフォルダーに置きます。

<br>

<a name="issuepull-request-の扱い"></a>
<img src="docs/images/headings/contributing/09.svg" alt="Issue・Pull Request の扱い" height="53"><br>


Issue・Pull Request の本文とコメントは日本語で書いてください。日本語が書けない場合は、機械翻訳でかまいません。

`要トリアージ` が付いている Issue (メンテナーがまだ見ていないもの) には、基本的に着手しないでください。
対応したい場合は、その Issue に対応したいことをコメントで残しておいてください。メンテナーが進めると判断したら、`/self-assign` とコメントしてから対応してください ([bot のコマンド](#bot-のコマンド))。

- 「[やらないこと](#やらないこと)」に当たる要望は、理由を添えてすぐに閉じます。
- 情報が足りない報告には `情報待ち` を付けて聞き返します ([期限](#pull-request-を送る場合))。

<br>

<a name="pull-request-を送る場合"></a>
<img src="docs/images/headings/contributing/07.svg" alt="Pull Request を送る場合" height="53"><br>

Issue を直す Pull Request を送るつもりの場合は、その Issue のコメントに自分が対応することを書いてから、修正に取りかかってください。
同じ Issue を何人もが同時に直してしまうのを防ぐためです。
本文の `Fixes #123` (Issue の URL でも可) などで閉じる Issue に担当 (Assignees) がいて、作成者がその担当に入っていない Pull Request は、bot が (1 時間ごとに確かめて) Draft にし、理由をコメントします。担当のいない Issue なら、そのままです。担当に入ったら「Ready for review」で戻してください。

- **1 つの Pull Request は 1 つの目的にしてください。** その目的に必要な実装・テスト・文書は、一緒に入れて構いません。複数の目的をまとめた統合 PR は受け付けません。
- **同時にレビューする Pull Request は、メンテナーの PR を除き 1 人 2 件までです (Draft も数えます)。** メンテナーは GitHub の `OWNER`・`MEMBER`・`COLLABORATOR` を指します。
- **コンフリクトは Pull Request を出した人が直してください。**

2 件を超えた分の PR には、bot が `PR上限超過` を付けます (作成が新しいものから)。順番待ちの印で、先の PR がマージ・クローズされると外れます。Draft は作業中の下書きにだけ使ってください。
メンテナーが `次にレビュー` を付けた PR は、古い PR より先に 2 件の枠に入ります。

「1 つの目的か」などの判断はメンテナーが行います。対応が必要なときは、理由をコメントして `PR要対応` を付けます。

`PR要対応` の PR と `情報待ち` の Issue は、**bot の期限通知から 14 日間、投稿者の反応 (返信・本文の変更・push) がなければ閉じます。** 閉じた後に情報が揃ったら、再オープンを依頼してください。

<br>

<a name="コードの貢献と貢献者ライセンス同意-cla"></a>
<img src="docs/images/headings/contributing/03.svg" alt="コードの貢献と貢献者ライセンス同意 (CLA)" height="53"><br>


Meltype は GNU GPL v3 で公開していますが、GPL v3 の条件で使えない方 (非公開で利用したい方) には、作者が個別に相談して利用を認めることがあります。
これを続けられるように、初めて Pull Request を送る方には次の同意 (CLA) をお願いしています。
Pull Request を作ると、チェックが流れたあとに bot がこの文面をコメントするので、同意していただける場合は、その Pull Request に **「CLA に同意します」** と 1 行だけコメントしてください (英語なら `I agree to the CLA`)。
同意は記録され (`cla-signatures` ブランチ)、次の Pull Request からは不要です。同意するまで、Pull Request の「CLA」のチェックは失敗のままになります。

> 私は、この Pull Request で提供する貢献 (コード・辞書・文書など) について、次のことに同意します。
>
> 1. 貢献は私自身が作成したもので、私にはそれを提供する権利がある。
> 2. 私は Meltype の作者に対し、貢献を複製・改変・配布・サブライセンスする、無償で取り消し不能な、世界的・非独占的な権利を許諾する。
>    これには、GNU GPL v3 以外の条件 (作者が個別に認める利用を含む) で配布することを含む。
> 3. 貢献の著作権は私に残り、私は自分の貢献を自由に利用できる。

同意のない Pull Request は取り込めません。この同意書は簡易的なものです。貢献者が増えてきたら、専門家に確認した正式な CLA に切り替える予定です。

<a name="ソースファイルの先頭の表記"></a>
<img src="docs/images/headings/contributing/s01.svg" alt="ソースファイルの先頭の表記" height="40">


新しく作ったソースファイルの先頭には、ライセンスと著作権の行を入れてください。著作権は書いた人に残るので (上の 3)、名前は **自分の名前** (GitHub のユーザー名など) にしてください。既存のファイルをまねて作者の名前 (Yukishiro) のままにしないよう気をつけてください。

```
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 あなたの名前
```

Pull Request を作ると「notices」のチェックが確かめ、抜けや名前の間違いがあればファイルに注釈を付けて失敗にします (直し方はチェックの Summary に出ます)。

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


Meltype の日英判定・変換は手元の端末の中で行い、クラウド変換は使いません。外部サービスと連携する機能では、その機能に必要な最小限の情報だけを送ります (例: GitHub の Issue・PR のタイトルを取得するための、リポジトリと番号)。学習データ・診断ログは上の「原則」に従って扱います。

<br>

<a name="bot-のコマンド"></a>
<img src="docs/images/headings/contributing/05.svg" alt="bot のコマンド" height="53"><br>


Issue・Pull Request のコメントの行の先頭に書くと、bot が GitHub Actions で実行して結果をコメントします。1 つのコメントに書けるコマンドは 1 つです。`/repro` だけは 1 行に 1 つずつ、10 個まで書けます。

| コマンド | すること | 使える人 |
|---|---|---|
| `/repro <打ったキー> [space\|enter\|none]` | 最新のコード (と最新のリリース) で打ってみて、結果を返す | だれでも |
| `/explain <打ったキー>` | 英数状態の検知での 1 文字ずつの判定理由 | だれでも |
| `/ca <打ったキー>` | Space で変換して、文節の区切りと最初の文節の候補 (9 件まで) を出す (最新のコードと最新のリリース) | だれでも |
| `/test <SHA>` | Pull Request のコードでテストを流す | メンテナー |
| `/pack <SHA>` | Pull Request のコードで Windows のテスト版の zip を作る | メンテナー |
| `/self-assign` | 自分を Issue の Assignees に入れる (担当になる) | だれでも |
| `/help` | コマンドの一覧 | だれでも |

`/test`・`/pack` の `<SHA>` は、中身を確かめた commit の SHA (40 桁) です。PR の今の head と違えば動かしません。

「変換・判定の間違い」の Issue を作ると、`/repro` と同じことを自動で行います (「打ったもの」がキーの英字のとき)。漢字の変換は再現しません (日本語 / 英語の判定だけを見ます)。

`要トリアージ` が付いている Issue では、メンテナー以外は `/self-assign` できません ([Issue・Pull Request の扱い](#issuepull-request-の扱い))。

<a name="自動の仕分け"></a>
<img src="docs/images/headings/contributing/s06.svg" alt="自動の仕分け" height="40">


新しい報告には、bot が次のラベルを自動で付けます。

- `要トリアージ`: 作者がまだ見ていない (確認したら外す)
- `再現済み`: bot が再現した

<br>

<a name="pull-request-のチェック"></a>
<img src="docs/images/headings/contributing/06.svg" alt="Pull Request のチェック" height="53"><br>


Pull Request を作ると、変わったものに合わせて次のチェックが自動で流れます。

| チェック | 流れるとき | 内容 |
|---|---|---|
| test / core | いつも | Windows 版のビルドとテスト (test)、判定・変換のコアのテスト (core。Linux・Mac) |
| mac / linux | Mac 版・Linux 版か共通部分が変わったとき | Mac 版・Linux 版のビルドとテスト。Linux 版は IBus で実際に打って確かめます。できた配布物は実行結果の Artifacts から取れます |
| web | `web/` が変わったとき | Web (Cloudflare Workers・報告フォーム) の構文・テスト |
| 辞書の形式 | `dictionaries/` が変わったとき | 読めない行・ひらがなでない読み・重複など (`node tools/check-dictionaries.mjs` で手元でも確かめられます) |
| 精度の比較 | 判定・変換の本体か辞書が変わったとき | main と Pull Request で品質テストを比べ、新しく外れた例があれば失敗。直った例も一覧に出ます。あわせて、よく使う語 (約 5.7 万語) の判定が main から変わった語を一覧にします (参考。失敗にはしません) |
| 報告の再現 | いつも | Pull Request の本文の「## 確かめること」の「入力 → 期待」を、Pull Request のコードと最新のリリースの Mozc で確かめます |
| CLA | いつも | 貢献者ライセンス同意 (上の「コードの貢献と貢献者ライセンス同意」)。初めての方は、同意するまで失敗します |
| notices | いつも | 足したソースファイルの先頭のライセンス・著作権の表記 (上の「ソースファイルの先頭の表記」) |
| フォーマット | いつも | すべての `.cs` の空白・改行・字下げを `dotnet format whitespace` で調べ、違反があれば失敗 |
| 静的解析 | いつも | Roslyn の解析の警告が 1 件でもあれば失敗 |
| CodeQL | いつも | C#・C++・JavaScript・Python・Actions のセキュリティの解析。新しく見つかった重大な問題があれば失敗 |
| スクリプト | いつも | `.github/bot`・`tools` の Node のスクリプトのテスト |
| 依存関係 | いつも | 足した・上げた依存に既知の脆弱性 (high 以上) があれば失敗 |

結果は、チェックの「Details」→「Summary」に表で出ます。

フォーマットは自動では直さないので、失敗したら `dotnet format whitespace --folder` を流してください。静的解析は `dotnet build Meltype.sln -c Release -p:AnalysisLevel=latest-recommended -p:TreatWarningsAsErrors=true -p:EnableWindowsTargeting=true` で手元でも確かめられます (Linux・macOS でも Windows 版まで調べられます)。CodeQL は、前からあるコードの問題ではなく、Pull Request で新しく出た問題だけを見ます (前からある問題は Security タブの Code scanning に一覧で残し、少しずつ直します)。
