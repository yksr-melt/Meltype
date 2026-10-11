Meltype for Linux テスト版

※ テスト版です。内容は公開しないでください。
※ 試作のため、動かないところがあります。気づいたことはなんでも教えてください。

Meltype は「入力ソース」(日本語入力、IBus のエンジン・fcitx5 のアドオン) です。下の手順でインストールして、入力ソースに追加して使います。

■ 動く環境
  Ubuntu 24.04 など (x86_64)。入力の仕組みが IBus (GNOME、Ubuntu の標準) か fcitx5 (KDE など) のもの
  (fcitx5 のアドオンは Ubuntu 24.04 の fcitx5 5.1 で作っています。版が大きく違う fcitx5 では動かないことがあります)

■ インストール
  1. この zip を展開する
  2. 「端末」(ターミナル) を開いて、展開したフォルダーで次を実行
       bash install.sh
     (管理者のパスワードを聞かれます。足りない部品があれば自動で入れます)
  IBus のとき:
  3. いったんログアウトしてログインし直す
  4. 設定 → キーボード → 入力ソース →「+ 入力ソースを追加」→ 日本語 → Meltype を追加
  5. 画面右上の入力ソースのメニュー (または Super + Space) で Meltype を選ぶ
  fcitx5 のとき (fcitx5 が入っていれば、install.sh が fcitx5 にも入れます。fcitx5 だけに入れるなら bash install.sh --fcitx5):
  3. fcitx5 の設定 (fcitx5-configtool) →「入力メソッド」で Meltype を追加する
  4. Ctrl + Space などで Meltype に切り替える

■ 使い方
  ・ふつうにローマ字で打つと日本語、英単語 (google、github …) は英字のまま
  ・Space で変換、Enter で確定、← → で文節を選ぶ、Esc で取り消し
  ・F6 ひらがな / F7 カタカナ / F8 半角カタカナ / F9 全角英数 / F10 半角英数
  ・半角/全角 キーで英数 ⇔ 日本語
  ・設定と学習データは ~/.local/share/Meltype にあります (config.json は Windows 版と同じ形式)

■ 不具合を報告するとき
  ・どのアプリで、何と打って、どうなったか (できればスクリーンショットも)
  ・動かないときは、端末で次を実行してから操作すると、エラーが表示されます
       ibus restart; sleep 2; pkill -f ibus-engine-meltype; /opt/meltype/ibus-engine-meltype
     (表示された内容を送ってください。終わるときは Ctrl + C)

■ アンインストール
  展開したフォルダーで
       bash uninstall.sh          (設定と学習データも消すなら bash uninstall.sh --data)
