# Mac 版

利用者向けの説明 (インストール・使い方・困ったとき) は [docs/MACOS.md](../docs/MACOS.md) にあります。

| 部分 | 中身 |
| --- | --- |
| IME 本体 (`Sources/MeltypeIME`, Swift) | キーを受け取り、変換中の文字と候補を表示し、確定した文字を入力欄に入れる |
| 判定の本体 (`src/Meltype.Core.CApi` → `libMeltypeNative.dylib`) | Windows 版と共通の C# の部分 (`src/Meltype.Core`: 英語 / 日本語の判定・ローマ字・変換の流れ・学習・辞書) を NativeAOT で Mac 用のライブラリにしたもの |
| 漢字変換 | 本体が `Resources/mozc/` の Mozc の変換ヘルパーを起動する (Windows・Linux 版と同じ)。Zenzai は、`build.sh` が取ってきて `Resources/zenzai/` に同梱する llama.cpp の llama-server (MIT) と zenz-v3.2-small (Apache-2.0) |

全体の仕組み・テストは [docs/dev/DEVELOPMENT.md](../docs/dev/DEVELOPMENT.md) にあります。コマンドはどれも、リポジトリのフォルダーで実行します。

## ビルドする

必要なもの:

- Xcode、またはコマンドライン ツール (`xcode-select --install`)
- .NET 10 SDK (<https://dotnet.microsoft.com/download>。または `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0`)
- Mozc の変換ヘルパーとライセンスの 3 ファイル (`native/mozc/bin/mac/`)。以下の手順で用意します

Apple シリコンでは、main の `mac.yml` の成果物 `meltype-mac` から取得します。ソースから Mozc をビルドする必要はありません。
[GitHub CLI](https://cli.github.com/) を入れて `gh auth login` で GitHub にログインした後、リポジトリのルートで次を実行してください。

```bash
(
    set -e
    run_id="$(gh run list --repo yksr-melt/Meltype --workflow mac.yml --branch main --status success --limit 1 --json databaseId --jq '.[0].databaseId')"
    if [[ -z "$run_id" || "$run_id" == null ]]; then
        echo "main で成功した mac.yml の実行が見つかりません。" >&2
        exit 1
    fi
    mozc_tmp="$(mktemp -d)"
    trap 'rm -rf "$mozc_tmp"' EXIT
    gh run download "$run_id" --repo yksr-melt/Meltype --name meltype-mac --dir "$mozc_tmp"
    unzip -q "$mozc_tmp"/Meltype-*.zip -d "$mozc_tmp"
    mkdir -p native/mozc/bin/mac
    cp "$mozc_tmp"/Meltype-mac/Meltype.app/Contents/Resources/mozc/{meltype_mozc_helper,MOZC-LICENSE.txt,MOZC-CREDITS.html} native/mozc/bin/mac/
    chmod +x native/mozc/bin/mac/meltype_mozc_helper
)
```

ブラウザーで取得する場合も GitHub へのログインが必要です。[Actions の mac](https://github.com/yksr-melt/Meltype/actions/workflows/mac.yml) で、main の成功した実行を開き、Artifacts の `meltype-mac` をダウンロードします。
ダウンロードした zip を展開すると、さらに `Meltype-*.zip` が入っています。それも展開し、`Meltype-mac/Meltype.app/Contents/Resources/mozc/` の `meltype_mozc_helper`・`MOZC-LICENSE.txt`・`MOZC-CREDITS.html` を、リポジトリの `native/mozc/bin/mac/` に置いてください。
`meltype_mozc_helper` に実行権限がなければ、`chmod +x native/mozc/bin/mac/meltype_mozc_helper` を実行します。成果物の保存期間は 14 日です。期限切れの場合は、成果物が残っている main の成功した実行を選んでください。

成果物は Apple シリコン用だけです。Intel Mac と、ヘルパーのソースや `native/mozc/MOZC_COMMIT` を変えた場合は、[Bazelisk](https://github.com/bazelbuild/bazelisk) を用意して、次のように手元でビルドします。`BAZEL` で Bazelisk の場所を指定します (初回 30〜40 分)。

```bash
BAZEL="$(command -v bazelisk)" native/mozc/build-mozc-helper.sh
```

ヘルパーを用意した後、本体をビルドします。

```bash
mac/build-cli.sh --setup --all
```

`--all` は、本体のテスト → `mac/build/Meltype.app`・`mac/build/Meltype Settings.app` (設定アプリ) のビルド → `~/Library/Input Methods/Meltype.app`・`~/Applications/Meltype Settings.app` へのインストールまでをまとめて行います。ビルドした CPU 向けのアプリができるので、Apple シリコン版と Intel 版は互換ではありません。

一部だけ行うときは、次のように分けて実行できます。`--build` はインストール・プロセスの停止・入力ソースの切り替えを行いません。

```bash
mac/build-cli.sh --test      # テストだけ
mac/build-cli.sh --build     # ビルドだけ
mac/build-cli.sh --install   # ビルドしてインストール
```

macOS から Meltype の有効化を求められたら「許可」を押します。入力メニューに Meltype が出てこなければ、**システム設定 → キーボード →（テキスト入力の）入力ソース →「編集…」→「+」→ 日本語 → Meltype** で追加します (macOS 27 では、この手順が必要になることがあります)。追加すると、同じログインセッションのまま入力メニューから選べます。それでも出てこなければ、ログアウトしてログインし直します。

## 1 つのコマンドで更新する

先に[ビルドする](#ビルドする)の手順で Mozc の変換ヘルパーを用意してください。手元のソースで、入れてある Meltype を新しくします。使用中の入力ソースを入れ替えるので、入れ替えてよいときだけ実行します。

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

## 配布ファイルを作る

ビルドした後に次を実行すると、`dist/` に DMG ができます。開いた DMG の「Install Meltype」から試せます。

```bash
bash mac/package.sh Meltype-mac-test.dmg
```

`bash mac/package.sh` は ZIP を作ります。ZIP 全体を展開し、`Install Meltype.command` または `bash install.sh` で入れます。

## 入力ソースの重複を直す

```bash
"$HOME/Library/Input Methods/Meltype.app/Contents/MacOS/Meltype" --register-input-source
```

OS の API で登録し、有効化を要求した後、`com.apple.HIToolbox` の `AppleEnabledInputSources` と `com.apple.inputsources` の `AppleEnabledThirdPartyInputSources` を読みます。それぞれの一覧で Meltype の親と日本語モードの重複を除き、日本語モードを伴わない親の項目を取り除きます。残す項目は最初の位置と付随情報を保持し、ほかの入力ソースも保持します。OS の版番号で保存先を作ったり、保存先をまたいで登録を移したりはしません。

変更前の一覧は、変更する設定領域ごとに `MeltypeInputSourcesBeforeRepair` に保存します。有効な日本語モードが一件になるまで最大約 2 秒待ち、一件なら入力メニューを更新します。反映待ちの場合は件数と対処方法を表示して終了コード 0 で終了し、入力メニューを再起動しません。設定の形式・保存・登録 API の失敗は終了コード 1 にします。同じコマンドを繰り返しても一覧内の重複は増えません。保存先をまたぐ登録の統合は行わないため、実メニューが必ず一件になるとは限りません。

## 版の番号

ビルドの版の番号は、本体のプロジェクトの `Version`、環境変数 `MELTYPE_VERSION`、GitHub Actions ではリリースのタグから決まります。
ネイティブのビルドとアプリの plist は同じ番号を使い、変換エンジンは版をアプリの plist から読みます。
