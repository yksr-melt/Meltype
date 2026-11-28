<p align="center">
  <img src="images/logo.png" alt="Meltype" width="360">
</p>

<a name="meltype-for-linux"></a>

# Meltype for Linux

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

Linux 版のインストール・設定・困ったときの説明です。IBus と fcitx5 で動きます。キー操作と、どの OS でも共通の機能は [USAGE.md](USAGE.md) にあります。GUI の無いコンソールで使う端末版は [TTY.md](TTY.md) にあります。

> **プレビュー版です。** 動かないところがあります。気づいたことはなんでも [Issues](https://github.com/yksr-melt/Meltype/issues) で教えてください！

<a name="動く環境"></a>

## 動く環境

- Ubuntu 24.04・Debian・Fedora 44・AlmaLinux 9 などの RHEL 9 系 (x86_64 / ARM64)
- glibc 2.34 以降、ICU。IBus は Python 3.9 以降と PyGObject も使います (パッケージから入れると依存関係も入ります)
- 入力の仕組みが IBus (GNOME、Ubuntu の標準) か fcitx5 (KDE など) のもの

fcitx5 のアドオンは、`.deb` は Ubuntu 24.04 の fcitx5 5.1、`.rpm` は Fedora 44 の fcitx5 5.1 で作っています。版が大きく違う fcitx5 では動かないことがあります。

GUI の無いコンソールでは、端末版 `meltype-tty` を使います。[TTY.md](TTY.md) を見てください。

<a name="インストール"></a>

## インストール

Ubuntu・Debian は apt で、Fedora と RHEL 9 系は dnf で、Arch Linux は AUR から入れます。IBus と fcitx5 の両方に入れます (RHEL 9 系は IBus だけ)。

以前の版の zip にあった `install.sh` / `uninstall.sh`（後の `*-legacy.sh`）は廃止しました。今後の zip にはインストール・アンインストール用スクリプトを同梱しません。以前のスクリプトで入れた人も、apt / dnf なら先に外す必要はありません (Arch Linux は下を見てください)。apt / dnf で入れると、旧スクリプトが配置したファイルはパッケージのものに置き換わり、apt / dnf で外すときにまとめて消えます。

<a name="ubuntu--debian"></a>

### Ubuntu / Debian

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

<a name="fedora"></a>

### Fedora

dnf リポジトリから入れると、以後 `sudo dnf upgrade` で更新できます。初回だけ、リポジトリを登録します (公開鍵は最初のインストールのときに確認を求められます)。

```bash
sudo dnf config-manager addrepo --from-repofile=https://yksr-melt.github.io/Meltype/rpm/meltype.repo
sudo dnf install meltype
```

AlmaLinux 9・Rocky Linux 9 などの RHEL 9 系では、リポジトリを次のコマンドで登録します。

```bash
sudo dnf install dnf-plugins-core
sudo dnf config-manager --add-repo https://yksr-melt.github.io/Meltype/rpm/meltype.repo
sudo dnf install meltype
```

fcitx5 のアドオンは別のパッケージ `meltype-fcitx5` です。fcitx5 が入っていれば、dnf が `meltype` と一緒に入れます。

リポジトリを登録せずに入れるときは、[Releases](https://github.com/yksr-melt/Meltype/releases/latest) から CPU に合う `.rpm` (x86_64 は `meltype-<version>-1.x86_64.rpm`、ARM64 は `meltype-<version>-1.aarch64.rpm`) をダウンロードして、次のコマンドでインストールします。fcitx5 で使うときは、`meltype-fcitx5-<version>-1.x86_64.rpm` も一緒に入れます。

```bash
sudo dnf install ./meltype-<version>-1.x86_64.rpm
sudo dnf install ./meltype-<version>-1.x86_64.rpm ./meltype-fcitx5-<version>-1.x86_64.rpm  # fcitx5 で使うとき
```

<a name="ほかのディストリビューション"></a>

### ほかのディストリビューション

Arch Linux は AUR の `meltype-bin` で入れられます。AUR ヘルパー (`yay` など) で入れると、以後 `yay -Syu` で更新できます。

```bash
yay -S meltype-bin
```

以前の zip の `install.sh` で入れていた場合は、同じ場所にファイルがあるため pacman が止まります。先にその zip の `uninstall.sh` (または `uninstall-legacy.sh`) で外してから入れてください。

外すときは `sudo pacman -Rns meltype-bin` です。fcitx5 のアドオンは Ubuntu 24.04 の fcitx5 向けにビルドしたものなので、Arch の fcitx5 では読み込めないことがあります。

それ以外のディストリビューション向けのパッケージはまだありません。[Releases](https://github.com/yksr-melt/Meltype/releases/latest) の `Meltype-<version>-linux.zip` (ARM64 は `Meltype-<version>-linux-arm64.zip`) に、ビルド済みのファイルが入っています。
置く場所と、`@DIR@` を置いた場所に書き換えるファイルは、[linux/build-deb.sh](../linux/build-deb.sh) と同じです。zip の fcitx5 のアドオン (`fcitx5/meltype.so`) は Ubuntu 24.04 の fcitx5 向けです。
ソースからビルドする方法は [linux/README.md](../linux/README.md) にあります。

<a name="ibus-のとき"></a>

### IBus のとき

1. いったんログアウトしてログインし直す
2. 設定 → キーボード → 入力ソース →「+ 入力ソースを追加」→ 日本語 → Meltype を追加
3. 画面右上の入力ソースのメニュー (または <kbd>Super</kbd> + <kbd>Space</kbd>) で Meltype を選ぶ

<a name="fcitx5-のとき"></a>

### fcitx5 のとき

1. いったんログアウトしてログインし直す (動いている fcitx5 は、起動し直すまで新しい Meltype を読み込みません)
2. fcitx5 の設定 (`fcitx5-configtool`) →「入力メソッド」で Meltype を追加する
3. <kbd>Ctrl</kbd> + <kbd>Space</kbd> などで Meltype に切り替える

<a name="使い方"></a>

## 使い方

<kbd>半角/全角</kbd> で英数と日本語を切り替えます。<kbd>英数</kbd>・<kbd>無変換</kbd> で英数に、<kbd>ひらがな/カタカナ</kbd>・<kbd>変換</kbd> で日本語になります。切り替えたときは、入力位置の近くに「あ」か「A」を少しの間出します。入力欄に入ったときも出すには、設定 `ModeIndicator` を `OnSwitchAndFocus` にします。

設定は、アプリの一覧の「Meltype の設定」で変えられます。

かな漢字変換は、同梱の Mozc で行います。Zenzai (llama.cpp の llama-server と zenz-v3.2-small、同梱) で、キャレットの前の文を文脈にして変換します (既定で ON。CPU で計算します)。CPU の負荷が気になるときは、設定の「Zenzai を使う」を OFF にしてください。Vulkan・CUDA 対応の llama-server を使う場合は、「Zenzai: 使う GPU」に `llama-server --list-devices` で表示される名前 (例: `Vulkan0`) を指定できます。自分の llama-server・モデルを使うときは、`config.json` の `"ZenzaiServerPath"`・`"ZenzaiModelPath"` に場所を書きます。

設定「カッコを打ったら中にカーソルを移動」が ON のとき、`「」`・`（）`・`()` などの空の括弧を <kbd>Enter</kbd> で確定すると、改行せず括弧の中へ移ります。

ユーザー辞書・定型文・学習データも「Meltype の設定」で扱います。設定と学習データは `~/.local/share/Meltype` にあります。

<a name="困ったとき"></a>

## 困ったとき

不具合を報告するときは、どのアプリで・何と打って・どうなったか (できればスクリーンショットも) を教えてください。

IBus で動かないときは、端末で次を実行してから操作すると、エラーが表示されます。表示された内容を送ってください (終わるときは <kbd>Ctrl</kbd> + <kbd>C</kbd>)。

```bash
ibus restart
sleep 2
pkill -f ibus-engine-meltype || true
/opt/meltype/ibus-engine-meltype
```

「Meltype の設定」が開かないときは、`~/.local/share/Meltype/crash.log` の内容を送ってください。

fcitx5 では、ログアウト・ログイン後も動かなければ、fcitx5 の設定に Meltype が追加されているか、IBus と fcitx5 を同時に使っていないかを確かめてください。

入れた後に入力ソースの一覧に Meltype が出てこないときは、いったんログアウトしてログインし直してください。

<a name="アンインストール"></a>

## アンインストール

Ubuntu / Debian は `sudo apt remove meltype`、Fedora・RHEL 9 系は `sudo dnf remove meltype meltype-fcitx5`、Arch Linux は `sudo pacman -Rns meltype-bin` で外します。dnf で fcitx5 のアドオンを入れていなければ `meltype` だけを指定します。設定と学習データ `~/.local/share/Meltype` は残るので、要らなければ消してください。
