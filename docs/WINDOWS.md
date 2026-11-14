<p align="center">
  <img src="images/logo.png" alt="Meltype" width="360">
</p>

<a name="meltype-for-windows"></a>
<p align="center"><img src="images/headings/windows/title.svg" alt="Meltype for Windows" height="80"></p>

<p align="center"><b>雪解けのように、半角/全角の壁を溶かす日本語入力。</b></p>

<p align="center">
  <a href="https://github.com/yksr-melt/Meltype/releases/latest"><img src="https://img.shields.io/github/v/release/yksr-melt/Meltype?color=5ec4f0" alt="release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011%20(64bit)-5ec4f0?logo=windows&logoColor=white" alt="Windows 10 / 11">
  <img src="https://img.shields.io/badge/.NET-同梱-ff8ab4" alt=".NET は同梱">
</p>

<p align="center">
  <a href="https://github.com/yksr-melt/Meltype/releases/latest"><b>ダウンロード</b></a>
  &nbsp;·&nbsp;
  <a href="USAGE.md">使い方</a>
  &nbsp;·&nbsp;
  <a href="../README.md">トップの README</a>
  &nbsp;·&nbsp;
  <a href="https://github.com/yksr-melt/Meltype/issues/new/choose">不具合の報告・提案</a>
</p>

<p align="center">
  <a href="WINDOWS.md"><img src="images/cards/os-windows.svg" alt="Windows" width="30%"></a>
  <a href="MACOS.md"><img src="images/cards/os-mac.svg" alt="Mac" width="30%"></a>
  <a href="LINUX.md"><img src="images/cards/os-linux.svg" alt="Linux" width="30%"></a>
</p>

Windows 版のインストール・設定・困ったときの説明です。キー操作と、どの OS でも共通の機能は [USAGE.md](USAGE.md) にあります。

<br>

<a name="動く環境"></a>
<img src="images/headings/windows/01.svg" alt="動く環境" height="53"><br>

- Windows 10 / 11 (64bit)

.NET は同梱しているので、別にインストールする必要はありません。

<br>

<a name="インストール"></a>
<img src="images/headings/windows/02.svg" alt="インストール" height="53"><br>

