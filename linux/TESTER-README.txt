Meltype for Linux (プレビュー版)

Meltype は「入力ソース」(日本語入力) です。IBus と fcitx5 で動きます。

■ 動く環境
  Ubuntu 24.04 など。入力の仕組みが IBus (GNOME、Ubuntu の標準) か fcitx5 (KDE など) のもの

■ インストール
  Ubuntu・Debian・Fedora では、apt / dnf で入れてください (この zip は使いません)。
  入れ方: https://github.com/yksr-melt/Meltype/blob/main/docs/LINUX.md#インストール

  apt / dnf を使わずに入れるときは、Debian / Ubuntu 向けの旧来のスクリプトがあります
  (ほかのディストリビューションでの動作は保証しません)。
  1. この zip を展開する
  2. 「端末」(ターミナル) を開いて、展開したフォルダーで次を実行する (管理者のパスワードを聞かれます)
       bash install-legacy.sh
  IBus のとき:
  3. いったんログアウトしてログインし直す
  4. 設定 → キーボード → 入力ソース →「+ 入力ソースを追加」→ 日本語 → Meltype を追加
  fcitx5 のとき:
  3. fcitx5 の設定 (fcitx5-configtool) →「入力メソッド」で Meltype を追加する

■ 使い方・困ったとき
  ・Linux 版の説明: https://github.com/yksr-melt/Meltype/blob/main/docs/LINUX.md
  ・キー操作と機能: https://github.com/yksr-melt/Meltype/blob/main/docs/USAGE.md

■ アンインストール
  apt / dnf で入れたときは sudo apt remove meltype / sudo dnf remove meltype
  install-legacy.sh で入れたときは、展開したフォルダーで
       bash uninstall-legacy.sh   (設定と学習データも消すなら bash uninstall-legacy.sh --data)
