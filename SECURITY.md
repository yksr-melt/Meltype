<a name="セキュリティについて"></a>
<img src="docs/images/headings/security/title.svg" alt="セキュリティについて" height="80">


Meltype はキーボードの入力を扱うソフトです。安心して使ってもらえるように、次の方針で作っています。

<br>

<a name="meltype-がすることしないこと"></a>
<img src="docs/images/headings/security/01.svg" alt="Meltype がすること・しないこと" height="53"><br>


- **打った文字を外部に送りません。** 判定・変換・学習はすべて PC の中で行います。既定で通信するのは次の 2 つだけです。
  - 自動更新: GitHub に新しい版があるかを確かめる (送るのは今の版だけ)。設定「自動で更新する」で止められます。
  - 不具合の報告: トレイの「不具合の報告・提案...」から、利用者が自分でフォームや GitHub を開いたときだけ。
- 設定「#番号で GitHub の Issue・PR のタイトルを候補に出す」(既定は OFF) を ON にすると、`#123` を変換したときに、設定したリポジトリと番号を GitHub (api.github.com) に問い合わせます。打った文字は送りません。
- **パスワードの欄では何もしません。** Windows がパスワードの欄と知らせている入力欄では、キーを横取りしません。
- **ログに打った文字を残しません (既定)。** 設定「ログに入力した文字を残す」を ON にしたときだけ残します。OFF のときは文字数だけです
  (ただし判定の理由には、判定した語の先頭の数文字が残ります)。
- **Meltype 本体には管理者権限は要りません。** インストール先はユーザーのフォルダー (`%LOCALAPPDATA%\Programs\Meltype`) で、ふつうの権限で動きます。
- **Meltype IME (入力欄に直接入力) を入れるときだけ、管理者権限を求めます。** Windows に IME として登録するためです。IME の DLL は、ユーザーが書き換えられない `C:\Program Files\Meltype\tip` に置き、登録の前に配布したときのハッシュと合うかを確かめます。
  Meltype より高い権限で動いているアプリ (ふつうは管理者として動いているアプリ)・サインインや UAC の画面では、Meltype IME は何もしません。
  UAC の確認に見覚えが無いときは「いいえ」を選んでください (Meltype は変換ボックスで入力する方式で動きます)。設計と制限の詳細は [docs/dev/TSF_DESIGN.md](docs/dev/TSF_DESIGN.md) にあります。
- 保存するのは、設定・学習データ・ユーザー辞書 (`%LOCALAPPDATA%\Meltype`) と、ON にしたときのログ、異常終了と自動更新の記録だけです。

<br>

<a name="脆弱性の報告"></a>
<img src="docs/images/headings/security/02.svg" alt="脆弱性の報告" height="53"><br>


悪用できそうな問題 (打った文字が漏れる・ほかのアプリに勝手に入力される・権限が上がる など) を見つけたら、
**公開の Issue ではなく、非公開で報告してください。**

- GitHub: [Security → Report a vulnerability](https://github.com/yksr-melt/Meltype/security/advisories/new) (非公開で作者にだけ届きます)
- メール: ibutya0319@gmail.com (件名に「Meltype セキュリティ」と入れてください)

次のことを書いてもらえると助かります: 起きること、再現の手順、Meltype の版と OS、考えられる影響。
受け取ったら 1 週間以内に返事をし、直すまでの見通しをお知らせします。直した版を出すまでは、内容を公開しないでください。
直した版の公開後、希望があれば報告してくれた方のお名前をリリースノートに載せます。

<br>

<a name="対象の版"></a>
<img src="docs/images/headings/security/03.svg" alt="対象の版" height="53"><br>


直すのは最新の版だけです。古い版を使っている場合は、最新の版で起きるかを確かめてください。

<br>

<a name="配布物の確かめ方"></a>
<img src="docs/images/headings/security/04.svg" alt="配布物の確かめ方" height="53"><br>


- 配布しているのは GitHub のリリースと、各 OS の説明に書いた入れ方 (Homebrew・apt・dnf) だけです。ほかの場所の Meltype は使わないでください。
- 自動更新は、GitHub が公開している SHA-256 と一致するかを確かめてからインストールします。
- 公開版の実行ファイルには、作者のコード署名を付ける予定です。
