# IME入力テスト

Playwright Testで、製品IME(Meltype)を通したブラウザー入力を検査するためのツールです。

テストは2種類あります。テストページや記録処理だけを確認する場合は、IMEを起動せずに実行できます。

| 種類 | 確認すること | IME | コマンド |
| --- | --- | --- | --- |
| 基盤テスト | 型検査、記録処理、テストページの動作 | 不要 | `npm run test:ci` |
| 実IMEテスト | 製品IME経由の入力結果 | 必要(Linuxのみ) | `npm run test:ime:linux:ci` |

実IMEテストの入口は`test:ime:<OS>:<実行環境>`の形式で指定します。OS名だけの指定はありません。
`linux:ci`は、専用のXvfbとIBusのセッションを自前で起動して実行します。CIと同じ構成なので、ローカルのLinuxでも使えます。

## クイックスタート(基盤テスト)

Node.js 22.xを用意し、リポジトリのルートから実行します。

```bash
cd tools/ime-tests
npm ci
npx playwright install chromium
npm run test:ci
```

> Linuxでブラウザーの共有ライブラリも入れる場合は、`npx playwright install --with-deps chromium`を使います。

`test:ci`は型検査と基盤自身のテストです。実IMEを使うテストは含まれません。

> 画面用サーバーはテスト時に自動で起動します。手動で起動した`npm run dev`は、先に停止してください。

## Linuxの実IMEテストを実行する

### 必要なもの

Linux上に、[CIの実IMEジョブ](../../.github/workflows/ime-tests.yml)と同じ依存関係を用意します。

- 実行環境: Python 3、IBus、GTK3のIBusモジュール、Xvfb、D-Bus、xdotool、setxkbmap
- 製品のビルド: .NET 10 SDK、NativeAOT、Mozcのビルド依存

### 手順

リポジトリのルートで製品をビルドし、パッケージの場所を指定して実行します。

```bash
# 1. 製品をビルドする(リポジトリのルートで実行)
native/mozc/build-mozc-helper.sh
linux/build.sh

# 2. テストを実行する
cd tools/ime-tests
python3 scripts/test_linux_ime.py
MELTYPE_PACKAGE_DIR="$PWD/../../linux/build/Meltype-linux" npm run test:ime:linux:ci
```

- `python3 scripts/test_linux_ime.py`: IMEの起動とキー入力の制御を検査します。
- `npm run test:ime:linux:ci`: 製品IMEによる入力結果を検査します。

### 実行時の注意

- 専用のLinuxセッションで動作します。HOME・設定・学習先は一時領域に分離されるため、通常のデスクトップにIMEを登録する必要はありません。
- 比較する製品は、テストと同じチェックアウトからビルドしてください。
- テストでは学習とライブ変換をOFF、配列をUSに固定します。
- 実IMEテスト全体の上限は60分です。CIジョブは製品ビルドも含め90分で打ち切ります。
- 入力には**ダミー値だけ**を使ってください。passwordの内容はイベント記録から除外しますが、trace・スクリーンショット・失敗時の差分には、テスト用の値が残ることがあります。

## ファイル構成

| 場所 | 役割 |
| --- | --- |
| `src/` | テストページと記録処理 |
| `tests/unit/` | 記録処理のテスト |
| `tests/page/` | テストページのテスト |
| `tests/ime/support.ts` | イベント記録・本文・キャレットの観測と、確定イベントの待機 |
| `tests/ime/linux-keyboard.ts` | OSキーで入力するfixture(`ime`) |
| `scripts/linux_ime.py` | 専用Linuxセッションの起動とキー入力の制御 |
| `playwright.ime.linux.config.ts` | 実IMEテストのPlaywright設定 |

製品結果の期待値は、`tests/ime/`のspecで確認します。

## テストを追加する

### 書き方

実IMEのテストは`tests/ime/`のspecに、通常の`test`・`expect`で操作と期待値を書きます。別ファイルに分ける場合は、同じディレクトリに`*.spec.ts`を追加すれば自動で収集されます。

```ts
import { test, expect } from './linux-keyboard';
import { expectCompositionEnds } from './support';

test('text: 入力して確定する', async ({ page, ime }) => {
  const input = page.locator('#text');
  await ime.type('aiueo');
  await expect(input).toHaveValue('あいうえお');
  await ime.key('Return');
  await expectCompositionEnds(page, 'text', 1);
  await expect(input).toHaveValue('あいうえお');
});
```

fixtureは、通常の入力欄でIMEを選択した状態を用意します。

| 操作 | 書き方 |
| --- | --- |
| 入力先を変える | `await ime.focus('search')` |
| 文字列を入力する | `await ime.type('aiueo')` |
| Enterなどのキーを送る | `await ime.key('Return')` |

欄を移動するたびにIMEを選び直す必要はありません。

### Linuxのfixtureとランナー間の操作契約

`linux-keyboard.ts`は`python3 scripts/linux_ime.py control '<操作JSON>'`を呼びます。
成功時の標準出力は、次の`target`を表すJSONオブジェクト1個です。失敗は非0終了です。

```ts
type Target = {
  windowId: string;       // 正の10進数文字列。X11のウィンドウID
  context: string | null; // /org/freedesktop/IBus/ で始まる入力コンテキストのパス
  password: boolean;      // 現在の欄がpasswordか
};
```

