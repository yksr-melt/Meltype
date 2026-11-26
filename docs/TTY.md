<p align="center">
  <img src="images/logo.png" alt="Meltype" width="360">
</p>

# Meltype for TTY

端末版 `meltype-tty` のインストール・使い方の説明です。`meltype-tty` は、シェルなどのコマンドを pty の中で起動し、そのコマンドへの入力を Meltype で変換します。IBus・fcitx5 などの IME が動かない、GUI の無い Linux のコンソールで使います。キー操作と共通の変換機能は [USAGE.md](USAGE.md) にあります。

## 動く環境

- Linux (x86_64 / ARM64)、glibc 2.34 以降
- python3 (3.9 以降) と ICU (`libicu`)
- 日本語を表示できる端末。Linux のコンソール (`/dev/tty1` など) は日本語を表示できないので、fbterm などの中で起動します

## インストール

[Releases](https://github.com/yksr-melt/Meltype/releases/latest) から CPU に合う zip (x86_64 は `Meltype-<version>-tty-linux.zip`、ARM64 は `Meltype-<version>-tty-linux-arm64.zip`) をダウンロードして、好きな場所に展開します。apt・dnf のパッケージとは別の配布物です。ソースからビルドする方法は [tui/README.md](../tui/README.md) にあります。

```sh
mkdir -p ~/.local/share ~/.local/bin
unzip Meltype-<version>-tty-linux.zip -d ~/.local/share
ln -s ~/.local/share/Meltype-tty/meltype-tty ~/.local/bin/meltype-tty
```

## 使い方

```sh
meltype-tty            # $SHELL を起動
meltype-tty -- vim     # コマンドを指定して起動
```

`meltype-tty` の中では、どのコマンドでも [コードのモード](USAGE.md#日本語入力と英数) で動きます。日本語になるのは、コメント (`# ` の後ろなど) と文字列 (`"` `'` `` ` `` の中) だけで、`echo "konnnichiha` と打つと `echo "こんにちは` になります。端末には <kbd>半角/全角</kbd> などのキーが届かないので、コメント・文字列の外を日本語にする方法はありません。vim などで文章を書くときも同じです。

変換中の文字と候補はカーソルの位置に重ねて出し、カーソルの右にある文字は一時的に隠れます。

## 設定

設定画面はありません。Mozc と Zenzai は配布フォルダーに同梱され、変換は端末内で行います。Zenzai を使わないときは `config.json` に `"UseZenzai": false` と書きます。

設定と学習データは `~/.local/share/Meltype` にあり、設定は Windows 版と同じ `config.json` を直接編集します。先頭に `"$schema"` を書くと、エディターで項目の補完と説明が出ます。IBus・fcitx5 の版と同じ PC で使うと、設定と学習データを共有します。

```json
{
  "$schema": "./config.schema.json",
  "Punctuation": "FullWidthCommaPeriod"
}
```

## アンインストール

展開したフォルダー (`~/.local/share/Meltype-tty`) と、作った symlink (`~/.local/bin/meltype-tty`) を消します。設定と学習データ `~/.local/share/Meltype` も、要らなければ消してください。
