<a name="meltype-の使い方"></a>
<img src="images/headings/usage/title.svg" alt="Meltype の使い方" height="80">

Meltype では、英語と日本語を切り替えずにローマ字のまま続けて打てます。英単語は英字のまま、日本語はかなと漢字になります。

```
kyouhagoogledekensaku  →  今日はgoogleで検索
I want to go to the park  →  I want to go to the park
```

この文書は、Windows・Mac・Linux で共通の使い方です。インストールと OS ごとの設定は [WINDOWS.md](WINDOWS.md)・[MACOS.md](MACOS.md)・[LINUX.md](LINUX.md) にあります。

設定の名前は、Windows と Linux の設定画面での名前です。Mac では `config.json` を直接書きます。設定の項目は、同じフォルダーの `config.schema.json` で探せます。

<br>

<a name="打ち方の基本"></a>
<img src="images/headings/usage/01.svg" alt="打ち方の基本" height="53"><br>

1. ローマ字で打ちます。英単語の所は英字で、日本語の所はかなで表示されます
2. <kbd>Space</kbd> で漢字に変換します。もう一度 <kbd>Space</kbd> を押すと、次の候補になります
3. <kbd>Enter</kbd> で確定します

4 文字以上の日本語は、<kbd>Space</kbd> を押す前から漢字で表示されます (ライブ変換)。表示のままでよければ、そのまま <kbd>Enter</kbd> で確定できます。

英単語で終わっているときの <kbd>Space</kbd> は、変換せずに確定して空白を入れます。英文は、ふつうのキーボードと同じように <kbd>Space</kbd> で区切りながら打てます。英単語で終わっていても変換したいときは、<kbd>Shift</kbd> + <kbd>Space</kbd> を押します。

<a name="よく使うキー"></a>
<img src="images/headings/usage/s01.svg" alt="よく使うキー" height="40">

| したいこと | キー |
| --- | --- |
| 漢字に変換する・次の候補にする | <kbd>Space</kbd> / <kbd>↓</kbd> |
| 前の候補に戻る | <kbd>Shift</kbd> + <kbd>Space</kbd> / <kbd>↑</kbd> |
| 候補を番号で選ぶ | <kbd>1</kbd>〜<kbd>9</kbd> |
| 候補の一覧の次のページ・前のページを出す | <kbd>PageDown</kbd> / <kbd>PageUp</kbd> |
| 予測変換の候補を選ぶ | <kbd>Tab</kbd> / <kbd>Shift</kbd> + <kbd>Tab</kbd> |
| 確定する | <kbd>Enter</kbd> |
| 1 文字消す | <kbd>BackSpace</kbd> |
| 変換をやめてかなに戻す | 変換中に <kbd>BackSpace</kbd> か <kbd>Esc</kbd> |
| 打った文字をすべて取り消す | 変換していないときに <kbd>Esc</kbd> |
| 全角スペースを入れる | 何も打っていないときに <kbd>Shift</kbd> + <kbd>Space</kbd> |

- <kbd>BackSpace</kbd> は、かなの 1 音 (「きょ」「っ」) ずつ消します
- 変換中に数字を入れたいときは、設定「変換中の数字キーで候補を選ぶ」を OFF にします。数字キーで確定して、その数字が入ります
- 変換中にほかのキーを押したり、ほかの場所をクリックしたりすると、確定してからそのキーやクリックがアプリに届きます

<a name="文節を選んで変換する"></a>
<img src="images/headings/usage/s02.svg" alt="文節を選んで変換する" height="40">

<kbd>Space</kbd> で変換すると、文が文節に分かれて、最後の文節が選ばれます。

- <kbd>←</kbd> <kbd>→</kbd> で、候補を替えたい文節を選びます。<kbd>Space</kbd> を押す前でも選べます
- 区切りがずれているときは、<kbd>Shift</kbd> + <kbd>←</kbd> <kbd>→</kbd> で、選んでいる文節を 1 文字ずつ縮めたり伸ばしたりします

