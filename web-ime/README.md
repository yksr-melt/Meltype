# Meltype Web (試作)

Meltype をウェブページの入力欄で使うためのものです。Core (`src/Meltype.Web`) をブラウザーの .NET (WebAssembly) で動かすので、サーバーは要りません。打った文字は外に送りません。

```bash
dotnet workload install wasm-tools
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

## 今できること・できないこと

- できる: 日本語と英語の自動の判定、ローマ字からかなへの変換、英単語の予測の候補、候補のクリック、日本語と英数の切り替え (入力欄の横のボタン)。
- まだできない: 漢字変換。Mozc を WebAssembly にしたもの (`mozc/mozc.js` で、`meltype_mozc_request(要求の1行)` を出すもの) を置けば使えるようにしてあるが、そのビルドはまだ無い。無いあいだは、かなのまま確定する。
- 設定と学習データはページを閉じると消える (ブラウザーのメモリーに置いている)。

大きさ: Core と .NET の実行環境で、Brotli で圧縮しておよそ 5MB。
