Meltype for Mac テスト版

※ テスト版です。内容は公開しないでください。
※ 試作のため、動かないところがあります。気づいたことはなんでも教えてください。

Meltype は「入力ソース」(日本語入力) です。アプリ本体の Meltype.app.zip は手動で展開せず、そのまま保管してください。
展開済みのアプリを Downloads などに残すと、macOS が別の入力ソースとして検出することがあります。
インストール済みの ~/Library/Input Methods/Meltype.app を残し、Downloads などにある古い展開済みの Meltype.app は削除して構いません。
保管したい旧版は ZIP に圧縮し、展開済みコピーを残さないでください。
下の手順でインストールして、入力ソースに追加して使います。

■ 動く環境
  macOS 13 以降、Apple シリコン (M1 / M2 / M3 / M4 …) の Mac

■ インストール
  1. この zip を展開する
  2. 「Install Meltype.command」をダブルクリック
       「開発元が未確認のため開けません」と出たら、右クリック (control + クリック) →「開く」→「開く」
       (macOS 15 以降は、一度ダブルクリックしてから システム設定 → プライバシーとセキュリティ →
        下のほうの「このまま開く」を押す)
       (それでも開けないときは、「ターミナル」を開いて次の 1 行を貼り付けて Enter:
          bash ~/Downloads/Meltype-mac/install.sh  )
  3. インストーラーが Meltype を登録・起動し、入力ソースを選びます。
     通常の更新ではシステム設定を開く必要はありません。
     install-app.sh、start-input-method.sh、select-input-source.swift も必要なので、zip 全体を展開してください。
     起動エラーが出た場合は入力メニューから ABC または標準の日本語入力を選んでください。
     初回に入力ソースが見つからない場合だけ、ログアウト・ログイン後に入力メニューを確認してください。
  5. 「絵文字と記号を表示」「キーボードビューアを表示」「キーボード設定を開く…」の 3 つしか出ないときは、
     入力メニューの表示が古いだけです。いったん「キーボードビューアを表示」を押すと Meltype が出てきます
     (入力ソースとして登録されていても、この表示になることがあります)

■ 使い方
  ・ふつうにローマ字で打つと日本語、英単語 (google、github …) は英字のまま
  ・Space で変換、Enter で確定、← → で文節を選ぶ、Esc で取り消し
  ・F6 ひらがな / F7 カタカナ / F9 全角英数 / F10 半角英数
  ・JIS キーボードの「英数」キーで英数、「かな」キーで日本語

■ 不具合を報告するとき
  ・どのアプリで、何と打って、どうなったか (できればスクリーンショットも)
  ・動きがおかしいときのログ: ターミナルで次を実行してから操作すると、ログが流れます
       log stream --predicate 'process == "Meltype"' --level debug

■ 止まってしまったら
  まず入力メニューで ABC または標準の日本語入力を選びます。
  展開したフォルダーで次を実行して再起動できます:
       bash ./start-input-method.sh "$HOME/Library/Input Methods/Meltype.app"
       swift ./select-input-source.swift

■ アンインストール
  入力ソースから Meltype を外してから、ターミナルで
       launchctl remove io.github.yksr-melt.Meltype.manual
       rm -rf ~/Library/Input\ Methods/Meltype.app
  設定と学習データも消すなら
       rm -rf ~/Library/Application\ Support/Meltype
