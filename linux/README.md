# Linux 版

利用者向けの説明 (インストール・使い方・困ったとき) は [docs/LINUX.md](../docs/LINUX.md) にあります。

| 部分 | 中身 |
| --- | --- |
| IBus のエンジン (`ibus-engine-meltype`, Python) | IBus からキーを受け取り、判定の本体に渡して、変換中の文字・候補・確定を入力欄に反映する |
| fcitx5 のアドオン (`fcitx5/`, C++) | fcitx5 からキーを受け取り、判定の本体に渡す |
| 判定の本体 (`libMeltypeNative.so`) | Windows 版と共通の C# の部分 (`src/Meltype.Core`) を NativeAOT で Linux 用のライブラリにしたもの |
| 漢字変換 | Mozc の変換ヘルパー (`native/mozc`) |

全体の仕組み・テストは [docs/dev/DEVELOPMENT.md](../docs/dev/DEVELOPMENT.md) にあります。

## ビルドする

WSL の Ubuntu でも、OS に依存しない部分のテストと、Linux 用のパッケージのビルドができます。

```bash
bash linux/build-cli.sh --setup --all
```

`--all` は、本体のテスト → Mozc のヘルパーのビルド → パッケージの作成までをまとめて行います。できたものは `linux/build/Meltype-linux/` に入ります。

一部だけ行うときは、次のように分けて実行できます。

```bash
linux/build-cli.sh --test            # テストだけ
bash linux/build-cli.sh --build      # ビルドだけ
linux/build-cli.sh --package         # パッケージを作るだけ
linux/build-cli.sh --mozc --package  # Mozc のヘルパーも作り直してパッケージにする
```

## インストールする

```bash
linux/build-cli.sh --install
```

入力ソースの一覧に Meltype が出てこないときは、Linux のセッションを入り直してください。
エンジンの動きを調べるときは、次のように手で起動するとログが見られます。

```bash
ibus restart
sleep 2
pkill -f ibus-engine-meltype || true
/opt/meltype/ibus-engine-meltype --version   # 版だけ確認するとき
/opt/meltype/ibus-engine-meltype
```
