<a name="meltype-の使い方"></a>
<img src="images/headings/usage/title.svg" alt="Meltype の使い方" height="80">

英語も日本語も、切り替えずにローマ字で打つだけです。

```
kyouhagoogledekensaku  →  今日はgoogleで検索
```

インストールと OS ごとの設定は [WINDOWS.md](WINDOWS.md)・[MACOS.md](MACOS.md)・[LINUX.md](LINUX.md)、端末版は [TTY.md](TTY.md) にあります。

<br>

<a name="打ち方の基本"></a>
<img src="images/headings/usage/01.svg" alt="打ち方の基本" height="53"><br>

ローマ字で打って、<kbd>Space</kbd> で変換、<kbd>Enter</kbd> で確定します。英単語で終わっているときの <kbd>Space</kbd> はふつうの空白になるので、変換するには <kbd>Shift</kbd> + <kbd>Space</kbd> を押します (`kyouhagoogle` のように英単語で終わる文)。

| したいこと | キー |
| --- | --- |
| 変換中に次の候補 / 前の候補 | <kbd>Space</kbd> / <kbd>Shift</kbd> + <kbd>Space</kbd> |
| 予測変換 | <kbd>Tab</kbd> |
| 変換する文節を選ぶ | <kbd>←</kbd> <kbd>→</kbd> |
| 文節の区切りを変える | <kbd>Shift</kbd> + <kbd>←</kbd> <kbd>→</kbd> |
| ひらがな / カタカナ / 半角英字にする | <kbd>F6</kbd> / <kbd>F7</kbd> / <kbd>F10</kbd> |
| 取り消す | <kbd>Esc</kbd> |

ローマ字の綴りは Microsoft IME と同じです。

<br>

<a name="設定の開き方"></a>
<img src="images/headings/usage/10.svg" alt="設定の開き方" height="53"><br>

| OS | 開き方 |
| --- | --- |
| Windows | タスクバーの右にある青い丸の「あ」を右クリック →「設定...」。英数のときは「A」、一時停止中は灰色の丸になります。見当たらなければ「^」の中にあります |
| Mac | メニューバーの入力メニュー →「Meltype の設定ファイル (config.json) を開く…」 |
| Linux | アプリの一覧の「Meltype の設定」 |

丸の無い「あ」「A」と Meltype のロゴは Windows の入力モードの表示で、右クリックしても Meltype のメニューは出ません。ユーザー辞書・定型文・学習データを消すメニューも、青い丸の「あ」の右クリックにあります。

<br>

<a name="英語と日本語の判定"></a>
<img src="images/headings/usage/02.svg" alt="英語と日本語の判定" height="53"><br>

英単語は英字のまま、日本語はかなと漢字になります。

判定が外れたら、打っている途中に <kbd>半角/全角</kbd> を押すと 日本語 ⇔ 英字 が切り替わります。直した語は次から覚えています。

確定した後でも、続きを打つと直ることがあります (`i` → `want` と打つと「I want」)。

`/review`・`$skill`・`@src/app.ts` のように `/` `$` `@` で始まる語は、そのまま入ります。

<a name="自動判定を使わない"></a>
<img src="images/headings/usage/s01.svg" alt="自動判定を使わない" height="40">

主に英語を打つなら、設定「英字を主に入力」を ON にします。打った英字はそのまま入り、日本語にしたい語だけ <kbd>変換</kbd> か <kbd>↓</kbd> で変換します。

<br>

<a name="日本語入力と英数"></a>
<img src="images/headings/usage/03.svg" alt="日本語入力と英数" height="53"><br>

ふだんは日本語入力のままで大丈夫です。英数は、打った文字をそのまま入れたいときに使います。

| キー | 切り替え |
| --- | --- |
| <kbd>半角/全角</kbd> / <kbd>Alt</kbd> + <kbd>&#96;</kbd> | 日本語入力 ⇔ 英数 |
| <kbd>英数</kbd> / <kbd>無変換</kbd> | 英数 |
| <kbd>かな</kbd> / <kbd>変換</kbd> | 日本語入力 |

設定「左 Alt で英数 / 右 Alt で日本語」を ON にすると、左右の <kbd>Alt</kbd> だけを押して切り替えられます (Windows・Linux)。

アプリごとに、動きを 4 つのモードから選べます。設定の「アプリ別設定」の「種類」で変えられます。

| モード | 動き | 最初からこのモードのアプリ |
| --- | --- | --- |
| 一般 | この文書のとおりに動きます | ほかのアプリ |
| コード | コメントと文字列の中だけ日本語になります | コードエディター・ターミナル |
| ゲーム | 英数で始まります | ゲーム |
| OFF | 何もしません | リモートデスクトップ・仮想マシン |

