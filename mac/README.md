# Mac 版

利用者向けの説明 (インストール・使い方・困ったとき) は [docs/MACOS.md](../docs/MACOS.md) にあります。

| 部分 | 中身 |
| --- | --- |
| IME 本体 (`Sources/MeltypeIME`, Swift) | Input Method Kit でキーを受け取り、変換中の文字 (下線付き)・候補の一覧・確定を入力欄に反映する |
| 判定の本体 (`src/Meltype.Mac.Native` → `libMeltypeNative.dylib`) | Windows 版と共通の C# の部分 (`src/Meltype.Core`: 英語 / 日本語の判定・ローマ字・変換の流れ・学習・辞書) を NativeAOT で Mac 用のライブラリにしたもの |
| 漢字変換 | 本体が `Resources/mozc/` の Mozc の変換ヘルパーを起動する (Windows・Linux 版と同じ)。Zenzai は、`build.sh` が取ってきて `Resources/zenzai/` に同梱する llama.cpp の llama-server (MIT) と zenz-v3.2-small (Apache-2.0) |
| 英単語の判定 | macOS のスペルチェッカー (英語) |

全体の仕組み・テストは [docs/dev/DEVELOPMENT.md](../docs/dev/DEVELOPMENT.md) にあります。コマンドはどれも、リポジトリのフォルダーで実行します。

## ビルドする

必要なもの:

- Xcode、またはコマンドライン ツール (`xcode-select --install`)
- .NET 10 SDK (<https://dotnet.microsoft.com/download>。または `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0`)
- Mozc の変換ヘルパー (`native/mozc/bin/mac/`)。main の `mac.yml` の成果物 `meltype-mac` の `Meltype.app/Contents/Resources/mozc/` の 3 つのファイルを置く。ヘルパーのソースや `native/mozc/MOZC_COMMIT` を変えたときだけ、`native/mozc/build-mozc-helper.sh` (Bazelisk が要る。初回 30〜40 分) で作る

```bash
mac/build-cli.sh --setup --all
```

`--all` は、本体のテスト → `mac/build/Meltype.app` のビルド → `~/Library/Input Methods/Meltype.app` へのインストールまでをまとめて行います。ビルドした CPU 向けのアプリができるので、Apple シリコン版と Intel 版は互換ではありません。

一部だけ行うときは、次のように分けて実行できます。`--build` はインストール・プロセスの停止・入力ソースの切り替えを行いません。

```bash
mac/build-cli.sh --test      # テストだけ
mac/build-cli.sh --build     # ビルドだけ
mac/build-cli.sh --install   # ビルドしてインストール
```

初めて入れたときに入力メニューに Meltype が出てこなければ、**システム設定 → キーボード →（テキスト入力の）入力ソース →「編集…」→「+」→ 日本語 → Meltype** で追加します (macOS 27 では、アプリからの有効化が通らず、この手順が必要になることがあります)。追加すると、同じログインセッションのまま入力メニューから選べます。それでも出てこなければ、ログアウトしてログインし直します。

## 1 つのコマンドで更新する

手元のソースで、入れてある Meltype を新しくします。使用中の入力ソースを入れ替えるので、入れ替えてよいときだけ実行します。

```bash
bash mac/update.sh
```

次のことを順に行います。入力のチェックは既定の設定で実行します。チェックに失敗した場合は入力ソースの選び直しを行わず、更新済みのアプリはそのまま残します。Git の変更を取り込んだり、ユーザー辞書を上書きしたりはしません。

- 本体のテスト (OS に依存しない部分)
- アプリのビルドとインストール
- 同梱の Mac 用の入力のチェック
- Meltype を入力ソースに選び直す

## 動作の確認とログ

インストールせずに、ビルドしたアプリで確認できます。

```bash
mac/build/Meltype.app/Contents/MacOS/Meltype --check-inputs mac/Resources/InputChecks.tsv
mac/build/Meltype.app/Contents/MacOS/Meltype --self-test
swift test --package-path mac
sbcl --script tests/test_mac_scripts.lisp
```

`--check-inputs` と `--self-test` は、空の一時ディレクトリをデータの保存先にして実行します。利用者の Meltype の設定・学習データ・ユーザー辞書は読み書きせず、検査が終わると一時ディレクトリを削除します。

`--self-test` は、同梱の変換エンジンを実際に使い、日本語と英語の混ざった入力・候補の選択・確定・直接入力の切り替え・絵文字の候補・綴りの提案・入力ソースの重複の整理・plist の辞書の読み込みを確かめます。Chrome・VS Code などの IMK クライアントでの動きや、OS の入力ソースの切り替えは確かめないので、実機で確認してください。

動作中のログは、次のコマンドで見られます。

```bash
log stream --predicate 'process == "Meltype"' --level debug
```

## 配布用の zip を作る

ビルドした後に `bash mac/package.sh` を実行すると、`dist/` に配布用の zip ができます。zip 全体を展開して、`Install Meltype.command` または `bash install.sh` で入れます。

## 入力ソースの重複を直す

```bash
"$HOME/Library/Input Methods/Meltype.app/Contents/MacOS/Meltype" --register-input-source
```

OS の登録 API で登録・有効化した後、`com.apple.HIToolbox` の `AppleEnabledInputSources` と `com.apple.inputsources` の `AppleEnabledThirdPartyInputSources` を読み、Meltype がある各一覧の中で親と日本語モードの重複を除きます。最初の項目の位置と付随情報、ほかの入力ソースを保持します。OS の版番号で保存先を作ったり、保存先をまたいで登録を移したりはしません。両方に Meltype がある場合は、それぞれの登録を残します。

変更前の一覧は、変更する設定領域ごとに `MeltypeInputSourcesBeforeRepair` に保存します。有効な日本語モードが一件になるまで最大約 2 秒待ち、一件なら入力メニューを更新します。反映待ちの場合は件数と対処方法を表示して終了コード 0 で終了し、入力メニューを再起動しません。設定の形式・保存・登録 API の失敗は終了コード 1 にします。同じコマンドを繰り返しても一覧内の重複は増えません。保存先をまたぐ登録の統合は行わないため、実メニューが必ず一件になるとは限りません。

## 版の番号

ビルドの版の番号は、本体のプロジェクトの `Version`、環境変数 `MELTYPE_VERSION`、GitHub Actions ではリリースのタグから決まります。
ネイティブのビルドとアプリの plist は同じ番号を使い、変換エンジンは版をアプリの plist から読みます。
