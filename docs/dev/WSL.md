<a name="wsl--linux-でビルドする"></a>
<img src="images/headings/wsl/title.svg" alt="WSL / Linux でビルドする" height="80">


WSL の Ubuntu では、OS に依存しない部分のテストと、Linux 用の IBus のパッケージのビルドができます。
コマンドは、PowerShell や macOS ではなく、Ubuntu (WSL) のターミナルで実行してください。
できるのは Linux 用のパッケージです。Windows や Mac の入力メソッドとして入るわけではありません。

<br>

<a name="ビルドする"></a>
<img src="images/headings/wsl/01.svg" alt="ビルドする" height="53"><br>


```bash
cd ~/path/to/Meltype
bash linux/build-cli.sh --setup --all
```

`--all` は、本体のテスト → Mozc のヘルパーのビルド → パッケージの作成までをまとめて行います。できたものは次の場所に入ります。

```text
linux/build/Meltype-linux/
```

<br>

<a name="インストールする"></a>
<img src="images/headings/wsl/02.svg" alt="インストールする" height="53"><br>


ビルドした IBus のエンジンを、Linux の環境に入れます。

```bash
linux/build-cli.sh --install
```

一部だけ行うときは、次のように分けて実行できます。

```bash
linux/build-cli.sh --test            # テストだけ
bash linux/build-cli.sh --build      # ビルドだけ
linux/build-cli.sh --package         # パッケージを作るだけ
linux/build-cli.sh --mozc --package  # Mozc のヘルパーも作り直してパッケージにする
```

<br>

<a name="入力ソースに出てこないとき"></a>
<img src="images/headings/wsl/03.svg" alt="入力ソースに出てこないとき" height="53"><br>


入れた後に入力ソースの一覧に Meltype が出てこないときは、Linux のセッションを入り直してください。
エンジンの動きを調べるときは、次のように手で起動するとログが見られます。

```bash
ibus restart
sleep 2
pkill -f ibus-engine-meltype || true
/opt/meltype/ibus-engine-meltype --version   # 版だけ確認するとき
/opt/meltype/ibus-engine-meltype
```
