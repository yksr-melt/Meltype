# 入力・回帰テスト

Playwright Testで、入力テスト画面のDOM操作とテスト基盤を検査するフォルダです。

## テストランナーと実行方法

- ランナー: Playwright Test
- ブラウザー: Playwrightの固定バージョンに対応するChromium
- 必要環境: Node.js 20以降、macOSまたはLinux
- CI: Ubuntu 24.04、Node.js 22、1ジョブ・1ワーカー、リトライなし

```bash
cd tools/ime-tests
npm ci
npx playwright install chromium
# LinuxでOS依存ライブラリも導入する場合:
# npx playwright install --with-deps chromium
npm run test:ci
```

| コマンド | 実行内容 |
| --- | --- |
| `npm run test:ci` | 型検査と全テスト |
| `npm run typecheck` | TypeScriptの型検査のみ |
| `npm run test:unit` | 記録・プロセス実行・レポートなど基盤の単体テスト |
| `npm run test:dom` | 入力画面の基本操作 |
| `npm run dev` | 入力テスト画面を起動（`http://127.0.0.1:4173`） |
| `npm run report` | 保存済みのHTML結果を開く |

テスト実行時は画面用サーバーが自動起動します。既存サーバーを再利用しないため、`npm run dev` は停止してからテストを実行してください。

### 絞り込み・デバッグのオプション

| 指定 | 用途 |
| --- | --- |
| `--project=unit` / `chromium` | 実行プロジェクトを選択 |
| ファイルパス | 対象specを絞り込み |
| `--grep '正規表現'` | タイトルやケースIDを絞り込み |
| `--headed` | ブラウザーを表示してDOM試験を実行 |
| `--debug` | Inspectorで操作を追跡 |
| `--trace=on` | 成功時もtraceを保存 |
| `--help` | 使用可能なオプションを表示 |

```bash
npx playwright test --project=chromium --grep '\[D03/' --headed
npm run test:dom -- --grep '\[D03/' --debug
```

`npm run` に追加オプションを渡す場合は `--` を挟みます。`test:ci` は複合コマンドなので、絞り込みには `npx playwright test` または個別のテストコマンドを使います。

## フォルダ構成

```text
tools/ime-tests/
├── index.html                 入力テスト画面
├── playwright.config.ts       テスト収集・ブラウザー・レポートの設定
├── package.json               実行コマンド・依存パッケージ
├── package-lock.json          依存バージョンの固定
├── tsconfig.json              型検査の設定
├── src/
│   ├── main.ts                画面の入力・送信・イベント処理
│   ├── style.css              画面の表示
│   ├── recorder.ts            イベント記録・保存JSONの検査
│   ├── process.ts             外部プロセスの実行・終了判定
│   ├── schemas/               操作・期待値・観測データの型
│   ├── runners/               操作の実行・観測・比較
│   └── reporting/             失敗要約・証拠保存・ログ出力
├── tests/
│   ├── e2e/                   入力テスト画面の基本操作
│   └── unit/                  テスト基盤の単体テスト・型検査
└── artifacts/                 実行結果・添付（Git管理対象外）
```

## テストの追加方法

1. 検証目的に合うspecへ、背景コメント・操作と期待値・`test(...)` をまとめて追加する。
2. 専用の型に対して `as const satisfies` を使う。
3. 型検査と追加したケースを実行する。
4. `npm run test:ci` で全体を確認する。

新しいspecを作る場合は、事象・目的を表すファイル名にします。現在の収集設定は次のとおりです。

| プロジェクト | 収集対象（`tests/` からの相対パス） |
| --- | --- |
| `unit` | `unit/**/*.spec.ts` |
| `chromium` | `e2e/**/*.spec.ts` |

収集対象外の場所へspecを追加する場合は、`playwright.config.ts` の対応する `testMatch` にも追加します。

### 例: DOMの本文への追記

`tests/e2e/` のspecへ追加する例です。使用するimportは既存の宣言へまとめます。


