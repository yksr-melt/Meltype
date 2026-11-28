# Linux 版

利用者向けの説明 (インストール・使い方・困ったとき) は [docs/LINUX.md](../docs/LINUX.md) にあります。

| 部分 | 中身 |
| --- | --- |
| IBus のエンジン (`ibus-engine-meltype`, Python) | IBus からキーを受け取り、判定の本体に渡して、変換中の文字・候補・確定を入力欄に反映する |
| fcitx5 のアドオン (`fcitx5/`, C++) | fcitx5 からキーを受け取り、判定の本体に渡す |
| 設定アプリ (`meltype-settings`) | Windows・Mac と共通の `src/Meltype.Settings` を NativeAOT でビルドしたもの。IBus・fcitx5 の設定と、アプリの一覧の「Meltype」から開く |
| 判定の本体 (`libMeltypeNative.so`) | Windows 版と共通の C# の部分 (`src/Meltype.Core`) を NativeAOT で Linux 用のライブラリにしたもの |
| 漢字変換 | Mozc の変換ヘルパー (`native/mozc`) |
| 文脈を見た変換 (Zenzai) | llama.cpp の llama-server と zenz のモデル。llama-server は `native/zenzai/build-llama-server.sh` が `native/zenzai/zenzai.env` の版のソースからビルドし、モデルは `native/build-core-linux.sh` が取ってくる (リリースタグでは取得失敗で停止。手元のタグなしビルドだけは、モデルを取れなければ Mozc だけで変換する)。どちらも `zenzai/` に置く |

全体の仕組み・テストは [docs/dev/DEVELOPMENT.md](../docs/dev/DEVELOPMENT.md) にあります。

## ビルドする

.NET 10 SDK を使います。公式配布物の NativeAOT は glibc 2.34 を基準にビルドします。手元では、`MELTYPE_SYSROOT` を指定しなければビルド環境の glibc が基準になります。古いディストリビューションへ配る場合は [native/build-core-linux.sh](../native/build-core-linux.sh) と [CI の sysroot 設定](../.github/actions/linux-native/action.yml) を参照してください。

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
