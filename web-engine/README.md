# Meltype Web Engine

ウェブページの入力欄で Meltype を使うためのものです。ローマ字のまま打つと、日本語と英語を分けて変換します。

Meltype の判定 (Core) と漢字変換 (Mozc) を WebAssembly にして、ブラウザーの中だけで動かします。打った文字はどこにも送りません。最初に 20MB ほど (圧縮後) 読み込みます。

## 使い方

### `<script>` で読む

`dist/` の中身をサイトに置き、`meltype.js` を読みます。本体 (`_framework/`・`mozc/`) は `meltype.js` の隣から読みます。

```html
<textarea id="text"></textarea>
<script src="/meltype/meltype.js"></script>
<script>
  Meltype.attach(document.getElementById("text"));
</script>
```

### npm で使う

```bash
npm install meltype-web-engine
```

```js
import { attach } from "meltype-web-engine";

await attach(document.querySelector("textarea"));
```

本体 (`_framework/`・`mozc/`) は `index.mjs` の隣から読みます。まとめるツール (バンドラー) が `index.mjs` だけを別の場所に移すときは、パッケージのフォルダーを丸ごと公開する場所に写し、`load({ baseUrl })` でその場所を指定してください。

## API

| 関数 | 内容 |
| --- | --- |
| `attach(element, options)` | `<textarea>`・`<input>`・`contenteditable` の要素に付ける。本体をまだ読んでいなければ読む。返す物の `detach()` で外す |
| `load(options)` | 本体だけを先に読む。返す物 (`Engine`) の `attach()` は待たずに付けられる。2 回目からは同じものを返す |
| `engine.clearLearning()` | 覚えたことを消す |
| `loadCore(options)` | Core (判定・変換・学習) だけを読む。入力欄の画面は持たない。ほかの場所 (拡張機能のページなど) で Core を動かし、`load({ core })` で渡すときに使う |

| 設定 | 既定 | 内容 |
| --- | --- | --- |
| `keyboard` | `"auto"` | 画面のキーボードを出すか。`"auto"` は指で操作する端末だけ、`"always"`・`"never"` |
| `persist` | `true` | 覚えたこと (変換の選び方など) を残すか |
| `storage` | そのページの IndexedDB | 覚えたことの置き場 (`{ get(), set(json), onChange(listener) }`。`get`・`set` は Promise を返す。`onChange` は、ほかのタブなどが保存した・消した (`null`) ときに呼んでもらう関数を受け取る) |
| `onCommit(text)` | | 確定した文字を入力欄に入れたとき |
| `onProgress({ phase, loaded, total })` | | 読み込みの進み具合。`phase` は `"core"`・`"dictionary"` (辞書。`loaded`・`total` はバイト数)・`"ready"` |
| `baseUrl` | `meltype.js`・`index.mjs` の場所 | 本体を置いた場所 |
| `core` | | ほかの場所で動かしている Core (`loadCore` の返す物と同じ形。`create`・`handle` は Promise を返してよい)。渡すと、このページでは Core を読まない |

型は `index.d.ts` にあります。

## 見た目を変える

候補の欄・画面のキーボード・カーソルは Shadow DOM の中に描くので、ページの CSS とは混ざりません。色と字は、ページで `--meltype-*` の変数を決めると変わります (`--meltype-bg`・`--meltype-fg`・`--meltype-muted`・`--meltype-accent`・`--meltype-on-accent`・`--meltype-border`・`--meltype-key-bg`・`--meltype-keyboard-bg`・`--meltype-font`・`--meltype-key-height`)。

```css
:root {
  --meltype-accent: #e8590c;
  --meltype-font: "Zen Maru Gothic", sans-serif;
}
```

## 動き

- 入力モードは日本語だけです。Meltype は日本語のモードのままで英語も打てるので、英数のモードはありません。半角/全角・英数・かなのキーは何もしません。
- 変換中の文字と候補は、入力欄には入れず、キャレットの下に重ねて出します。入力欄に入るのは確定した文字だけです。
- 指で操作する端末では、端末のキーボードを出さずに、画面の下に Meltype のキーボードを出します (端末のキーボードの IME が打鍵を変換してしまうため)。
  - `<textarea>`・`<input>` は `readonly` にしてカーソルを描きます (Android の Firefox は `inputmode="none"` でも端末のキーボードを出すことがあるため)。
  - `contenteditable` は `readonly` にできないので、Android の Firefox では端末のキーボードも出ます。
- 覚えたことは、既定ではそのブラウザーのそのサイトの IndexedDB にだけ残ります (`storage` で置き場を変えられます)。プライベートブラウズなどで残せないときは、ページを閉じると忘れます。
- 同じサイトを複数のタブで開いていても、覚えたことは上書きし合いません。確定したらすぐ保存し、ほかのタブが保存したら、このタブもすぐ読み直します (デスクトップ版で、入力と設定画面が学習データを共有するのと同じ)。2 つのタブでほぼ同時に確定したときだけ、片方が覚えたことが無くなることがあります。`clearLearning()` で消すと、ほかのタブも消えます。
- 付けられる `<input>` は、文字を打つもの (`text`・`search`・`url`・`tel`) だけです。パスワード・メールアドレス・数字などの入力欄に `attach()` しても、何もしません。
- 変換に使ったキー (確定の Enter など) は、ページのほかのキーのリスナーにも届きません。Enter で送信するページでも、確定で送信されません。

Core をほかの場所で動かす (`core` を渡す) ときは、答えを待たずにキーを入力欄に通すかを決めます。Core が前の答えで知らせた「受け取るキー」(印字できる文字・Space など) で決め、変換中は修飾キーの無いキーをすべて受け取ります。外れたとき (受け取ったのに Core が通したとき) は、そのキーの動き (文字・Backspace・Enter・← →) を代わりに入力欄に書きます。

## 組み込むサイトに要るもの

- **CSP**: WebAssembly を動かすため、`script-src` に `'wasm-unsafe-eval'` が要ります。本体を別のドメインに置くときは、そのドメインも `script-src`・`connect-src` に入れてください。
- **MIME**: `.wasm` は `application/wasm` で配ってください。
- **CORS**: 本体を別のドメインに置くときは、そのドメインで `Access-Control-Allow-Origin` を返してください。

## アプリとの違い

- AI の変換 (Zenzai) はありません。
- 設定は初期値のままで、変えられません。
- Mozc が覚えたこと (漢字の候補の順番) はページを閉じると忘れます。IndexedDB に残すのは Meltype が覚えたことだけです。

## ライセンス

GPL-3.0-or-later です。このエンジン (JS・WebAssembly) はページを開いた人のブラウザーに配られるので、組み込んだサイトは、このエンジンについて GPL の条件 (ソースを手に入れられるようにするなど) を守る必要があります。Mozc は BSD-3-Clause です。

## 作る

```bash
dotnet workload install wasm-tools
native/mozc/build-mozc-wasm.sh    # Mozc (初回は 30 分以上。作らなければ、かなのまま確定する版になる)
web-engine/build.sh                # → web-engine/dist/
```

紹介ページ (できること・インストールの方法・API・デモ) は、Meltype のサイトの [/web-engine/](../web/README.md#web-エンジン-web-engine) にあります。

`web-engine/example/` は、作ったものを試すページです。`web-engine/` を静的なサーバーで開き、`/example/` を見てください。
