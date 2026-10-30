<p align="center">
  <img src="../docs/images/logo.png" alt="Meltype" width="360">
</p>

<a name="meltype-for-linux"></a>
<p align="center"><img src="../docs/images/headings/linux/title.svg" alt="Meltype for Linux" height="80"></p>

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
  <a href="../docs/WINDOWS.md"><img src="../docs/images/cards/os-windows.svg" alt="Windows" width="30%"></a>
  <a href="../mac/README.md"><img src="../docs/images/cards/os-mac.svg" alt="Mac" width="30%"></a>
  <a href="README.md"><img src="../docs/images/cards/os-linux.svg" alt="Linux" width="30%"></a>
</p>

Meltype は Linux では「入力ソース」(日本語入力) として動きます。IBus のエンジンと fcitx5 のアドオンの両方があります。
Windows 版と同じく、日本語を打っている途中の英単語は英字のまま出ます！

> **プレビュー版です。** 動かないところがあります。気づいたことはなんでも [Issues](https://github.com/yksr-melt/Meltype/issues) で教えてください！

<br>

<a name="動く環境"></a>
<img src="../docs/images/headings/linux/01.svg" alt="動く環境" height="53"><br>

- Ubuntu 24.04 など (x86_64 / ARM64)
- 入力の仕組みが IBus (GNOME、Ubuntu の標準) か fcitx5 (KDE など) のもの

fcitx5 のアドオンは、Ubuntu 24.04 の fcitx5 5.1 で作っています。版が大きく違う fcitx5 では動かないことがあります。

<br>

<a name="インストール"></a>
<img src="../docs/images/headings/linux/02.svg" alt="インストール" height="53"><br>

1. [Releases](https://github.com/yksr-melt/Meltype/releases/latest) から `Meltype-<version>-linux.zip` (ARM64 のパソコンは `Meltype-<version>-linux-arm64.zip`) をダウンロードして展開する
2. 「端末」(ターミナル) を開いて、展開したフォルダーで次を実行する (管理者のパスワードを聞かれます。足りない部品があれば自動で入れます)
   ```bash
   bash install.sh
   ```

Debian / Ubuntu では、`.deb` からも入れられます (apt で外せます。中身は `install.sh` で入れるものと同じです)。
[Releases](https://github.com/yksr-melt/Meltype/releases/latest) から CPU に合う `.deb` をダウンロードして、次のコマンドでインストールします。

x86_64:

```bash
sudo apt install ./meltype_<version>_amd64.deb
```

ARM64:

```bash
sudo apt install ./meltype_<version>_arm64.deb
```

`.deb` は IBus と fcitx5 の両方に入れます (IBus の部品は「おすすめ」なので、ふつうは一緒に入ります)。

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

apt リポジトリがまだ公開されていない場合は、上の Releases から `.deb` を入れる方法を使ってください.

Fedora では、`.rpm` から入れられます (dnf で外せます。中身は `.deb` と同じです。Fedora 44 でビルドしています)。
[Releases](https://github.com/yksr-melt/Meltype/releases/latest) から CPU に合う `.rpm` をダウンロードして、次のコマンドでインストールします。

x86_64:

```bash
sudo dnf install ./meltype-<version>-1.x86_64.rpm
```

ARM64:

```bash
sudo dnf install ./meltype-<version>-1.aarch64.rpm
```

dnf リポジトリから入れると、以後 `sudo dnf upgrade` で更新できます。初回だけ、リポジトリを登録します (公開鍵は最初のインストールのときに確認を求められます)。

```bash
sudo dnf config-manager addrepo --from-repofile=https://yksr-melt.github.io/Meltype/rpm/meltype.repo
sudo dnf install meltype
```

<a name="ibus-のとき"></a>
<img src="../docs/images/headings/linux/s01.svg" alt="IBus のとき" height="40">

1. いったんログアウトしてログインし直す
2. 設定 → キーボード → 入力ソース →「+ 入力ソースを追加」→ 日本語 → Meltype を追加
3. 画面右上の入力ソースのメニュー (または <kbd>Super</kbd> + <kbd>Space</kbd>) で Meltype を選ぶ

<a name="fcitx5-のとき"></a>
<img src="../docs/images/headings/linux/s02.svg" alt="fcitx5 のとき" height="40">

fcitx5 が入っていれば、`install.sh` が fcitx5 にも入れます (fcitx5 だけに入れるなら `bash install.sh --fcitx5`)。

1. fcitx5 の設定 (`fcitx5-configtool`) →「入力メソッド」で Meltype を追加する
2. <kbd>Ctrl</kbd> + <kbd>Space</kbd> などで Meltype に切り替える

動いている fcitx5 があれば、`install.sh` は Meltype を読み込ませます (入れ直したときは、新しい版に切り替えます)。
「新しい Meltype を読み込ませられませんでした」と出たときは、案内のとおり fcitx5 を起動し直してください。
KDE Plasma の Wayland では fcitx5 を KWin が起動しているため、ログアウトしてログインし直すのが確実です。

<br>

<a name="使い方"></a>
<img src="../docs/images/headings/linux/03.svg" alt="使い方" height="53"><br>

ふつうにローマ字で打つと日本語、英単語 (google、github …) は英字のまま！

| キー | |
|---|---|
| <kbd>Space</kbd> | 変換 |
| <kbd>Enter</kbd> | 確定 |
| <kbd>←</kbd> <kbd>→</kbd> | 文節を選ぶ |
| <kbd>Esc</kbd> | 取り消し |
| <kbd>F6</kbd> / <kbd>F7</kbd> / <kbd>F8</kbd> | ひらがな / カタカナ / 半角カタカナ |
| <kbd>F9</kbd> / <kbd>F10</kbd> | 全角英数 / 半角英数 |
| <kbd>半角/全角</kbd> | 英数と日本語の切り替え |

設定と学習データは `~/.local/share/Meltype` にあります (`config.json` は Windows 版と同じ形式)。

設定は `config.json` を直接編集します。Meltype は同じフォルダーに設定の一覧と説明 (`config.schema.json`) を書くので、`config.json` の先頭で指定すると、VS Code などのエディターで項目の補完・説明・書き間違いの警告が出ます。`"SettingsVersion"` の無い `config.json` は、Meltype が最初に読んだときに、書いていない項目を既定値で書き足します。

fcitx5 の固定の操作ガイドを隠すには、`config.json` に `"ShowGuide": false` を追加します (既定は `true`)。英字・予測・訂正の提案は OFF でも表示します。設定は新しい入力欄を作るときに読み込むので、既存の入力欄へ確実に反映するには fcitx5 を再起動してください。

```json
{
  "$schema": "./config.schema.json",
  "Punctuation": "FullWidthCommaPeriod"
}
```

<br>

<a name="困ったとき"></a>
<img src="../docs/images/headings/linux/04.svg" alt="困ったとき" height="53"><br>

不具合を報告するときは、どのアプリで・何と打って・どうなったか (できればスクリーンショットも) を教えてください。

動かないときは、端末で次を実行してから操作すると、エラーが表示されます。表示された内容を送ってください (終わるときは <kbd>Ctrl</kbd> + <kbd>C</kbd>)。

```bash
ibus restart; sleep 2; pkill -f ibus-engine-meltype; /opt/meltype/ibus-engine-meltype
```

入力ソースに出てこないときは、[WSL / Linux でビルドする](../docs/WSL.md#入力ソースに出てこないとき) の「入力ソースに出てこないとき」も見てください。

<br>

<a name="アンインストール"></a>
<img src="../docs/images/headings/linux/05.svg" alt="アンインストール" height="53"><br>

展開したフォルダーで次を実行します。

```bash
bash uninstall.sh
# 設定と学習データも消すなら
bash uninstall.sh --data
```

`.deb` で入れたときは `sudo apt remove meltype`、`.rpm` で入れたときは `sudo dnf remove meltype` で外します (設定と学習データ `~/.local/share/Meltype` は残るので、要らなければ消してください)。

<br>

<a name="開発者向け"></a>
<img src="../docs/images/headings/linux/06.svg" alt="開発者向け" height="53"><br>

| 部分 | 中身 |
| --- | --- |
| IBus のエンジン (`ibus-engine-meltype`, Python) | IBus からキーを受け取り、判定の本体に渡して、変換中の文字・候補・確定を入力欄に反映する |
| fcitx5 のアドオン (`fcitx5/`, C++) | fcitx5 からキーを受け取り、判定の本体に渡す |
| 判定の本体 (`libMeltypeNative.so`) | Windows 版と共通の C# の部分 (`src/Meltype.Core`) を NativeAOT で Linux 用のライブラリにしたもの |
| 漢字変換 | Mozc の変換ヘルパー (`native/mozc`) |

- ビルドとインストール → [docs/WSL.md](../docs/WSL.md) (`linux/build.sh`)
- 全体の仕組み・テスト → [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md)
