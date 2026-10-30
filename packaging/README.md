# パッケージマネージャー向けの定義

配布チャネルごとのパッケージ定義です。**まだ実際の環境で入れて動かす確認はしていません** (このリポジトリの CI は、ここを検査していません)。

| ファイル | 対象 | 何をするか |
|---|---|---|
| `aur/meltype-bin/PKGBUILD` | Arch Linux (AUR) | リリースの zip を展開して `/opt/meltype` に置く (ビルドなし) |
| `pacstall/meltype.pacscript` | Debian / Ubuntu (pacstall) | リリースの `.deb` を展開する (ビルドなし) |

## それぞれを用意する理由

- **AUR (`meltype-bin`)**: Arch Linux には、いま Meltype を入れる配布経路がありません (zip を手で展開するしかない)。AUR があると `pacman` で入れ・更新・削除できます
- **pacstall (`meltype`)**: Debian / Ubuntu は、すでに apt リポジトリから入れられます (`linux/README.md` の手順で鍵とリポジトリを登録すると、`apt upgrade` で更新できます)。pacstall の定義は、**鍵とリポジトリの登録をせずに pacstall だけで入れたい人**と、pacstall を既に使っている人のための導線です。apt の代替ではありません

`meltype-ut` (Mozc-UT の辞書入りの変換ヘルパー) は、`makepkg` のときに Mozc を Bazel でビルドする必要があり、この PR には入れていません。AUR の `mozc-ut` と同じ形にできますが、ビルドに時間がかかるため、`meltype-bin` とは別に相談したいと考えています。

## 版を上げるとき

`pkgver` / `pkgversion` と `sha256sums` を、リリースの成果物に合わせて更新します。

```bash
# AUR
cd packaging/aur/meltype-bin
sha256sum Meltype-<版>-linux.zip Meltype-<版>-linux-arm64.zip   # リリースから落として確認
makepkg --printsrcinfo > .SRCINFO

# pacstall
sha256sum meltype_<版>_amd64.deb meltype_<版>_arm64.deb
```

`makepkg` は Arch で、`pacstall -I` は pacstall を入れた Debian / Ubuntu で確認してください。

## 確認してほしいこと

- `sudo pacman -U` / `pacstall -I meltype` で入り、IBus か fcitx5 の入力ソースに Meltype が出ること
- ログアウト→ログインで打てること
- 外すときに `/opt/meltype` と IBus のコンポーネントが残らないこと
- fcitx5 のアドオンが、そのディストリビューションの fcitx5 の版と合うこと (合わない場合は `optdepends` の案内だけにします)