英語と日本語の区切り方が何通りもある文は、文全体が 1 つの文節になります。候補には区切り方ごとの変換結果が並びます (`botto` → botと・ボット)。区切り方を決めた後に文節ごとに選び直すときは、<kbd>←</kbd> <kbd>→</kbd> を押すと、選んでいる候補の区切りで文節に分かれます。

<a name="ひらがなカタカナ英字にする"></a>
<img src="images/headings/usage/s03.svg" alt="ひらがな・カタカナ・英字にする" height="40">

打っている途中に押すと、文字の種類を決められます。

| キー | 文字の種類 |
| --- | --- |
| <kbd>F6</kbd> / <kbd>Ctrl</kbd> + <kbd>U</kbd> | ひらがな |
| <kbd>F7</kbd> / <kbd>Ctrl</kbd> + <kbd>I</kbd> | カタカナ |
| <kbd>F8</kbd> | 半角カタカナ |
| <kbd>F9</kbd> / <kbd>Ctrl</kbd> + <kbd>P</kbd> | 全角英字 |
| <kbd>F10</kbd> / <kbd>Ctrl</kbd> + <kbd>O</kbd> | 半角英字 |

- <kbd>F9</kbd> と <kbd>F10</kbd> は、続けて押すと 大文字 → 先頭だけ大文字 → 小文字 の順に切り替わります (ai → AI → Ai → ai)
- <kbd>F6</kbd>〜<kbd>F8</kbd> を押した後に <kbd>Space</kbd> を押すと、英語と判定された所もローマ字として読んで変換します

<a name="ローマ字の綴り"></a>
<img src="images/headings/usage/s04.svg" alt="ローマ字の綴り" height="40">

綴りは Microsoft IME と同じです。

- 「ん」は、子音の前なら `n`、それ以外は `nn` か `xn` です。「こんにちは」は `konnnichiha` と打ちます
- 小さい文字は、前に `x` か `l` を付けます (`xtu` → っ、`la` → ぁ、`xya` → ゃ)
- `-` は ー、`,` は 、、`.` は 。、`[` `]` は 「」、`/` は ・ になります。英文の続きでは半角のままです
- 数字は半角のまま入ります。<kbd>Space</kbd> を押すと、①・一・Ⅰ・１ などの候補が出ます。数字の後にかなを続けると日本語になります (`3ji` → 3時)
- 何も打っていないときのテンキーは、数字と記号をそのまま半角で入れます。Excel や CAD で数値を打つときに、確定の手間がかかりません