```ts
import { test } from '@playwright/test';
import type { AppendCase } from '../../src/schemas/input';
import { append, withDomEvidence } from '../../src/runners/dom';

{
  // 既存の本文を残して追記できることを確認する。
  const item = {
    id: 'D.append-example',
    operation: { field: 'textarea', initial: '本文', added: '追記' },
    expected: { text: '本文追記' },
  } as const satisfies AppendCase;

  test(`[${item.id}] 既存の本文を残して追記する`, async ({ page }, info) => {
    await withDomEvidence(page, info, () => append(page, item));
  });
}
```

| 値・引数 | 渡すもの |
| --- | --- |
| `id` | プロジェクト内で重複しないケース識別子 |
| `operation.field` | 操作する入力欄のID。`password`は指定不可 |
| `operation.initial` | 操作前に設定する本文 |
| `operation.added` | 本文末尾へ追記する文字列 |
| `expected.text` | 追記後に期待する本文全体 |
| `page` | Playwrightが用意するブラウザーページ |
| `info` | 添付保存などに使うテスト実行情報 |
| `withDomEvidence` の第3引数 | 画面を開いたあとに実行する処理。終了時にイベント記録を添付 |
| `append` の第3引数（省略可） | `true`なら複数行本文の末尾へ移動し、追記後のキャレット位置も検査 |

```bash
npm run typecheck
npx playwright test --project=chromium --grep '\[D.append-example\]'
npm run test:ci
```

### ケースの記述ルール

- タイトル: `[ケースID] 検証する事象`。
- ID: 移動・説明変更でも保持。同じ背景の変種は `D03/textarea` のように対象値を付ける。
- 背景: 関連するIssue・PR番号と事象をケース直前のコメントに記載。
- 操作・期待値: 型付きオブジェクトを正本とし、コメントへ重複記載しない。
- 型: 専用interfaceを使用。`Dict`、`Record<string, …>`、文字列インデックスシグネチャは使わない。
- 外部JSON: `unknown`から必要な項目を検査。
- 登録: 中央のケース一覧や採番台帳は不要。プロジェクト名とIDの重複を収集時に検査。

## 失敗表示と詳細

```text
FAIL E2E/dom-input [D03/textarea] 確定と送信・改行
送信回数: 期待 "1" / 実際 "0"
```

1. ログのケースIDと、2行目の差分・エラーを確認する。
2. `npm run report` でHTML結果を開き、ケースIDで検索する。
3. 添付された観測、`case-source`（実行時のspec）、DOM試験のtraceを確認する。

| 保存先 | 内容 |
| --- | --- |
| `artifacts/report/` | HTML結果・添付へのリンク |
| `artifacts/results.json` | JSON形式のテスト結果 |
| `artifacts/results/` | テスト添付・DOM失敗時のスクリーンショットとtrace |
| `artifacts/source/` | 実行開始時のテストソース・設定・入力画面 |
| `artifacts/run-info.json` | SHA、未コミット変更の有無、開始時刻、OS、Nodeのバージョン |
| `artifacts/run-errors.jsonl` | 収集・実行基盤の障害。発生時のみ |
| `artifacts/ci.log` | CIの実行ログ |

- 通常の失敗表示: ケース名と差分・エラーの2行。
- CLI異常終了・timeout: 期待値不一致とは別のエラーとして表示。
- DOMの比較値: 自動待機中の最終観測。失敗後の再読取りで差し替えない。
- 添付保存失敗: 注釈へ記録。テスト本体の失敗を優先し、本体が成功していた場合は保存失敗を報告。
- 保存ソース: テスト基盤が対象。Core製品ソース全体の未コミット差分は含まない。
- CI成果物の保持期間: 14日。
- 必須チェック名: `IME CI tests`（リポジトリ保護ルールで設定）。

## 検証範囲と機密情報

- DOM: ローカル入力テスト画面への合成イベントと画面の状態。
- 対象外: 実IME、OS入力ソース、実候補確定、OSクリップボード経由の入力。
- イベント記録の既定値: ON。
- 通常欄の文字保存の既定値: OFF。
- passwordの記録: 値・キー・変換データを収集・保存しない。
- 試験用の入力: ダミー文字列のみ。
- trace: 画面全体を含む。実際の秘密情報は入力しない。
