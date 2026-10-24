<a name="mac-版をターミナルからビルドする"></a>
<img src="images/headings/mac-cli/title.svg" alt="Mac 版をターミナルからビルドする" height="80">


Mac 版の入力メソッドを、ソースからビルドして入れる手順です。コマンドはどれも、リポジトリのフォルダー (`Meltype/`) で実行します。

<br>

<a name="1-つのコマンドで更新する"></a>
<img src="images/headings/mac-cli/01.svg" alt="1 つのコマンドで更新する" height="53"><br>


手元のソースで、入れてある Meltype を新しくします。

```bash
bash mac/update.sh
```

次のことを順に行います。

- 本体のテスト (OS に依存しない部分)
- アプリのビルドとインストール
- 同梱の Mac 用の入力のチェック
- Meltype を入力ソースに選び直す

入力のチェックが失敗しても、入力メソッドは元に戻します。Git の変更を取り込んだり、ユーザー辞書を上書きしたりはしません。

<br>

<a name="ビルドしてインストールする"></a>
<img src="images/headings/mac-cli/02.svg" alt="ビルドしてインストールする" height="53"><br>


```bash
mac/build-cli.sh --setup --all
```

`--all` は、本体のテスト → `mac/build/Meltype.app` のビルド → インストールまでをまとめて行います。インストール先は次の場所です。

```text
~/Library/Input Methods/Meltype.app
```

一部だけ行うときは、次のように分けて実行できます。

```bash
mac/build-cli.sh --test      # テストだけ
mac/build-cli.sh --build     # ビルドだけ
mac/build-cli.sh --install   # インストールだけ
```

<br>

<a name="入力ソースに出てこないとき"></a>
<img src="images/headings/mac-cli/03.svg" alt="入力ソースに出てこないとき" height="53"><br>


初めて入れたときは、メニューバーの入力メニューに Meltype があるか確かめてください。
無いときは、次の場所から追加できます (macOS 27 では、アプリからの有効化が通らず、この手順が必要になることがあります)。

```text
システム設定 → キーボード →（テキスト入力の）入力ソース →「編集…」→「+」→ 日本語 → Meltype
```

追加すると、同じログインセッションのまま入力メニューから選べます。それでも出てこないときは、一度ログアウトしてログインし直してください。

<br>

<a name="動作の確認とログ"></a>
<img src="images/headings/mac-cli/04.svg" alt="動作の確認とログ" height="53"><br>


入れたアプリで、オフラインの確認テストを実行できます。

```bash
"$HOME/Library/Input Methods/Meltype.app/Contents/MacOS/Meltype" --version
"$HOME/Library/Input Methods/Meltype.app/Contents/MacOS/Meltype" --self-test
```

同梱の変換エンジンを実際に使い、日本語と英語の混ざった入力・英語・絵文字の候補・綴りの提案・入力ソースの重複の整理・plist の辞書の読み込みを確かめます。アプリの画面の操作は確かめません。

動作中のログは、次のコマンドで見られます。

```bash
log stream --predicate 'process == "Meltype"' --level debug
```

<br>

<a name="入力ソースの重複を直す"></a>
<img src="images/headings/mac-cli/05.svg" alt="入力ソースの重複を直す" height="53"><br>


入力ソースの一覧に Meltype がいくつも並んでしまったときは、次のコマンドで整理できます。

```bash
"$HOME/Library/Input Methods/Meltype.app/Contents/MacOS/Meltype" --register-input-source
```

OS の登録 API で登録・有効化した後、`com.apple.HIToolbox` の `AppleEnabledInputSources` と `com.apple.inputsources` の `AppleEnabledThirdPartyInputSources` を読み、Meltype がある各一覧の中で親と日本語モードの重複を除きます。最初の項目の位置と付随情報、ほかの入力ソースを保持します。OS の版番号で保存先を作ったり、保存先をまたいで登録を移したりはしません。両方に Meltype がある場合は、それぞれの登録を残します。

変更前の一覧は、変更する設定領域ごとに `MeltypeInputSourcesBeforeRepair` に保存します。有効な日本語モードが一件になるまで最大約 2 秒待ち、一件なら入力メニューを更新します。反映待ちの場合は件数と対処方法を表示して終了コード 0 で終了し、入力メニューを再起動しません。設定の形式・保存・登録 API の失敗は終了コード 1 にします。同じコマンドを繰り返しても一覧内の重複は増えません。保存先をまたぐ登録の統合は行わないため、実メニューが必ず一件になるとは限りません。

入力ソースが消えた場合の `MeltypeRegisterInputSource` による復旧手順は、[Mac 版の「困ったとき」](../mac/README.md#困ったとき) を参照してください。

<br>

<a name="macos-の辞書を読み込む"></a>
<img src="images/headings/mac-cli/06.svg" alt="macOS の辞書を読み込む" height="53"><br>


macOS のユーザー辞書を書き出したファイルは、Meltype の入力メニューの「辞書の読み込み」で取り込めます。

- `shortcut` / `phrase`、または `reading` / `word` の組を持つ、XML とバイナリの plist に対応しています
- 今ある語は残し、まったく同じ語は飛ばします
- 2 文字 (UTF-16) より短い読みと、タブや改行を含む語は読み込みません
- 読み込んだら、使っている入力にもすぐ反映します
- 辞書の中身をネットワークに送ることはありません

<br>

<a name="版の番号"></a>
<img src="images/headings/mac-cli/07.svg" alt="版の番号" height="53"><br>


ビルドの版の番号は、本体のプロジェクトの `Version`、環境変数 `MELTYPE_VERSION`、GitHub Actions ではリリースのタグから決まります。
ネイティブのビルドとアプリの plist は同じ番号を使い、変換エンジンは版をアプリの plist から読みます。