`context: null`は`password: true`の場合だけ許可します。通常欄ではコンテキストが必要です。
`title`は`Meltype IME E2E <UUID>`形式で、fixtureが作るブラウザーウィンドウを識別します。

| 操作JSON | 動作 |
| --- | --- |
| `{ action: 'select', title }` | fixtureの初期化時にだけMeltypeを選択し、`target`を返す |
| `{ action: 'focus', title, password }` | DOMで欄を移動した後にOSフォーカスと入力コンテキストを確認し、`target`を更新する。IMEを選び直さない |
| `{ action: 'type', ...target, text }` | ASCIIの印字可能文字(U+0020〜U+007E)を1〜300文字、OSキーで送る |
| `{ action: 'key', ...target, key }` | 許可されたキーを1回送る |

`key`はxdotool/X11 keysym名です。現在の許可値は`Return`、`space`、`Escape`、`BackSpace`、`Tab`、`Shift+Tab`、`Shift+Return`、`Left`、`Right`、`Up`、`Down`、`Home`、`End`、`ctrl+a`です。
ここで示したもの以外の操作・キー・追加フィールドは受け付けません。
`type`と`key`はOSフォーカスとIBusの選択を確認してからXTESTで送り、更新後の`target`を返します。

### 守ってほしいルール

- **入力はOSキーで送る。** 完成した日本語を`fill`で入れたり、compositionを合成して結果を作ったりしないでください。
- **初期条件も確認する。** 既存本文やキャレット位置をDOMで設定する場合は、入力前にその状態をassertします。
- **passwordではcompositionを期待しない。** IMEが抑止される操作に、compositionの発生を要求しないでください。
- **途中状態と最終状態をassertする。** 操作の直後に、守りたい途中状態と最終状態を検査します。確定前後で本文が変わらない場合は、本文の一致だけでは確定完了を待てません。例のように`compositionend`などのイベントも観測してください。

### 命名とコメント

- テスト名には、検査する条件を書きます。
- 関連するIssueがあれば、URLと取り出した条件を近くのコメントに書きます。Issueの手順全体を再現しない部分検査でも、何を検出するテストなのかが分かるようにしてください。
- 条件と期待値の正本はspecです。テストを追加するたびにREADMEへ項目を追記する必要はありません。

### 追加先の判断

- Coreだけで検査できる条件は、C#側に追加してください。`InputRegressionTests`も含め、既存の`build.yml`のCoreジョブ(Linux/macOS)で実行されます。既存のCoreテストをTypeScriptから再実行する構成にはしません。
- テストページは`tests/page/`、記録処理は`tests/unit/`のspecで確認します。各ディレクトリの`*.spec.ts`は、サブディレクトリも含めて自動で収集されます。

### 追加後の確認

```bash
npm run test:ci
npm run test:ime:linux:ci
```

絞り込みや部分実行には、次のコマンドを使えます。

| 目的 | コマンド |
| --- | --- |
| 基盤の記録処理だけ実行 | `npm run test:unit` |
| テストページだけ実行 | `npm run test:page` |
| テストページをブラウザー表示で実行 | `npm run test:page -- --headed` |
| テスト名で絞り込む | `npm run test:ime:linux:ci -- --grep 'テスト名'` |

## 失敗を確認する

コンソールには、失敗したテスト名、アサーションのファイルと行番号、期待値と実際の値が表示されます。詳しく調べるには、実行後にHTMLレポートを開きます。

```bash
npm run report
```

レポートから失敗したテストを選ぶと、該当コードと差分を確認できます。

### 成果物の場所

| 場所 | 内容 |
| --- | --- |
| `artifacts/report/` | HTMLレポート |
| `artifacts/results/` | 失敗時のtrace・スクリーンショット。入力前の状態やイベントの順序はtraceで調べられます |
| `artifacts/real-ime/` | 実IMEテストのプロセスログと`session.json` |
| テスト結果の添付 | 実IMEテストのブラウザーイベント |

### IMEを起動できない・キーを送れない場合

`artifacts/real-ime/`のプロセスログを確認してください。

同じ場所の`session.json`には、実行時の条件が残ります。

- 実行したチェックアウトのSHA
- 未コミット変更の有無
- 製品ファイルのハッシュ
- Node・Playwright・Chromium・.NET SDK・IBus・xdotool・Xvfbの版、Mozcの固定コミット
- GitHub Actionsのrun ID・attempt・runnerイメージの版(ローカル実行ではnull)
- `settings.requested`: ランナーが要求した設定
- `settings.productSaved`: 製品の初回読込みで移行・保存された設定。通常欄の初期化後に記録

要求した設定キーが保存後に消えたり値が変わったりした場合は、入力前に失敗します。
`productSaved`は製品による設定の読込み・保存の記録であり、実行中の全設定を直接取得したものではありません。
製品設定を読み取る前に失敗した場合はnullです。要求値との不一致で失敗した場合も、読み取れた内容は残します。
IBus接続先とPIDは一時領域の`linux-runtime.json`だけに保存し、来歴用の`session.json`には含めません。
Playwrightの出力は実行中にコンソールと`playwright.log`の両方で確認できます。

PRのheadと、実際に検査したmerge checkoutは区別して記録されます。

### CIで失敗した場合

[ime-tests workflow](../../.github/workflows/ime-tests.yml)の実行画面からArtifactsを取得してください。ローカルと同じレポートとログを確認できます。
