<a name="meltype-の使い方"></a>
<img src="images/headings/usage/title.svg" alt="Meltype の使い方" height="80">

英語も日本語も、切り替えずにローマ字で打つだけです。

```
kyouhagoogledekensaku  →  今日はgoogleで検索
```

インストールと OS ごとの設定は [WINDOWS.md](WINDOWS.md)・[MACOS.md](MACOS.md)・[LINUX.md](LINUX.md) にあります。

<br>

<a name="打ち方の基本"></a>
<img src="images/headings/usage/01.svg" alt="打ち方の基本" height="53"><br>

ローマ字で打って、<kbd>Space</kbd> で変換、<kbd>Enter</kbd> で確定します。英単語の後の <kbd>Space</kbd> は、ふつうの空白です。

| したいこと | キー |
| --- | --- |
| 次の候補 / 前の候補 | <kbd>Space</kbd> / <kbd>Shift</kbd> + <kbd>Space</kbd> |
| 予測変換 | <kbd>Tab</kbd> |
| 変換する文節を選ぶ | <kbd>←</kbd> <kbd>→</kbd> |
| 文節の区切りを変える | <kbd>Shift</kbd> + <kbd>←</kbd> <kbd>→</kbd> |
| ひらがな / カタカナ / 半角英字にする | <kbd>F6</kbd> / <kbd>F7</kbd> / <kbd>F10</kbd> |
| 取り消す | <kbd>Esc</kbd> |

ローマ字の綴りは Microsoft IME と同じです。

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
| <kbd>半角/全角</kbd> | 日本語入力 ⇔ 英数 |
| <kbd>英数</kbd> / <kbd>無変換</kbd> | 英数 |
| <kbd>かな</kbd> / <kbd>変換</kbd> | 日本語入力 |

コードエディターとターミナルでは、コメントと文字列の中だけ日本語になります。ゲームでは英数で始まります。アプリごとの動きは、設定の「アプリ別設定」で変えられます。

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
| ばーじょん | Meltype の版 (不具合の報告に貼ってください) |

<br>

<a name="自分の語を足す"></a>
<img src="images/headings/usage/06.svg" alt="自分の語を足す" height="53"><br>

**ユーザー辞書**: Windows はタスクトレイの「ユーザー辞書...」で登録します。Microsoft IME・Google 日本語入力の辞書も取り込めます。Mac は [MACOS.md](MACOS.md#ユーザー辞書) を見てください。

**定型文**: Windows はタスクトレイの「定型文...」で登録して、`;名前` と打って <kbd>Space</kbd> で呼び出します。

<br>

<a name="かな入力とほかの配列-α版"></a>
<img src="images/headings/usage/07.svg" alt="かな入力とほかの配列 (α版)" height="53"><br>

設定「入力方式」「かな配列」で、JIS かな・月配列・花配列・NICOLA・中指NICOLA・TRON 配列・SKY 配列を選べます。試験中です。

<br>

<a name="ほかの-ime-から移るとき"></a>
<img src="images/headings/usage/08.svg" alt="ほかの IME から移るとき" height="53"><br>

<kbd>Ctrl</kbd> + 英字を Microsoft IME と同じにするには、設定「Control キーの個別の割り当て」に次のとおり書きます。

```
U=F6;I=F7;O=F8;P=F9;T=F10
```

ATOK のように <kbd>変換</kbd> で日本語、<kbd>無変換</kbd> で英字にするには、設定「入力中のキーの役割を分ける」を ON にします。

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
