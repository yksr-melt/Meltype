# TTY 版

利用者向けの説明 (インストール・使い方・設定) は [docs/TTY.md](../docs/TTY.md) にあります。

| 部分 | 中身 |
| --- | --- |
| 端末のフロントエンド (`meltype-tty`, Python) | コマンドを pty の中で起動し、そのコマンドへの入力を判定の本体に渡して、変換中の文字と候補をカーソルの位置に重ねて描く |
| 判定の本体 (`libMeltypeNative.so`) | Windows 版と共通の C# の部分 (`src/Meltype.Core`) を NativeAOT で Linux 用のライブラリにしたもの。IBus・fcitx5 の版 (`linux/`) とは別に同梱する |
| 漢字変換 | Mozc の変換ヘルパー (`native/mozc`) |
| 文脈を見た変換 (Zenzai) | llama.cpp の llama-server と zenz のモデル (`native/zenzai`) |

全体の仕組み・テストは [docs/dev/DEVELOPMENT.md](../docs/dev/DEVELOPMENT.md) にあります。

## ビルドする

.NET 10 SDK・clang・zlib の開発用ファイルを用意し、リポジトリのルートから実行します。依存関係の準備は [Linux 版のビルド](../linux/README.md#ビルドする) も参照してください。

```bash
native/mozc/build-mozc-helper.sh
native/zenzai/build-llama-server.sh
tui/build.sh
python3 -m unittest discover -s tui -p 'test_*.py'
```

`tui/build/Meltype-tty/` に本体とエンジンが揃います。ビルドしたマシンより古い glibc へ配る場合は、[共通ビルドスクリプト](../native/build-core-linux.sh) の `MELTYPE_SYSROOT` の説明を参照してください。
