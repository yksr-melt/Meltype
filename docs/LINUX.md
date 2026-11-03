<p align="center">
  <img src="images/logo.png" alt="Meltype" width="360">
</p>

<a name="meltype-for-linux"></a>
<p align="center"><img src="images/headings/linux/title.svg" alt="Meltype for Linux" height="80"></p>

<p align="center"><b>雪解けのように、半角/全角の壁を溶かす日本語入力。</b></p>

<p align="center">
  <a href="https://github.com/yksr-melt/Meltype/releases/latest"><img src="https://img.shields.io/github/v/release/yksr-melt/Meltype?color=5ec4f0" alt="release"></a>
  <img src="https://img.shields.io/badge/Linux-IBus%20%2F%20fcitx5-7a96f0?logo=linux&logoColor=white" alt="IBus / fcitx5">
  <img src="https://img.shields.io/badge/プレビュー版-ff8ab4" alt="プレビュー版">
</p>

<p align="center">
  <a href="https://github.com/yksr-melt/Meltype/releases/latest"><b>ダウンロード</b></a>
  &nbsp;·&nbsp;
  <a href="../README.md">トップの README</a>
  &nbsp;·&nbsp;
  <a href="https://github.com/yksr-melt/Meltype/issues/new/choose">不具合の報告・提案</a>
</p>

<p align="center">
  <a href="WINDOWS.md"><img src="images/cards/os-windows.svg" alt="Windows" width="30%"></a>
  <a href="MACOS.md"><img src="images/cards/os-mac.svg" alt="Mac" width="30%"></a>
  <a href="LINUX.md"><img src="images/cards/os-linux.svg" alt="Linux" width="30%"></a>
</p>

Linux 版のインストール・設定・困ったときの説明です。IBus と fcitx5 で動きます。キー操作と、どの OS でも共通の機能は [USAGE.md](USAGE.md) にあります。

