# Meltype の拡張機能

Chrome・Edge・Firefox の拡張機能です。どのサイトの入力欄でも、ローマ字のまま打つと、日本語と英語を分けて変換します。

中身は [Web エンジン](../web-engine/README.md) で、判定 (Core) も漢字変換 (Mozc) もブラウザーの中で動きます。打った文字はどこにも送りません。

## 作る・入れる

```bash
web-extension/build.sh
```

`web-extension/dist/` と `web-extension/meltype-extension.zip` ができます (Web エンジンが無ければ、先に `web-engine/build.sh` で作ります)。

- **Chrome・Edge**: `chrome://extensions` (Edge は `edge://extensions`) で「デベロッパー モード」を ON にし、「パッケージ化されていない拡張機能を読み込む」で `dist/` を選ぶ。
- **Firefox**: `about:debugging#/runtime/this-firefox` の「一時的なアドオンを読み込む」で `dist/manifest.json` を選ぶ (Firefox を閉じると外れる)。
- ストアに出すときは `meltype-extension.zip` を使う。

アイコンは `web/scripts/make-icons.mjs` が `web/public/favicon.svg` から作ります。

## 動き

- 入力欄 (`<textarea>`・文字の `<input>`・`contenteditable`) を初めて選んだときに、Web エンジンを読み込みます。入力欄を使わないページでは読み込みません。読み込んでいる間に打ったキーは、ふつうに入ります。
- メールアドレス・数字・パスワードなどの `<input>` には付けません。
- 入力モードは日本語だけです (英語もそのまま打てます)。
- 覚えたことは拡張機能の保存場所 (`storage.local`) に置き、どのサイトでも共有します。サイトの IndexedDB には置きません (そのサイトから読めてしまうため)。
- 候補の欄は Shadow DOM の中に描くので、サイトの CSS とは混ざりません。
- ツールバーのボタンから、サイトごとに使うかを切り替えられます。覚えたことも消せます。
- 指で操作する端末 (Android の Firefox など) では、画面の下に Meltype のキーボードを出します。

## 使えないサイト

サイトのセキュリティの設定 (CSP) の `script-src` に `'wasm-unsafe-eval'` が無いと、ブラウザーは拡張機能のスクリプトの中の WebAssembly も止めるので、Meltype を動かせません (GitHub・X など、CSP の厳しいサイト)。そのサイトでは何もせず、キーはそのまま入ります。ツールバーのボタンを押すと、そのことが出ます。

対応するには、エンジンを拡張機能の側 (Chrome は offscreen document、Firefox は裏のページ) で動かし、ページのスクリプトがキーを受け取ってから答えを待つ作りにする必要があります。キーをページに通すかをその場で決められなくなるので、通すキーの動き (文字・Backspace・矢印など) を拡張機能が代わりに行うことになります。

## そのほか

- タブごとに Web エンジンを読み込むので、入力欄を使ったタブごとに数百 MB ほどのメモリーを使います。
- Mozc が覚えたこと (漢字の候補の順番) は、タブを閉じると忘れます。
- AI の変換 (Zenzai) はありません。
- Chrome では、拡張機能の中のファイル (`engine/`) をサイトから読めるようにしているので、サイトはこの拡張機能が入っていることを知ることができます。
