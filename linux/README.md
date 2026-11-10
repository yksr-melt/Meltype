# Linux 版

利用者向けの説明 (インストール・使い方・困ったとき) は [docs/LINUX.md](../docs/LINUX.md) にあります。

| 部分 | 中身 |
| --- | --- |
| IBus のエンジン (`ibus-engine-meltype`, Python) | IBus からキーを受け取り、判定の本体に渡して、変換中の文字・候補・確定を入力欄に反映する |
| fcitx5 のアドオン (`fcitx5/`, C++) | fcitx5 からキーを受け取り、判定の本体に渡す |
| 設定画面 (`meltype-settings`, Python・GTK 4) | `config.schema.json` から画面を組み立てて、`config.json` を書く |
| 判定の本体 (`libMeltypeNative.so`) | Windows 版と共通の C# の部分 (`src/Meltype.Core`) を NativeAOT で Linux 用のライブラリにしたもの |
| 漢字変換 | Mozc の変換ヘルパー (`native/mozc`) |

全体の仕組み・テストは [docs/dev/DEVELOPMENT.md](../docs/dev/DEVELOPMENT.md) にあります。

## ビルドする

Ubuntu・Debian (WSL を含む) と Fedora でビルドできます。`--setup` は、apt か dnf でビルドに要るものを入れます。

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

`linux/build/Meltype-linux/` から `.deb` (apt のディストリビューション) か `.rpm` (Fedora) を作って入れます。版には `+dev.<日時>` が付くので、同じ版で何度入れ直しても、同じ版のリリース版を入れてあっても、更新として入ります。

`+dev` の版は、同じ版のリリース版より新しい扱いになります。リリース版に戻すときは、ダウングレードを明示します。

```bash
sudo apt install --allow-downgrades meltype=<版>   # apt のリポジトリから。ファイルなら ./meltype_<版>_amd64.deb
sudo dnf downgrade meltype                          # dnf のリポジトリから。ファイルなら ./meltype-<版>-1.x86_64.rpm と ./meltype-fcitx5-<版>-1.x86_64.rpm
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