> **プレビュー版です。** 動かないところがあります。気づいたことはなんでも [Issues](https://github.com/yksr-melt/Meltype/issues) で教えてください！

<br>

<a name="動く環境"></a>
<img src="images/headings/linux/01.svg" alt="動く環境" height="53"><br>

- Ubuntu 24.04・Debian・Fedora 44 など (x86_64 / ARM64)
- 入力の仕組みが IBus (GNOME、Ubuntu の標準) か fcitx5 (KDE など) のもの

fcitx5 のアドオンは、Ubuntu 24.04 の fcitx5 5.1 で作っています。版が大きく違う fcitx5 では動かないことがあります。

<br>

<a name="インストール"></a>
<img src="images/headings/linux/02.svg" alt="インストール" height="53"><br>

Ubuntu・Debian は apt で、Fedora は dnf で入れます。どちらも IBus と fcitx5 の両方に入れます。

**Ubuntu / Debian**

apt リポジトリから入れると、以後 `sudo apt update && sudo apt upgrade` で更新できます。初回だけ、公開鍵とリポジトリを登録します。

```bash
sudo install -d -m 0755 /etc/apt/keyrings
curl -fsSL https://yksr-melt.github.io/Meltype/apt/meltype-archive-key.asc | sudo tee /etc/apt/keyrings/meltype-archive-key.asc >/dev/null
sudo chmod 0644 /etc/apt/keyrings/meltype-archive-key.asc
sudo tee /etc/apt/sources.list.d/meltype.sources >/dev/null <<'EOF'
Types: deb
URIs: https://yksr-melt.github.io/Meltype/apt
Suites: stable
Components: main
Architectures: amd64 arm64
Signed-By: /etc/apt/keyrings/meltype-archive-key.asc
EOF
sudo apt update
sudo apt install meltype
```

リポジトリを登録せずに入れるときは、[Releases](https://github.com/yksr-melt/Meltype/releases/latest) から CPU に合う `.deb` (x86_64 は `meltype_<version>_amd64.deb`、ARM64 は `meltype_<version>_arm64.deb`) をダウンロードして、次のコマンドでインストールします。

```bash
sudo apt install ./meltype_<version>_amd64.deb
```

IBus の部品は「おすすめ」なので、ふつうは一緒に入ります。

**Fedora** (Fedora 44 でビルドしています)

dnf リポジトリから入れると、以後 `sudo dnf upgrade` で更新できます。初回だけ、リポジトリを登録します (公開鍵は最初のインストールのときに確認を求められます)。

```bash
sudo dnf config-manager addrepo --from-repofile=https://yksr-melt.github.io/Meltype/rpm/meltype.repo
sudo dnf install meltype
```

リポジトリを登録せずに入れるときは、[Releases](https://github.com/yksr-melt/Meltype/releases/latest) から CPU に合う `.rpm` (x86_64 は `meltype-<version>-1.x86_64.rpm`、ARM64 は `meltype-<version>-1.aarch64.rpm`) をダウンロードして、次のコマンドでインストールします。

```bash
sudo dnf install ./meltype-<version>-1.x86_64.rpm
```

ほかのディストリビューション向けのパッケージはまだありません。ソースからビルドする方法は [linux/README.md](../linux/README.md) にあります。

以前の版の zip にあった `install.sh` で入れていても、先に外す必要はありません。apt / dnf で入れると、`install.sh` が入れたファイルはパッケージのものに置き換わり、apt / dnf で外すときにまとめて消えます。

<a name="ibus-のとき"></a>
<img src="images/headings/linux/s01.svg" alt="IBus のとき" height="40">

1. いったんログアウトしてログインし直す
2. 設定 → キーボード → 入力ソース →「+ 入力ソースを追加」→ 日本語 → Meltype を追加
3. 画面右上の入力ソースのメニュー (または <kbd>Super</kbd> + <kbd>Space</kbd>) で Meltype を選ぶ

<a name="fcitx5-のとき"></a>
<img src="images/headings/linux/s02.svg" alt="fcitx5 のとき" height="40">

1. いったんログアウトしてログインし直す (動いている fcitx5 は、起動し直すまで新しい Meltype を読み込みません)
2. fcitx5 の設定 (`fcitx5-configtool`) →「入力メソッド」で Meltype を追加する
3. <kbd>Ctrl</kbd> + <kbd>Space</kbd> などで Meltype に切り替える

<br>

<a name="使い方"></a>
<img src="images/headings/linux/03.svg" alt="使い方" height="53"><br>

<kbd>半角/全角</kbd> で英数と日本語を切り替えます。

設定は、アプリの一覧の「Meltype の設定」で変えられます。

設定と学習データは `~/.local/share/Meltype` にあります。画面に無い項目 (アプリ別設定など) は、Windows 版と同じ `config.json` を直接編集します。先頭に `"$schema"` を書くと、エディターで項目の補完と説明が出ます。

```json
{
  "$schema": "./config.schema.json",
  "Punctuation": "FullWidthCommaPeriod"
}
```

<br>

<a name="困ったとき"></a>
<img src="images/headings/linux/04.svg" alt="困ったとき" height="53"><br>

不具合を報告するときは、どのアプリで・何と打って・どうなったか (できればスクリーンショットも) を教えてください。

動かないときは、端末で次を実行してから操作すると、エラーが表示されます。表示された内容を送ってください (終わるときは <kbd>Ctrl</kbd> + <kbd>C</kbd>)。

```bash
ibus restart; sleep 2; pkill -f ibus-engine-meltype; /opt/meltype/ibus-engine-meltype
```

入れた後に入力ソースの一覧に Meltype が出てこないときは、いったんログアウトしてログインし直してください。

<br>

<a name="アンインストール"></a>
<img src="images/headings/linux/05.svg" alt="アンインストール" height="53"><br>

Ubuntu / Debian は `sudo apt remove meltype`、Fedora は `sudo dnf remove meltype` で外します (設定と学習データ `~/.local/share/Meltype` は残るので、要らなければ消してください)。