自分の綴りを足すときは、[辞書のファイル](#辞書のファイルに足す) の `romaji.txt` に書きます。

<br>

<a name="英語と日本語の判定"></a>
<img src="images/headings/usage/02.svg" alt="英語と日本語の判定" height="53"><br>

Meltype は、打った文字を単語ごとに見て、英単語の所だけを英字にします。

<a name="英字になる語"></a>
<img src="images/headings/usage/s05.svg" alt="英字になる語" height="40">

| 英字になる語 | 例 |
| --- | --- |
| ローマ字として読めない語 | `google`・`github`・`hello`・`npm` |
| 英語の固有名詞 | `amazon`・`adobe`・`netflix` |
| 大文字で打ち始めた語 | `Tokyo`・`I` |
| 英文の途中の、助詞と同じ形の 2 文字の語 | `I want to` の `to` |

`sushi`・`repo` のように英語とも日本語とも読める語は、入力欄の前後の文で決めます。

```
I love ｜ is great   + sushi  →  sushi
今日は｜             + sushi  →  すし
```

前後の文で決まらないときは、日本語になります。

<a name="判定が外れたとき"></a>
<img src="images/headings/usage/s06.svg" alt="判定が外れたとき" height="40">

打っている途中に <kbd>半角/全角</kbd> (<kbd>Alt</kbd> + <kbd>&#96;</kbd>) を押すと、日本語 ⇔ 英字 が切り替わります。<kbd>F6</kbd> でひらがな、<kbd>F10</kbd> で英字にもできます。変換の候補の最後にも、打ったままの英字と全角の英字が出ます (`api` → あぴ … api・ａｐｉ)。

英語とも日本語とも読める語は、直して確定すると、次からその語を直した方で出します。

<a name="確定した後の自動修正"></a>
<img src="images/headings/usage/s07.svg" alt="確定した後の自動修正" height="40">

英語か日本語か分からないまま確定した語は、次に打った語ではっきりしたら、自動で直します。

```
i  を確定して、続けて want と打つ                      →  I want
make sure you  (まけすれよう) の後に have と打つ        →  make sure you have
英文の続きで sushi を確定して、続けて gasuki と打つ     →  すしがすき
```

- 日本語で確定した語を英語に直すのは、その語を <kbd>Space</kbd> で区切ったときだけです。<kbd>Enter</kbd> で確定した語は直しません
- 候補を自分で選び直した語と、確定した後にカーソルを動かしたときは直しません
- 直してほしくないときは、設定「確定後も文脈に合わせて直す」を OFF にします

<a name="--で始まる語"></a>
<img src="images/headings/usage/s08.svg" alt="/ $ @ で始まる語" height="40">

行の先頭か空白の直後に `/`・`$`・`@` で始まる語を打つと、空白までをそのままアプリに渡します。AI エージェントのコマンド (`/review`・`$skill`) やファイル名 (`@src/app.ts`) で、アプリの補完をそのまま使えます。空白の後は、いつもどおり判定します (`/review kyouha` → /review 今日は)。

メールアドレス (`taro@example.com`) の `@` のように、前に文字が続いているときは対象外です。この動きは設定「/ $ @ で始まる語はそのまま入力」で OFF にできます。

<a name="自動判定を使わない"></a>
<img src="images/headings/usage/s09.svg" alt="自動判定を使わない" height="40">

自分で英字と日本語を決めたいときは、次のどちらかを設定します。

| 設定 | 動き |
| --- | --- |
| 「英語 / 日本語を自動で判定する」を OFF | 大文字で始めた語だけを英字にします。ほかの英単語は「Tab → google (英字に)」と出るので、<kbd>Tab</kbd> で英字にします |
| 「英字を主に入力 (変換キー / ↓ で日本語に)」を ON | 英字をそのまま入れます。<kbd>Space</kbd> は空白、<kbd>Enter</kbd> は英字のまま確定です。日本語にしたい語は、<kbd>変換</kbd> か <kbd>↓</kbd> で変換します (`sakura` → 桜)。Microsoft IME の「主に英語を入力する」と同じ動きです |

<br>

<a name="日本語入力と英数"></a>
<img src="images/headings/usage/03.svg" alt="日本語入力と英数" height="53"><br>

ふだんは日本語入力のまま、英語も日本語も打てます。英数は、打った文字を判定せずにそのまま入れる状態です。

何も打っていないときに次のキーを押すと切り替わります。

| キー | 切り替え |
| --- | --- |
| <kbd>半角/全角</kbd> / <kbd>Alt</kbd> + <kbd>&#96;</kbd> | 日本語入力 ⇔ 英数 |
| <kbd>英数</kbd> / <kbd>無変換</kbd> | 英数にする |
| <kbd>かな</kbd> / <kbd>ひらがな/カタカナ</kbd> / <kbd>変換</kbd> | 日本語入力にする |

- 今のモードは、タスクバー・メニューバーの「あ」「A」で分かります。Windows の Meltype キーボードは [WINDOWS.md](WINDOWS.md#今のモード-日本語--英数-の見分け方) を見てください
- 日本語入力 ⇔ 英数 のキーは、設定「入力モード切り替えキー」で変えられます。<kbd>Shift</kbd> + <kbd>Space</kbd> にすると、変換中の <kbd>Shift</kbd> + <kbd>Space</kbd> は「前の候補」のままで、打っている途中は日本語 ⇔ 英字 の切り替えになります
- <kbd>半角/全角</kbd> の無い US 配列のキーボードでは、設定「左 Alt で英数 / 右 Alt で日本語」を ON にすると、左右の <kbd>Alt</kbd> を単独で押して切り替えられます (Windows)

英数でも、単語の打ち始めでローマ字だと分かったら、日本語入力に戻して打ち直します。英数のまま気付かずに日本語を打ち始めても、そのまま打ち続けられます。この動きは設定「英数状態でもローマ字を検知」で OFF にできます。

<a name="アプリごとの動き"></a>
<img src="images/headings/usage/s10.svg" alt="アプリごとの動き" height="40">

設定「アプリ」→「アプリ別設定」で、アプリごとに種類を決められます。

| 種類 | 動き |
| --- | --- |
| 一般 | この文書のとおりに動きます |
| コード | コードは英数のまま入れ、コメントと文字列の中だけ日本語を判定します。コードエディターとターミナルは、最初からこの種類です ([WINDOWS.md](WINDOWS.md#アプリの種類-コードエディターターミナル)) |
| ゲーム | 英数で始め、操作のキーはそのままゲームに届きます。チャットでは <kbd>半角/全角</kbd> で日本語にでき、<kbd>Enter</kbd> で確定するか <kbd>Esc</kbd> で取り消すと英数に戻ります |
| OFF | 何もしません |

パスワードの入力欄では、どの種類でも何もしません。

<br>

<a name="変換"></a>
<img src="images/headings/usage/04.svg" alt="変換" height="53"><br>

漢字への変換には、同梱の Mozc を使います。Windows・Mac 版は、Zenzai (ニューラルかな漢字変換) で前の文も見て変換します。

<a name="最初の候補"></a>
<img src="images/headings/usage/s11.svg" alt="最初の候補" height="40">

最初の候補は、前後の文に合わせて選びます。

- 前後に手がかりの語があれば、それに合う候補にします (気温 → 暑い、財布 → 革、ご飯 → 箸)
- 前に選び直した候補は、次から最初に出します
- カーソルの前の文も使います (この本は + `atsui` → 厚い)

候補の一覧には、ふつうの候補の後に、ひらがな・カタカナ、分野の辞書の語 (医療・情報・駅名・人名・法令名など)、絵文字・顔文字が並びます。英単語の候補には、固有名詞の正しい形 (iPhone)、先頭だけ大文字、すべて大文字、全角が並びます。

<a name="予測変換"></a>
<img src="images/headings/usage/s12.svg" alt="予測変換" height="40">

打っている途中に <kbd>Tab</kbd> を押すと、続きの候補を選べます。候補は、前に確定した語句、ユーザー辞書の語、英単語の続き (`decis` → decisions) から出します。

<a name="もしかして"></a>
<img src="images/headings/usage/s13.svg" alt="もしかして" height="40">

よくある書き間違いを打つと、「もしかして: ブレスレット　<Tab>で修正」と出ます。<kbd>Tab</kbd> を押すと正しい読みに直ります。

```
しゅみれーしょん   →  シミュレーション
ばとみんとん       →  バドミントン
ぶれすれっど       →  ブレスレット
```

書き間違いは、[辞書のファイル](#辞書のファイルに足す) の `misspellings.txt` に足せます。

<a name="確定した文を変換し直す"></a>
<img src="images/headings/usage/s14.svg" alt="確定した文を変換し直す" height="40">

入力欄で文字を選んで <kbd>変換</kbd> を押すと、選んだ文字を変換し直します (Windows と Linux の IBus)。

- 漢字の文は、読みに戻して変換し直します
- 判定を誤ってローマ字のまま入った文は、日本語の所だけを日本語にします (`asita no meeting de review shimasu` → 明日の meeting で review します)

<a name="句読点と空白"></a>
<img src="images/headings/usage/s15.svg" alt="句読点と空白" height="40">

設定の「全般」で、次の見た目を変えられます。

| 設定 | 既定 | 変えると |
| --- | --- | --- |
| 句読点 | 、。 | ，．・，。・、． から選べます |
| 英単語の前後に半角スペース | OFF | 日本語と英単語の間に半角スペースを入れます (今日は GitHub に push した) |
| 英字の後ろの , . も句読点にする | OFF | 英字だけを打ったときの `,` `.` も句読点にします (`IME.` → IME。) |
| ライブ変換 | ON | <kbd>Space</kbd> を押すまで、かなで表示します |

数字の間の `.` `,` (1.5・1,000) は、どの設定でも半角のままです。

<br>

<a name="読みで出せる特別な候補"></a>
<img src="images/headings/usage/05.svg" alt="読みで出せる特別な候補" height="53"><br>

次の読みだけを打って変換すると、特別な候補が出ます。ほかの語と続けて打ったとき (`きょうは`) は出ません。

| 打つ読み | 出る候補 |
| --- | --- |
| いま・なう | 今の時刻と日付 (17:22・午後5時22分・2026年10月9日(金) 17時22分 など) |
| きょう | 今日の日付 (2026年10月9日・10月9日(金)・2026-10-09・金曜日 など) |
| 10がつ9にち | その日付の書き方 (10/9・10月9日(金)・2026/10/09(金) など) |
| とけい・じこく | 設定「時計の候補のタイムゾーン」の今の時刻 |
| 100どる・1まんえんをゆーろ | 別の通貨に換算した額 (100どる → 約1.6万円) |
| えがお・ねこ・にほん | 絵文字 (😊・🐱・🇯🇵) |
| かおもじ | 顔文字の一覧 |
| ごんべんにふるとり | 部首を組み合わせた漢字 (誰・讎) |
| 「」・() などの括弧 | 同じ系統の括弧 |
| ばーじょん | 今使っている Meltype の版 |

<a name="日付と時刻"></a>
<img src="images/headings/usage/s16.svg" alt="日付と時刻" height="40">

- 日付だけを打ったときは、今年の日付として曜日を出します
- 「なう」は、音楽を再生中なら時刻より先に曲名を出します (`曲名 / アーティスト`・`#NowPlaying 曲名 / アーティスト`。Windows)。設定「「なう」で再生中の曲を候補に出す」で OFF にできます
- 「とけい」「じこく」のタイムゾーンは、空ならこの PC と同じです。`America/New_York` などを書くと、その場所の時刻を出します。「いま」「なう」の時刻もこのタイムゾーンになります
- 日付・時刻の候補は、選んでも学習しません

<a name="通貨の換算"></a>
<img src="images/headings/usage/s17.svg" alt="通貨の換算" height="40">

通貨だけを書くと円に、円を書くとドルとユーロに換算します。「〜を〜」で換算先を選べます (`1まんえんをゆーろ`)。

使える通貨は、えん・どる・ゆーろ・ぽんど・げん・うぉん・すいすふらん・かなだどる・ごうどる・ほんこんどる・しんがぽーるどる・ばーつ・るぴー です。

レートは、その版の Meltype を出したときの欧州中央銀行 (ECB) の参照レートです。今のレートとずれるので、額は 2 桁に丸めた概算で、レートの日付も候補に付けます。

<a name="絵文字顔文字"></a>
<img src="images/headings/usage/s18.svg" alt="絵文字・顔文字" height="40">

絵文字と顔文字は、候補の一覧の最後に並びます。変換してすぐ <kbd>↑</kbd> を押すと、いちばんよく使われる絵文字に飛びます。続けて <kbd>↑</kbd> を押すと次の絵文字です。

絵文字は、[辞書のファイル](#辞書のファイルに足す) の `emoji.txt` に足せます。

<a name="部首から漢字を探す"></a>
<img src="images/headings/usage/s19.svg" alt="部首から漢字を探す" height="40">

読みが分からない漢字は、部首や部品の呼び名を「に」でつないで変換すると探せます。

```
ごんべんにふるとり  →  誰・讎 …
にんべんにき        →  休 …
```

- 最初の呼び名は、ごんべん・さんずい のような部首の名前にします。`きにかける` のような、ふつうの言葉と同じ読みは、ふつうに変換します
- 使える呼び名は [dictionaries/radicals.txt](../dictionaries/radicals.txt) にあります
- 部品の位置 (左右・上下) は区別しません。同じ部品を重ねた字 (林・森) も区別しません

<a name="版の確かめ方"></a>
<img src="images/headings/usage/s20.svg" alt="版の確かめ方" height="40">

`ばーじょん` だけを打って変換すると、`Meltype-1.5.1 (Windows / Meltype IME)` のような候補が出ます。不具合を報告するときに貼ってください。この候補は選んでも学習しないので、ふつうに「バージョン」と打つときの候補は変わりません。

<br>

<a name="自分の語を足す"></a>
<img src="images/headings/usage/06.svg" alt="自分の語を足す" height="53"><br>

<a name="ユーザー辞書"></a>
<img src="images/headings/usage/s21.svg" alt="ユーザー辞書" height="40">

ユーザー辞書に登録した語は、変換でいちばん先に出ます。

- Windows: タスクトレイの「ユーザー辞書...」で登録します。読みはローマ字でも入れられます (`kigoutou` → きごうとう)
- Mac・Linux: データのフォルダーの `userdict.txt` に、1 行に「読み<Tab>単語」で書きます ([MACOS.md](MACOS.md#ユーザー辞書))

長い読みの一部に登録した読みが含まれていても、その部分は登録した語になります (きごうとう → 記号等 を登録すると、`kigoutoufukume` → 記号等含め)。1 文字の読みは、その 1 文字だけを変換するときにだけ使います。

ほかの IME の辞書も取り込めます。Windows の「ユーザー辞書...」の「取り込む...」で、Microsoft IME の「一覧の出力」・Google 日本語入力のエクスポート・macOS の「ユーザ辞書」の `.plist` を選びます。

<a name="定型文"></a>
<img src="images/headings/usage/s22.svg" alt="定型文" height="40">

署名やひな形のような決まった文は、`;名前` で呼び出せます。

1. 名前と文を登録します。Windows はタスクトレイの「定型文...」、Mac・Linux はデータのフォルダーの `snippets.json` です。複数行の文も登録できます
2. 行の先頭か空白の直後に `;名前` と打って、<kbd>Space</kbd> か <kbd>Tab</kbd> を押すと、登録した文に置き換わります

- 置き換えた直後に <kbd>Esc</kbd> を押すと、`;名前` に戻ります
- 先頭の記号は、設定「定型文を出す記号」で変えられます
- 「書き出す」「取り込む」で、別の PC に移したり、チームで同じひな形を使ったりできます
- カーソルの前の文字をアプリから読めない入力欄では使えません

<a name="辞書のファイルに足す"></a>
<img src="images/headings/usage/s23.svg" alt="辞書のファイルに足す" height="40">

[データのフォルダー](#データと学習) の中に `dictionaries` フォルダーを作り、次のファイルを置くと、同梱の辞書に足せます。書き方は、同じ名前の [同梱の辞書](../dictionaries/) を見てください。置いた後に Meltype を起動し直すと使えます。

| ファイル | 足せるもの |
| --- | --- |
| `candidates.txt` | 変換の候補 |
| `contexts.txt` | 前後の語に合わせて最初に出す候補 |
| `english.txt` | 英語と判定させたい語 |
| `japanese.txt` | 日本語と判定させたい語 (ヘボン式のローマ字で書く) |
| `propernouns.txt` | 英語の固有名詞とその正しい形 (`GitHub`) |
| `misspellings.txt` | [もしかして](#もしかして) で直す書き間違い |
| `emoji.txt` | [絵文字・顔文字](#絵文字顔文字) |
| `romaji.txt` | 自分のローマ字の綴り。組み込みの綴りより先に使います |

AZIK を使うときは、見本の [romaji-azik-sample.txt](romaji-azik-sample.txt) を `romaji.txt` という名前で置きます。AZIK で短く打った語 (`kzji` → かんじ) は英語と判定されやすいので、英字になったら <kbd>F6</kbd> で日本語にしてください。

<br>

<a name="かな入力とほかの配列-α版"></a>
<img src="images/headings/usage/07.svg" alt="かな入力とほかの配列 (α版)" height="53"><br>

かな入力と、ローマ字以外の配列は試験中です。おかしな判定があったら、打ったかなと結果を Issue で教えてください。

<a name="かな入力-jis"></a>
<img src="images/headings/usage/s24.svg" alt="かな入力 (JIS)" height="40">

設定「入力方式」を「かな入力 (JIS)」にすると、JIS かな配列で打てます。<kbd>Shift</kbd> + <kbd>E</kbd> で ぃ、<kbd>Shift</kbd> + <kbd>Z</kbd> で っ、濁点・半濁点は直前のかなに付きます。

かな入力でも、英語と日本語を判定します。英単語をキーの英字で打つと (`google` のキー → きららきりい)、英字になります。

<a name="ほかのかな配列"></a>
<img src="images/headings/usage/s25.svg" alt="ほかのかな配列" height="40">

Windows の Meltype キーボードでは、設定「かな配列」で次の配列も選べます。JIS かな以外では、英単語の判定はしません。

| かな配列 | 打ち方 |
| --- | --- |
| 月配列 (U9) | <kbd>D</kbd>・<kbd>K</kbd> を押してから次のキーで 2 段目、<kbd>S</kbd>・<kbd>L</kbd> を押してから次のキーで 3 段目 |
| 花配列 | <kbd>D</kbd>・<kbd>K</kbd> を押してから次のキーで 2 段目 |
| NICOLA | 親指シフト。<kbd>無変換</kbd> が左、<kbd>変換</kbd> が右の親指キーです |
| 中指NICOLA | NICOLA に加えて、<kbd>K</kbd> を押したまま左手のキーを押すと左シフト、<kbd>D</kbd> を押したまま右手のキーを押すと右シフトです。<kbd>D</kbd>・<kbd>K</kbd> を先に離すと、ふつうの文字になります |
| TRON 配列 | 親指シフト。<kbd>無変換</kbd> が左、<kbd>変換</kbd> が右の親指キーです |

- 親指シフトは、親指キーを押したまま文字キーを押すか、文字キーを押してから 0.1 秒以内に親指キーを押します。親指キーだけを押して離すと、ふつうの <kbd>無変換</kbd> / <kbd>変換</kbd> です
- <kbd>Shift</kbd> を押しながら打つと、ふつうの英字と記号です

<a name="sky-配列"></a>
<img src="images/headings/usage/s26.svg" alt="SKY 配列" height="40">

設定「ローマ字の配列」を「SKY 配列」にすると、左手で子音、右手で母音・二重母音・撥音を打てます。拗音は 子音 + <kbd>Y</kbd> + 母音、促音は子音を 2 回です。SKY 配列では、英単語の判定はしません。

<br>

<a name="ほかの-ime-から移るとき"></a>
<img src="images/headings/usage/08.svg" alt="ほかの IME から移るとき" height="53"><br>

<a name="microsoft-ime-と同じキーにする"></a>
<img src="images/headings/usage/s27.svg" alt="Microsoft IME と同じキーにする" height="40">

Meltype の <kbd>Ctrl</kbd> + 英字の割り当ては、Microsoft IME と少し違います。設定「Control キーの個別の割り当て」に次のとおり書くと、Microsoft IME と同じになります。

```
U=F6;I=F7;O=F8;P=F9;T=F10
```

F キーが打ちにくいノートパソコンでは、ここで好きな <kbd>Ctrl</kbd> + 英字に F6〜F10 などを割り当てられます。使えるキー名は、設定の説明にあります。

主に英語を打つ使い方は [自動判定を使わない](#自動判定を使わない)、辞書の移し方は [ユーザー辞書](#ユーザー辞書) にあります。

<a name="atok-のキーに近づける"></a>
<img src="images/headings/usage/s28.svg" alt="ATOK のキーに近づける" height="40">

ATOK と同じく、<kbd>無変換</kbd> で英数、<kbd>変換</kbd> で日本語入力になります。文節の区切りは <kbd>Shift</kbd> + <kbd>←</kbd> <kbd>→</kbd> で変えます。

打っている途中のキーも ATOK に近づけるときは、設定「入力中のキーの役割を分ける」を ON にします。

| キー | 打っている途中の動き |
| --- | --- |
| <kbd>Space</kbd> | 英単語で終わっていても、候補を出します |
| <kbd>変換</kbd> | 日本語として変換します |
| <kbd>無変換</kbd> | 英字にします |
| <kbd>ひらがな/カタカナ</kbd> (Mac は <kbd>かな</kbd>) | ひらがな ⇔ カタカナ を切り替えます |

<br>

<a name="データと学習"></a>
<img src="images/headings/usage/09.svg" alt="データと学習" height="53"><br>

設定と学習した内容は、PC の中の次のフォルダーに保存します。

| OS | データのフォルダー |
| --- | --- |
| Windows | `%LOCALAPPDATA%\Meltype` |
| Mac | `~/Library/Application Support/Meltype` |
| Linux | `~/.local/share/Meltype` |

<a name="学習を消す"></a>
<img src="images/headings/usage/s29.svg" alt="学習を消す" height="40">

Windows では、タスクトレイの「学習データを消す」で消せます。ユーザー辞書・定型文・設定は残ります。

- 「この 1 時間に覚えたもの」「今日覚えたもの」: その間に覚えたものだけを消します
- 「すべて (リセット)」: 学習をすべて消します
- 1 語ずつ消すときは、タスクトレイの「学習した語...」を使います

Mozc も自分の学習を `mozc` フォルダーに持っていて、「学習データを消す」では消えません。Meltype を終了してから `mozc` フォルダーを消してください。

学習させたくないときは、設定「ユーザー学習を使う」を OFF にします。アプリが学習を止めるよう知らせた入力欄では、設定にかかわらず学習しません。

<a name="フォルダーの中身"></a>
<img src="images/headings/usage/s30.svg" alt="フォルダーの中身" height="40">

| ファイル | 中身 |
| --- | --- |
| `config.json` | 設定 |
| `config.schema.json` | 設定の項目の一覧と説明。Mac・Linux で `config.json` を直接書くときに見ます |
| `userdict.txt` | ユーザー辞書 |
| `snippets.json` | 定型文 |
| `conversions.json` | 選び直した変換 (5000 件まで) |
| `languages.json` | 英字・かなに直して確定した語 (3000 語まで) |
| `phrases.txt` | 予測変換のために覚えた語句 (3000 件まで) |
| `translations.json` | 候補から選んだ英訳 |
| `dictionaries` | [自分で足した辞書](#辞書のファイルに足す) |
| `meltype.log` | 設定「ファイルにログを書く」を ON にしたときのログ |
| `crash.log` | Meltype が異常終了したときの記録 |
| `update\install.log` | Windows の自動更新の記録。利用者名・コンピューター名は伏せてあります |
