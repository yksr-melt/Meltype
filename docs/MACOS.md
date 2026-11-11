<p align="center">
  <img src="images/logo.png" alt="Meltype" width="360">
</p>

<a name="meltype-for-mac"></a>
<p align="center"><img src="images/headings/mac/title.svg" alt="Meltype for Mac" height="80"></p>

<p align="center"><b>雪解けのように、半角/全角の壁を溶かす日本語入力。</b></p>

<p align="center">
  <a href="https://github.com/yksr-melt/Meltype/releases/latest"><img src="https://img.shields.io/github/v/release/yksr-melt/Meltype?color=5ec4f0" alt="release"></a>
  <img src="https://img.shields.io/badge/macOS-13%2B-ff8ab4?logo=apple&logoColor=white" alt="macOS 13 以降">
  <img src="https://img.shields.io/badge/プレビュー版-7a96f0" alt="プレビュー版">
</p>

<p align="center">
  <a href="https://github.com/yksr-melt/Meltype/releases/latest"><b>ダウンロード</b></a>
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

Mac 版のインストール・設定・困ったときの説明です。キー操作と、どの OS でも共通の機能は [USAGE.md](USAGE.md) にあります。

> **プレビュー版です。** 動かないところやビルドエラーがあれば、エラーの全文とあわせて [Issues](https://github.com/yksr-melt/Meltype/issues) で教えてください！

<br>

<a name="動く環境"></a>
<img src="images/headings/mac/01.svg" alt="動く環境" height="53"><br>

- macOS 13 以降
- 配布している zip は Apple シリコン (M1 / M2 / M3 / M4 …) の Mac 用です。Intel の Mac では、[ソースからビルド](../mac/README.md#ビルドする)してください (ビルドした CPU 向けのアプリができるので、Apple シリコン版と Intel 版は互換ではありません)

<br>

<a name="インストール"></a>
<img src="images/headings/mac/02.svg" alt="インストール" height="53"><br>

Meltype はアプリではなく「入力ソース」(日本語入力) です。`Meltype.app` をダブルクリックしても何も起きません。

1. [Releases](https://github.com/yksr-melt/Meltype/releases/latest) から `Meltype-<version>-mac.zip` をダウンロードして、**zip 全体を**展開する
2. 「Install Meltype.command」をダブルクリック
   - 「開発元が未確認のため開けません」と出たら、右クリック (control + クリック) →「開く」→「開く」
   - macOS 15 以降は、一度ダブルクリックしてから、システム設定 → プライバシーとセキュリティ → 下のほうの「このまま開く」を押す
   - それでも開けないときは、「ターミナル」で `bash ~/Downloads/Meltype-mac/install.sh` を実行する
3. インストーラーが Meltype を登録・起動します。初回は Meltype の選択を試み、配布 ZIP の更新時は更新前の入力ソースへ戻します

起動エラーが出たときは、入力メニューから ABC または標準の日本語入力を選んでください。入力メニューに Meltype が出てこないときは、[困ったとき](#困ったとき)を見てください。

配布 ZIP からの更新も同じ手順です。`Meltype.app` を先に削除したり、手で上書きしたりせず、新しい ZIP 全体を展開してインストーラーを実行してください。ソースビルドからの更新も、外側のアプリを残して内容を交換します。更新時は入力メニューを再起動せず、更新前の入力ソースへ戻します。

Homebrew から入れる場合は、最初に tap を追加してからインストールします。以後は `brew upgrade --cask meltype` で更新できます。

```bash
brew tap yksr-melt/Meltype https://github.com/yksr-melt/Meltype.git
brew install --cask yksr-melt/Meltype/meltype
```

<br>

<a name="使い方"></a>
<img src="images/headings/mac/03.svg" alt="使い方" height="53"><br>

入力メニューの「日本語・英語の自動判定」で使います。JIS キーボードの <kbd>英数</kbd> / <kbd>かな</kbd> で、英数 (直接入力) / 日本語に切り替えます。

設定・学習データ・ユーザー辞書は `~/Library/Application Support/Meltype` にあります (入力メニューの「Meltype のデータフォルダを開く」)。設定は Windows 版と同じ `config.json` で、入力メニューの「Meltype の設定ファイル (config.json) を開く…」で開いて直接編集します。VS Code などのエディターでは、項目の補完と説明が出ます。

かな漢字変換では、azooKey に組み込まれた Zenzai (zenz-v3.2-small、同梱) で、キャレットの前の文 (20 文字まで) を文脈にして変換します (既定で ON。CPU で計算します。80 文字より長い読みは Zenzai を使いません)。使わないときは `config.json` の `"UseZenzai": false`、自分のモデルを使うときは `"ZenzaiModelPath"` に GGUF の場所を書きます。変えたら Meltype を起動し直します。

<a name="入力モードのショートカット"></a>
<img src="images/headings/mac/s06.svg" alt="入力モードのショートカット" height="40">

入力メニューのモード (日本語・英語の自動判定 / 英数の直接入力) は、macOS の「サービス」にキーを割り当てて切り替えられます。最初はキーを割り当てていません。

1. Meltype をインストールした後、いったんログアウトしてログインし直す (サービスの一覧に Meltype の項目が出るようになります)
2. **システム設定 → キーボード → キーボードショートカット → サービス → 一般** を開く
3. 次の項目を有効にして、ショートカット欄をダブルクリックしてキーを割り当てる

| 操作 | サービス名 |
| --- | --- |
| 自動判定 | `Meltype：日本語・英語の自動判定` |
| 英数の直接入力 | `Meltype：英数の直接入力` |
| 日本語 ⇔ 英数 | `Meltype：日本語 ⇔ 英数` |

- Meltype が入力ソースに選ばれている間に使えます。前面のアプリによっては、ショートカットが届かないことや、アプリのショートカットとぶつかることがあります
- 割り当てが反映されないときは、アプリを開き直すか、ログアウトしてログインし直してください
- 切り替えると、入力カーソルの下 (位置が分からないときは画面の中央) に「あ」か「A」を約 1 秒出します。JIS の 英数 / かな キーで切り替えたときも出ます。出すかは設定 `ModeIndicator` で決めます (Windows 版と同じ。入力欄に入ったときも出すには `OnSwitchAndFocus`)
- VS Code・ターミナルなど「コード」のアプリでは、コメント・文字列の外は英数のまま入ります (Windows 版と同じアプリ別設定。既定で主なエディター・ターミナルが入っています)

<a name="ユーザー辞書"></a>
<img src="images/headings/mac/s02.svg" alt="ユーザー辞書" height="40">

ユーザー辞書は、データフォルダ (`~/Library/Application Support/Meltype`) の `userdict.txt` に、1 行に「読み<Tab>単語」(UTF-8、読みはひらがな) で書きます。変換では最優先に使います。1 文字の読みは、その 1 文字だけを変換するときだけ使います。

macOS の「ユーザ辞書」の語は、次の手順で `userdict.txt` に取り込めます。

1. システム設定 → キーボード →「テキスト置換…」で、使いたい項目を選んで Finder にドラッグする (`ユーザ辞書.plist` ができます)
2. 入力メニューの「macOSのユーザー辞書を取り込む…」で、そのファイルを選ぶ

- 登録済みの語は残し、まったく同じ語は飛ばします。取り込んだ語はすぐに変換に使います
- 空の読みと、タブや改行を含む語は取り込みません

<a name="windows-版との違い"></a>
<img src="images/headings/mac/s03.svg" alt="Windows 版との違い (今のところ)" height="40">

- 設定画面・トレイ・ユーザー辞書の画面はありません (`config.json`・`userdict.txt` を直接編集。[ユーザー辞書](#ユーザー辞書))
- 設定「カッコを打ったら中にカーソルを移動」は未対応です

<br>

<a name="困ったとき"></a>
<img src="images/headings/mac/04.svg" alt="困ったとき" height="53"><br>

- **入力ソースに出てこない**: まず入力メニューを見る。無ければ、ターミナルで次を実行する
  ```bash
  ~/Library/Input\ Methods/Meltype.app/Contents/MacOS/MeltypeRegisterInputSource
  ```
  これで戻らなければ、システム設定 → キーボード →（テキスト入力の）入力ソース →「編集…」→「+」→ 日本語 → Meltype で追加する (macOS 27 では、この手順が必要になることがあります)。それでも出てこなければ、ログアウトしてログインし直し、`~/Library/Input Methods/Meltype.app` があるか確かめる。
- **Meltype がいくつも並ぶ**: ターミナルで次を実行する
  ```bash
  "$HOME/Library/Input Methods/Meltype.app/Contents/MacOS/Meltype" --register-input-source
  ```
  それでも並んだままなら、システム設定 → キーボード → 入力ソース で Meltype を追加し直すか、ログアウトしてログインし直す。
- **入力メニューに「絵文字と記号を表示」「キーボードビューアを表示」「キーボード設定を開く…」の 3 つしか出ない**: 上の `MeltypeRegisterInputSource` を実行するか、ターミナルで `killall TextInputMenuAgent` を実行する。入力ソースが 1 つしか無いときも、この見え方になります
- **動きがおかしい**: ログを見る
  ```bash
  log stream --predicate 'process == "Meltype"' --level debug
  ```
- **止まってしまった**: まず入力メニューで ABC または標準の日本語入力を選び、`pkill -x Meltype` (次にキーを打つと macOS が起動し直します)。zip を展開したフォルダーで次を実行しても再起動できます
  ```bash
  bash ./start-input-method.sh "$HOME/Library/Input Methods/Meltype.app"
  swift ./select-input-source.swift
  ```

不具合を報告するときは、どのアプリで・何と打って・どうなったか (できればスクリーンショットも) を教えてください。

<br>

<a name="アンインストール"></a>
<img src="images/headings/mac/05.svg" alt="アンインストール" height="53"><br>

入力ソースから Meltype を外してから、ターミナルで次を実行します。

```bash
launchctl remove io.github.yksr-melt.Meltype.manual
rm -rf ~/Library/Input\ Methods/Meltype.app
# 設定と学習データも消すなら
rm -rf ~/Library/Application\ Support/Meltype
```