1. [Releases](https://github.com/yksr-melt/Meltype/releases/latest) から `Meltype-<version>-setup.exe` をダウンロード
2. 開いて、案内どおりに進める
3. タスクトレイに「あ」のアイコンが出たら準備完了！ Windows の起動時にも自動で起動します

新しい版は自動で入ります。

- 「Windows によって PC が保護されました」と出たら、「詳細情報」→「実行」。コード署名をしていないため表示されます
- zip 版は `Meltype-<version>-windows.zip` を展開して `Install.cmd` をダブルクリックします。インストーラーでも zip でも、入る場所は同じ `%LOCALAPPDATA%\Programs\Meltype` です
- インストールせずに使うときは、ポータブル版 `Meltype-<version>-windows-portable.zip` をフォルダーごと展開して `Meltype.exe` を実行します。設定と学習データは `Meltype.exe` の隣の `data` に入ります。Meltype IME・自動起動・自動更新は使えず、Meltype キーボードで動きます
- 「Meltype を終了できませんでした」と出たら、タスクトレイの Meltype のアイコンを右クリック →「終了」してから、もう一度インストールしてください

<a name="meltype-ime"></a>
<img src="images/headings/windows/s01.svg" alt="Meltype IME" height="40">

インストールの途中で、Meltype IME を Windows に登録するための管理者権限の確認が出ます。
「はい」を選ぶと [Meltype IME](#動作モード) で、「いいえ」を選ぶと Meltype キーボードで動きます。もう一度インストールすると、また聞きます。

<a name="ウイルスを検出しましたと出たとき"></a>
<img src="images/headings/windows/s02.svg" alt="「ウイルスを検出しました」と出たとき" height="40">

SmartScreen の警告とは別のものです。誤検知のこともありますが、検出名だけでは見分けられません。

1. Windows セキュリティ →「ウイルスと脅威の防止」→「保護の更新」で定義を新しくする
2. 公式のリリースからダウンロードし直して、もう一度検査する
3. それでも検出されるときは、保護を切ったりフォルダーを除外したりせずに、「保護の履歴」で検出名を確かめて [Issues](https://github.com/yksr-melt/Meltype/issues) で教えてください (個人名・パス・ダウンロード URL の一時トークンは隠してください)

ファイルが本物か確かめたいときは、リリースのページにある SHA-256 と比べられます。PowerShell なら `Get-FileHash .\Meltype-<version>-windows.zip` です。

<br>

<a name="動作モード"></a>
<img src="images/headings/windows/03.svg" alt="動作モード" height="53"><br>

タスクトレイのアイコンを右クリックして、次の 2 つから選びます。判定・変換・キー操作はどちらも同じです。

| モード | 動き |
| --- | --- |
| **Meltype IME** (既定) | Windows の IME として、打った文字が入力欄にそのまま下線付きで入る。変換の候補は入力位置の下に一覧で出る |
| **Meltype キーボード** (Meltype IME を入れていないときの既定) | カーソルの下に出る変換ボックスで入力する。Enter を押すまで、入力欄には何も入らない |

<a name="meltype-ime-で入力する"></a>
<img src="images/headings/windows/s03.svg" alt="Meltype IME で入力する" height="40">

- 入れると、既定の入力方式が Meltype になります (自分で既定を決めていたときは変えません)。Microsoft IME など、ほかの IME との切り替えは <kbd>Win</kbd> + <kbd>Space</kbd> です
- 日本語の入力方式が 2 つになるので、<kbd>Ctrl</kbd> + <kbd>Shift</kbd> でも入力方式が切り替わります。ゲームなどで困るときは、「設定」→「時刻と言語」→「入力」→「キーボードの詳細設定」→「入力言語のホット キー」→「キー シーケンスの変更」で、「キーボード レイアウトの切り替え」を「割り当てなし」にしてください
- 打っている途中は点線、変換した文節は細い線、選んでいる文節は太い線の下線が付きます
- 設定「カッコを打ったら中にカーソルを移動」が ON のとき、`「」`・`（）`・`()` などの空の括弧を <kbd>Enter</kbd> で確定すると、改行せず括弧の中へ移ります。Windows Terminal ではこの移動は未対応です
- Chrome など一部の入力欄では、空の括弧を <kbd>Space</kbd> で変換し、続けて文字を打って括弧を確定すると、次の文字は閉じ括弧の後ろに入ります。括弧の中に入力するには、先に <kbd>Enter</kbd> で括弧を確定します
- <kbd>Ctrl</kbd> + <kbd>Enter</kbd> は変換中でもアプリに渡るので、チャットの送信などに使えます
- <kbd>半角/全角</kbd> か <kbd>Alt</kbd> + <kbd>&#96;</kbd> で日本語 ⇔ 英数です (設定の「入力モード切り替えキー」で変えられます)。タスクバーの「あ」「A」をクリックしても切り替わります
- タスクトレイの Meltype が動いていない・一時停止中・動作モードが Meltype IME でないときは、Meltype IME を選んでいても英字がそのまま入ります
- 管理者として動いているアプリ・サインインや UAC の画面・パスワードの入力欄・ブラウザーのシークレット / InPrivate ウィンドウ・アプリ別設定で OFF にしたアプリでは何もしません (英字がそのまま入ります)
- ゲーム (ゲームのフォルダーにあるアプリ・Minecraft・アプリ別設定で「ゲーム」にしたアプリ) では英数で始め、英数ではキーを横取りしないので、操作のキーはそのままゲームに届きます。チャットでは <kbd>半角/全角</kbd> で日本語にできます。<kbd>Enter</kbd> で確定するか <kbd>Esc</kbd> で取り消すと英数に戻るので、チャットを閉じたあとも操作のキーはゲームに届きます
- IME の登録は PC 全体です。同じ PC のほかのユーザーも Meltype を使っているときにアンインストールすると、そのユーザーの Meltype IME も使えなくなります (そのユーザーが `Install.cmd` を実行し直せば戻ります)

<a name="今のモード-日本語--英数-の見分け方"></a>
<img src="images/headings/windows/s04.svg" alt="今のモード (日本語 / 英数) の見分け方" height="40">

Meltype IME では、タスクバーの「あ」「A」が今のモードです。設定の「入力モードをカーソルの近くに表示」で、切り替えたとき (と入力欄に入ったとき) にカーソルの近くにも出せます。

Meltype キーボードのときは、タスクバーの IME の表示は常に「A」になります (故障ではありません)。Meltype が Windows の IME を OFF にして、代わりに入力を受け持っているためです。今のモードは、次のどちらかで確かめてください。

- 入力欄に入ったときと <kbd>半角/全角</kbd> を押したとき、カーソルの近くに「あ」(日本語) か「A」(英数) が 1.2 秒出ます。設定の「入力モードをカーソルの近くに表示」で出さないか <kbd>半角/全角</kbd> を押したときだけに、「入力モードの表示の色」で色を変えられます (英数のときだけ目立つ色にすると、英数のまま打ち始めるのを防げます)
- タスクトレイの Meltype のアイコン (青い「あ」= 日本語、青い「A」= 英数、灰色の「A」= 一時停止)。Windows 10 では最初「^」の中に隠れていることがあるので、タスクバーにドラッグしておくと便利です

タスクバーの「A」が紛らわしいときは、Windows の設定で入力インジケーターを非表示にできます (Windows 10: 設定 → 個人用設定 → タスクバー → システム アイコンのオン/オフ →「入力インジケーター」)。

<a name="ほかの-ime-と併用するとき"></a>
<img src="images/headings/windows/s05.svg" alt="ほかの IME と併用するとき" height="40">

Meltype IME では、<kbd>Win</kbd> + <kbd>Space</kbd> でほかの IME に切り替えます。

Meltype キーボードのときは、Windows の IME (Microsoft IME・Google 日本語入力など) を自動で OFF にします。ほかの IME を使いたいときは、<kbd>Ctrl</kbd> + <kbd>半角/全角</kbd> で Meltype を一時停止してください。

<br>

<a name="アプリの種類-コードエディターターミナル"></a>
<img src="images/headings/windows/04.svg" alt="アプリの種類 (コードエディター・ターミナル)" height="53"><br>

アプリ別設定の「種類」を **コード** にしたアプリ (既定で VS Code・Cursor・Visual Studio・JetBrains の IDE・Sublime Text・Notepad++・Windows Terminal・PowerShell・コマンドプロンプトなど) では、基本は英数のまま通し、日本語を書く場所だけ判定します。Meltype IME でも Meltype キーボードでも同じです。

- コードの中はキーをそのままアプリへ通します (補完もそのまま使えます)
- 今の行のキャレットより前がコメント (`//` `#` `--` `/* …` `<!-- …` `REM`) か文字列 (`"…` `'…` `` `… ``) の中なら、日本語を判定します。`#include` `#region` のような指令や `x--` はコメントとみなしません
- コードの行で <kbd>半角/全角</kbd> を押すと、日本語で入力できます (改行しても続きます。もう一度押すか、クリック・矢印でキャレットを別の場所に動かすと戻ります)
- README.md などの文章ファイル (ウィンドウのタイトルが .md .txt など)、チャット・AI への入力欄 (VS Code の Copilot Chat など)、ターミナルで動く AI の入力行 (Claude Code・Gemini CLI・Codex) では、ふつうどおり日本語を判定します

<br>

<a name="変換ボックス"></a>
<img src="images/headings/windows/05.svg" alt="変換ボックス" height="53"><br>

Meltype キーボードで入力するときの変換ボックスの設定と、変換ボックスが出ないときの対処です。

<a name="変換ボックスの見た目と位置"></a>
<img src="images/headings/windows/s06.svg" alt="変換ボックスの見た目と位置" height="40">

トレイの「設定...」→「全般」で変えられます。

| 設定 | 選べるもの |
|---|---|
| 変換ボックスの位置 | **入力位置に重ねる** (既定) / **カーソルの下** / **カーソルの上** |
| 変換ボックスの文字の大きさ | 自動 (入力欄に合わせる、既定) / 小 / 中 / 大 / 特大 / 最大 |
| 変換ボックスのフォント | この PC にあるフォント (既定は Yu Gothic UI) |
| 変換ボックスの色 | **ダーク** (既定) / **ライト** / **Windows の設定に合わせる** |
| 変換ボックスの不透明度 | 100% (既定) / 90% / 80% / 70% |

- 候補の一覧が入力欄や下の行を隠すときは、「変換ボックスの位置」を **カーソルの上** にしてください
- 候補の横に出る意味の枠は「候補の意味を表示」、文字の下に小さく出る打ったキーは「打ったキーを表示」で消せます

<a name="変換ボックスが出ない場面"></a>
<img src="images/headings/windows/s07.svg" alt="変換ボックスが出ない場面" height="40">

- 文字入力欄以外 (Gmail の `j`/`k` のようなショートカット、エクスプローラー、ゲーム)
- パスワード欄
- 管理者権限のアプリ、全画面のアプリ、アプリ別設定で OFF にしたアプリ (既定でリモートデスクトップと VM は OFF)
- ほかのソフトが送ったキー (Windows のタッチキーボード、AnyDesk・VNC などの遠隔操作、ULE4JIS などキー配列を変えるソフト)。設定「遠隔操作などの入力も処理する」を ON にすると処理します。ULE4JIS は Meltype より後に起動してください

出ない理由はトレイの「ログ / 判定理由」に「フォーカス → 入力欄ではない: …」のように残ります。

<a name="文字を打つ所なのに変換ボックスが出ないアプリ"></a>
<img src="images/headings/windows/s08.svg" alt="文字を打つ所なのに変換ボックスが出ないアプリ" height="40">

画面をすべて自分で描くアプリ (Premiere Pro など) では、文字を打つ所でも変換ボックスが出ないことがあります。ログに「フォーカス → 入力欄ではない: …」と出ていたら、次のどちらかで打てます。

- 設定の「アプリ」→「アプリ別設定」にプロセス名 (例: `Adobe Premiere Pro.exe`) の行を足し (「実行中のアプリから追加…」でも足せます)、「入力欄とみなす」を ON にする。そのアプリでは `V` `C` のような 1 文字のショートカットも変換ボックスに入るので、ショートカットを使うときは <kbd>Ctrl</kbd> + <kbd>半角/全角</kbd> で一時停止してください
- アプリ別設定でそのアプリを OFF にして、Windows の IME で打つ

<br>

<a name="日本語キーボードのときだけ動作させる"></a>
<img src="images/headings/windows/06.svg" alt="日本語キーボードのときだけ動作させる" height="53"><br>

設定の「全般」→「日本語キーボードのときだけ動作」を ON にすると、前面の入力欄で日本語のキーボード / IME が選ばれているときだけ動作します (既定は OFF)。
<kbd>Win</kbd> + <kbd>Space</kbd> などで韓国語・英語などに切り替えると Meltype は止まり、日本語に戻すと再開します。判定するのは Windows の入力言語で、物理キーボードの JIS / US 配列ではありません。

<br>

<a name="貼り付けで入力するアプリ"></a>
<img src="images/headings/windows/07.svg" alt="貼り付けで入力するアプリ" height="53"><br>

確定した文字を 1 文字ずつ送ると取り違えるアプリ (DaVinci Resolve で「あいうえお」→「あああああ」) では、クリップボードを使って貼り付けで入れます。元のクリップボードの中身は 0.5 秒後に戻します。

- Qt のアプリ (LINE・OBS Studio・Telegram Desktop・DaVinci Resolve など) は自動で貼り付けになります
- ほかのアプリは、設定の「アプリ」→「アプリ別設定」で「確定した文字の入れ方」を「貼り付け」にします
- 貼り付けにしたくないアプリは、「確定した文字の入れ方」を「1 文字ずつ送る」にします

<br>

<a name="zenzai-ニューラルかな漢字変換"></a>
<img src="images/headings/windows/08.svg" alt="Zenzai (ニューラルかな漢字変換)" height="53"><br>

[Zenzai](https://github.com/ensan-hcl/azooKey) (azooKey の zenz モデル) で、前の文脈を見てかな漢字変換します。**既定で ON** で、llama.cpp の `llama-server.exe` (b11719 の CPU 版) とモデル [zenz-v3.2-small](https://huggingface.co/Miwa-Keita/zenz-v3.2-small-gguf) (Apache-2.0) を Meltype に同梱しています (`app\zenzai\`)。

- Meltype が llama-server を 127.0.0.1 で起動します。使えないときは、Mozc で変換します
- Zenzai の答えを出しているときは、変換ボックスの下の説明の行の頭に「Zenzai」と出ます。Zenzai の状態はトレイのメニューに出ます
- CPU の負荷が気になるときは、設定の「Zenzai を使う」を OFF にしてください
- 自分で用意した llama-server や zenz v3 系のモデルを使うときは、「Zenzai: llama-server の場所」「Zenzai: モデル (GGUF) の場所」に書きます (空なら同梱のもの)。GPU 版 (Vulkan・CUDA) の llama-server なら、「Zenzai: 使う GPU」に `llama-server --list-devices` で出る名前 (例: `Vulkan0`) を書くと GPU で計算します

<br>

<a name="タスクトレイと設定"></a>
<img src="images/headings/windows/09.svg" alt="タスクトレイと設定" height="53"><br>

- <kbd>Ctrl</kbd> + <kbd>半角/全角</kbd>、アイコンのダブルクリック、または <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>F12</kbd> で一時停止 / 再開します (<kbd>F12</kbd> がほかのアプリと重なっていたら <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>F11</kbd> → <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Alt</kbd> + <kbd>A</kbd> → <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Pause</kbd> の順に使います)
- 右クリックで、動作モード・設定・ユーザー辞書・ログ / 判定理由・データフォルダ・学習データを消す・アンインストールなどを選べます
- 設定画面の項目にマウスを乗せると、下に説明が出ます。「判定テスト」欄に英字を打つと、日本語か英語かの判定結果と理由が出ます
- **トレイのメニューが出ないとき**: Windows の入力インジケーター (「あ」「A」) と Meltype のアイコンは別のものです。Meltype のアイコンは、タスクバーの右の「^」の中に入っていることがあります。それでも出ないときは、Meltype を終了してから `%LOCALAPPDATA%\Meltype` の `config.json` (設定) と `userdict.txt` (ユーザー辞書) を直接編集できます
- **プロファイル**: 仕事用・趣味用など、設定の値をまとめて切り替えられます。設定画面の上で作り、トレイの「プロファイル」で切り替えます。「書き出す...」でファイルにして人に渡し、相手は「読み込む...」で足せます
- **更新の前に変わる語を見せる**: 自動更新の前に、自分で英字 / かなに直したことのある語の見え方が新しい版で変わるなら、一覧を出します。「今のまま覚えて更新」で今の見え方を覚えてから更新し、「あとで」なら今回は更新しません
- **プレリリース版の自動更新**: 設定の「更新」→「プレリリース版も自動で更新する」を ON にすると、正式版の前に公開するプレリリース版 (`1.3.1-pre.4` など) にも更新します。不具合が含まれることがあります
- **リリースノート**: 更新したあとの最初の起動で、その版のリリースノートを一度だけ出します。あとから見るときは、トレイの「更新」→「リリースノート...」

<br>

<a name="おかしな動きがあったら"></a>
<img src="images/headings/windows/10.svg" alt="おかしな動きがあったら" height="53"><br>

Meltype を更新した後、前から開いていたアプリで日本語を入力できない (英字のまま入る) ときは、そのアプリが前の版の Meltype IME を読み込んだままです。そのアプリか PC を再起動してください (Meltype が見つけたときは、通知と、打っている所の表示でも知らせます)。

漢字に変換されず、ひらがなのまま確定するときは、Meltype が同梱の Mozc (`app\mozc\meltype_mozc_helper.exe`) を起動できていません。ログ (右クリック →「ログ / 判定理由...」) に「Mozc の変換ヘルパーを起動できません」か「Mozc を使えませんでした」と出ていれば、Meltype を入れ直してください。会社の PC でアプリの実行が制限されているときは、管理者に `meltype_mozc_helper.exe` の実行を許可してもらってください。

タスクトレイのアイコンを右クリック →「不具合の報告・提案...」から報告できます。次の 3 つを書いてもらえると助かります！

1. どのアプリで (メモ帳 / Chrome / Discord など)
2. 何と打って (例: `kyouhagoogle` と打って Enter)
3. どうなったか (例: 「今日はごおｇぇ」になった)

あわせて、右クリック →「ログ / 判定理由...」→「コピー」で、ログを貼り付けてもらえるとさらに助かります。ログには直前に打った文字の一部が含まれるので、気になる部分は消してから送ってください。
Meltype IME の不具合のときは、`%LOCALAPPDATA%\Meltype` に `tip-log` という名前の空のファイルを作ると、`tip.log` に Meltype IME の動きが残ります。

<br>

<a name="保存されるデータ"></a>
<img src="images/headings/windows/11.svg" alt="保存されるデータ" height="53"><br>

設定と学習データは `%LOCALAPPDATA%\Meltype` に保存されます。ファイルの一覧と消し方は [USAGE.md の「データと辞書」](USAGE.md#データと辞書)、通信については [README の「プライバシー」](../README.md#プライバシー) にあります。

<br>

<a name="制限"></a>
<img src="images/headings/windows/12.svg" alt="制限" height="53"><br>

- カーソルの前後の文字を読めないアプリでは、Meltype が最後に確定した文字列で代用します。Chrome などでは、カーソルの前後を読むために動作が少し重くなることがあります
- 32bit Windows には対応していません

<br>

<a name="アンインストール"></a>
<img src="images/headings/windows/13.svg" alt="アンインストール" height="53"><br>

タスクトレイの Meltype のアイコンを右クリック →「アンインストール...」を選びます。Windows の「設定」→「アプリ」から消すこともできます。

- インストーラーで入れた場合は、最後に設定と学習データも消すかを聞かれます
- zip 版は、展開したフォルダーの `Uninstall.cmd` でも消せます。こちらは設定と学習データもまとめて消します