<br>

<a name="変換"></a>
<img src="images/headings/usage/04.svg" alt="変換" height="53"><br>

選び直した候補は、次から最初に出ます。

書き間違いには「もしかして」が出ます。<kbd>Tab</kbd> で直ります (ぶれすれっど → ブレスレット)。

句読点 (、。 / ，．) と、英単語の前後の空白は、設定の「全般」で変えられます。

<br>

<a name="読みで出せる特別な候補"></a>
<img src="images/headings/usage/05.svg" alt="読みで出せる特別な候補" height="53"><br>

次の読みだけを打って変換すると出ます。

| 読み | 候補 |
| --- | --- |
| いま | 17:22 |
| きょう | 2026年10月9日 |
| なう | 再生中の曲 (Windows) |
| 100どる | 約1.6万円 |
| えがお | 😊 |
| かおもじ | 顔文字 |
| ごんべんにふるとり | 誰 |
| ばーじょん | Meltype の版と、使っている入力方式・Zenzai の設定 (不具合の報告に貼ってください) |

<br>

<a name="自分の語を足す"></a>
<img src="images/headings/usage/06.svg" alt="自分の語を足す" height="53"><br>

**ユーザー辞書**: Windows はタスクトレイの「ユーザー辞書...」で登録します。Microsoft IME・Google 日本語入力の辞書も取り込めます。Mac は [MACOS.md](MACOS.md#ユーザー辞書) を見てください。

**定型文**: Windows はタスクトレイの「定型文...」で登録して、`;名前` と打って <kbd>Space</kbd> で呼び出します。

<br>

<a name="かな入力とほかの配列-α版"></a>
<img src="images/headings/usage/07.svg" alt="かな入力とほかの配列 (α版)" height="53"><br>

設定の「入力方式」で、ローマ字入力かかな入力を選びます。ローマ字入力以外はどれも試験中です。

| 入力方式 | 選べる配列 | 使える場所 |
| --- | --- | --- |
| ローマ字入力 (標準) | 設定「ローマ字の配列」: QWERTY (標準)・SKY 配列 | QWERTY はすべての OS。SKY 配列は Windows の Meltype キーボード |
| かな入力 | 設定「かな配列」: JIS かな (標準)・月配列・花配列・NICOLA (親指シフト)・中指NICOLA・TRON 配列 | JIS かなはすべての OS。ほかの配列は Windows の Meltype キーボード |

- **月配列・花配列**: <kbd>D</kbd> か <kbd>K</kbd> を押してから次のキーを押すと、2 段目 (月配列は 3 段目も) の文字になります。
- **NICOLA・TRON 配列**: 左の親指キーは <kbd>無変換</kbd>、右は <kbd>変換</kbd> です。親指キーを押したまま文字キーを押すか、文字キーの直後に親指キーを押します。親指キーだけを押して離すと、ふつうの変換・無変換になります。
- **中指NICOLA**: NICOLA に加えて、<kbd>K</kbd> を押したまま左手のキーを押して先に離すと左シフト、<kbd>D</kbd> を押したまま右手のキーを押して先に離すと右シフトになります。
- かな入力で <kbd>Shift</kbd> を押したキーは、ふつうの英字・記号になります。

**ローマ字の表**: `%LOCALAPPDATA%\Meltype\dictionaries\romaji.txt` (Mac・Linux はデータフォルダーの `dictionaries/romaji.txt`) に、1 行に `綴り<Tab>かな` を書くと、その綴りで打てます。組み込みの綴りより優先され、Meltype を起動し直すと効きます。AZIK の一部を書いた見本が [romaji-azik-sample.txt](romaji-azik-sample.txt) にあります。

<br>

<a name="ほかの-ime-から移るとき"></a>
<img src="images/headings/usage/08.svg" alt="ほかの IME から移るとき" height="53"><br>

<kbd>Ctrl</kbd> + 英字を Microsoft IME と同じにするには、設定「Control キーの個別の割り当て」に次のとおり書きます。

```
U=F6;I=F7;O=F8;P=F9;T=F10
```

<br>

<a name="データと学習"></a>
<img src="images/headings/usage/09.svg" alt="データと学習" height="53"><br>

設定と学習した語は、この PC の中だけに保存します。

| OS | 場所 |
| --- | --- |
| Windows | `%LOCALAPPDATA%\Meltype` |
| Mac | `~/Library/Application Support/Meltype` |
| Linux | `~/.local/share/Meltype` |

学習した語は、Windows ではタスクトレイの「学習データを消す」で消せます。
