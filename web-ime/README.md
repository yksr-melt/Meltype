# Meltype Web

Meltype をウェブページの入力欄で使うためのものです。Core (`src/Meltype.Web`) をブラウザーの .NET (WebAssembly) で動かすので、サーバーは要りません。打った文字は外に送りません。

```bash
dotnet workload install wasm-tools
web-ime/mozc/build-mozc-wasm.sh   # Mozc (初回は 30 分以上。無くても動くが、漢字変換しない)
web-ime/build.sh
cd web-ime/dist && python3 -m http.server 8123   # → http://localhost:8123/
```

ページに埋め込むときは、`dist/` の中身を置いて次のように付けます。

```html
<link rel="stylesheet" href="meltype-web.css">
<script type="module">
  import { attach } from "./meltype-web.js";
  await attach(document.querySelector("textarea"));
</script>
```

## 中身

- `index.html`: プレイグラウンド。例を自動で打つボタンと、Core とのやり取り (ImeEvent → ImeOutput) の表示がある。
- `meltype-web.js`・`meltype-web.css`: 埋め込み用。変換中の文字と候補はキャレットの下の箱に出し、入力欄には確定した文字だけを入れる。
- `../src/Meltype.Web`: Core をブラウザーの .NET で動かす。
- `mozc/`: Mozc を Emscripten で WebAssembly にする。デスクトップ版と同じ `meltype_mozc_helper.cc` を取り込み、要求 1 行を関数で受ける。
  Mozc はスレッドを使うが、スレッドを使うと埋め込むページに Cross-Origin-Opener-Policy などのヘッダーが要るので、
  `thread-inline.patch` でスレッドで動かす処理をその場で動かすようにしてビルドする。

大きさ (圧縮後): Core と .NET でおよそ 5MB、Mozc (辞書を含む) でおよそ 14MB。

## まだできないこと

- 設定と学習データはページを閉じると消える (ブラウザーのメモリーに置いている)。
- `<textarea>`・`<input>` だけに付けられる (`contenteditable` にはまだ付けられない)。
