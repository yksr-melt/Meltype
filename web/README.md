# Meltype ホームページ

Meltype の紹介・使い方ガイド・タイピングと、「問題を報告する」フォーム・報告ダッシュボードです。静的ページと Cloudflare Workers・D1 を組み合わせています。

## テスト

```bash
pnpm install
cp .dev.vars.example .dev.vars
pnpm test
pnpm run dev
```

→ `http://localhost:8787`

`pnpm test` は D1 の代わりに `node:sqlite` を使うので、Node 22.13 以降で動かしてください (CI は Node 24)。`.dev.vars.example` には手元用の値が入っています。Turnstile は Cloudflare のテスト用キーで、テスト用キーの hostname は `example.com` になるので `TURNSTILE_HOSTNAMES` は空にしてあります。`DASHBOARD_DEV_BYPASS=1` のときは、localhostからのダッシュボードだけアクセス確認を飛ばします。

## 使い方ガイド

`/guide/` の使い方ガイドは、`docs/` の利用者向けの文書 (`USAGE.md`・`WINDOWS.md`・`MACOS.md`・`LINUX.md`) から `scripts/make-guide.mjs` で作ります。中身を変えるときは `docs/` の Markdown を直してください (`public/guide/` は作るたびに作り直すので、コミットしません)。

```bash
pnpm run guide
```

`pnpm run dev`・`pnpm run deploy` も先に作り直します。ガイドの描画は Markdown の見出し・表・コードブロックに対応し、Mermaid の描画は組み込んでいません。使い方の比較は表で書き、Mermaid の構成図は開発文書で使います。ほかの文書へのリンクはガイドのページに、ガイドに無い文書・ソースへのリンクは GitHub に直します。

## 画像を作り直す

リンクを貼ったときの画像 (`public/og.png`) は `scripts/og.html` をブラウザーで 1200x630 に描いて作ります。文字は Google Fonts の Zen Maru Gothic なので、ネットにつながった状態で実行してください。

```bash
node scripts/make-images.mjs
```

Chrome / Edge / Chromium が見つからないときは `CHROMIUM_PATH` に場所を入れてください。`logo-small.png` は Windows の PowerShell で `scripts/make-images.ps1` を実行して作ります。

アイコン (雪の結晶 + M) の元は `public/favicon.svg` です。変えたら `node scripts/make-icons.mjs` で `apple-touch-icon.png`・`native/tip/meltype.ico` (IME と Meltype.exe)・`linux/icon.png`・`mac/Resources/icon.tiff` (メニューバー用の白黒テンプレート) を作り直してください。

## デプロイ

初回:

```bash
pnpm exec wrangler login
pnpm exec wrangler deploy
pnpm run db:migrate:remote
pnpm exec wrangler secret put IP_HASH_SALT
pnpm exec wrangler secret put TURNSTILE_SECRET
```

- **R2 は無くても動きます。** R2 を使えないときは `wrangler.jsonc` の `r2_buckets` をコメントにしておくと、報告の画面に添付の欄を出さず、文章だけ受け付けます (添付付きの送信は断ります)。R2 を有効にしたら (Cloudflare のダッシュボード → R2。支払い方法の登録が要ることがあります)、`r2_buckets` のコメントを外してデプロイし直すと、添付も受け付けます。
- 最初の `wrangler deploy` で D1 (`meltype-reports`・`meltype-typing`)・R2 (`meltype-report-files`)・KV (`CACHE`) が作られ、`database_id` と KV の `id` が `wrangler.jsonc` に書き戻されます。先に自分で作る場合は `pnpm exec wrangler d1 create meltype-reports`・`pnpm exec wrangler d1 create meltype-typing`・`pnpm exec wrangler r2 bucket create meltype-report-files`・`pnpm exec wrangler kv namespace create CACHE` を実行して、ID を `wrangler.jsonc` に手動で書いてください。
- `workers_dev` を `false` にしているので、`*.workers.dev` では開けません。Cloudflare のダッシュボード → Workers → `meltype-web` → 設定 → ドメインとルート で、`meltype.moe` をカスタム ドメインに追加してください。
- `IP_HASH_SALT` には長いランダムな文字列 (`openssl rand -base64 32` など) を入れてください。ない場合、報告は503で受け付けません。IPアドレスはこの値と合わせたハッシュにしてから、レートリミットのためにそれぞれ1日間保存します。
- Turnstile: Cloudflareのダッシュボード → Turnstileでウィジェットを作り (ホスト名は `meltype.moe`)、サイトキーを `wrangler.jsonc` の `TURNSTILE_SITE_KEY` に、シークレットキーを `wrangler secret put TURNSTILE_SECRET` で入れます。`TURNSTILE_SECRET` がない場合、報告は503で受け付けません。
- GitHub APIの回数制限が気になるときは、`pnpm exec wrangler secret put GITHUB_TOKEN` で読み取り専用のトークンを追加可能です。

`wrangler.jsonc` の `vars`:

| 名前 | 中身 |
|---|---|
| `GITHUB_REPO` | Stars・リリース・コントリビューターを読むリポジトリ |
| `SITE_ORIGIN` | og:image・og:url・canonicalの絶対URLに使うオリジン |
| `ACCESS_TEAM_DOMAIN`・`ACCESS_AUD` | Accessのチーム ドメインとアプリのAUDタグ |
| `TURNSTILE_SITE_KEY` | Turnstileのサイトキー |
| `TURNSTILE_HOSTNAMES` | 空でなければ、Turnstileの確認結果のhostnameがこの中にあるかも確かめます |
| `FILE_RETENTION_DAYS`・`REPORT_RETENTION_DAYS` | 添付と報告を残す日数 (30 日・180 日) |

2回目以降は `pnpm run deploy` で変更を反映しデプロイできます。

main に `web/` の変更が入ると、GitHub Actions (`.github/workflows/web.yml` の `deploy`) がテストの後に `pnpm run deploy` を実行して、自動でデプロイします。使うには、リポジトリの Settings → Environments に `web-production` を作り、次の secrets を入れてください (無いときはデプロイのジョブが失敗します)。

| 名前 | 中身 |
|---|---|
| `CLOUDFLARE_API_TOKEN` | Cloudflare の API トークン。テンプレート「Edit Cloudflare Workers」に、D1 の編集を足したもの |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare のアカウント ID |

`wrangler.jsonc` に D1・KV の ID を書いていないときは、wrangler がデプロイ済みの Worker の同じ名前のつながりを使います。初回のデプロイは手元で行ってください。

## 報告の受付について

[設定の利用アンケート](https://meltype.moe/report/?form=settings-survey) では、回答ごとに設定の名前と利用状況を聞きます。ダッシュボードの「設定の利用アンケート」に絞り込むと回答を読めます。設定名で検索できます。回答の受付と保存期間は報告と共通で、送信回数は別に数えます。

- 種類は 不具合・変換や判定の間違い・辞書に足してほしい語・改善や機能の提案 の 4 つで、GitHub の Issue のテンプレートに合わせています。
- 同じ IP (IPv6 は /64 ごと) から送れるのは10分に3件・1日に10件まで。超えた場合は`429 / Retry-After:` をレスポンスに含めます。数えるのと記録するのは 1 つの `INSERT ... SELECT ... WHERE` で行うので、同時に送られても上限を超えません。
- 添付は 3 ファイル・1 ファイル 10 MB・合計 20 MB まで、画像 (PNG・JPEG・WebP・GIF)・動画 (MP4・WebM)・ログ (.log・.txt)・設定 (.json) に限定しています。
- 添付はダッシュボードからしか開けず、`Content-Disposition: attachment`・`nosniff`・`sandbox` のCSPを付けて返します。
- 送信には Cloudflare Turnstile の確認が要ります。
- ダッシュボードから Issue にするとき (「GitHub でIssueを作る」「Issueの形でコピー」) は、連絡先・ログ・実行環境を除去します (`report-schema.js` の `PRIVATE_FIELDS`)。
- 添付は報告の受付のあとに1ファイルずつR2に送ります (WorkerのメモリとCPU時間に収めるため)。受付から1時間だけ使える番号で送り、全部届くと状態が「届いた」になります
- 1 時間ごとのCron Trigger (`src/cleanup.js`) で、受付時間が過ぎても届かなかった添付・30 日 (`FILE_RETENTION_DAYS`) より前の添付・180日 (`REPORT_RETENTION_DAYS`) より前の報告・1日より前のレートリミットの記録を消します。
- 隠した入力欄と、ページを開いてから3秒より早い送信は受け付けません。

## GitHub の情報のキャッシュ

トップページの Stars・ダウンロード数・リリースなど (`/api/github`) は、Workers KVの `CACHE` に入れて1時間使い回します。1時間を過ぎたら古いものを返しつつ裏で取り直し、KVの値そのものは1日 (`expirationTtl`) で消えます。GitHubが落ちていても 1日は古い値で表示でき、読めないときは数字の代わりに "-" で表示します。

## URL で項目を埋める

フォームはURLで項目を埋められます。`form` (bug / misdetection / dictionary / idea) か、GitHub のテンプレートの名前の `template` (1-bug.yml など) で種類を選び、他は項目の名前 (`os`・`version`・`environment`・`typed` など) で渡します。

```text
https://meltype.moe/report/?form=bug&os=Windows+11&version=1.4.0
```

## Cloudflare Access

`/dashboard`・`/api/dashboard/*` (報告の本文・連絡先・添付) は、Cloudflare Accessを通ったメンテナーだけが見られます。WorkerもAccessのJWT (`Cf-Access-Jwt-Assertion` ヘッダー) を署名・AUD・発行元・期限で確認するので、Accessの設定を外したりworkers.devから開いたりしても素通しになりません。

1. Zero Trust → Access → アプリケーション で「セルフホスト」のアプリを作り、`meltype.moe/dashboard` と `meltype.moe/api/dashboard` の 2 つのパスを入れる。ポリシーは「許可」で、Includeにメンテナー全員のメールアドレスを入れる。ログイン方法に GitHub を足すと、メンテナーは GitHub のアカウントで入れます
2. アプリの「Application Audiance (AUD) タグ」と、チーム ドメイン (`<チーム名>.cloudflareaccess.com`) を `wrangler.jsonc` の `ACCESS_AUD`・`ACCESS_TEAM_DOMAIN` に書く
3. どちらかが空のあいだ、ダッシュボードは 503 で閉じたままです (開けっぱなしにはなりません)

`wrangler.jsonc` で `workers_dev` と `preview_urls` を `false` にしています。Access は `meltype.moe` にしか掛からないため、`*.workers.dev` を開けると Access の外から入れてしまいます。

## タイピング (`/typing/`)

お手本の文 (`sentences/*.txt`) をローマ字で打つページです。文は、ふつうの文・硬めの文・ネットの会話 (短め)・日本語と英語が混ざった文から選びます。誰でも遊べ、打った記録は、どのキーが抜けやすいか・打ち間違えやすいかの調査に、同意した人だけが送ります。Meltype のアプリには入れず、このページだけで集めます。

- 同意するまで、打鍵はどこにも送りません。結果の画面で同意すると (Turnstile の action は `typing`)、その回の記録を送ります。同意したあとは聞き直さずに続けて送ります (送らないようにも切り替えられます)。参加に期限は無く、同じブラウザーなら同じ参加として記録します (同じ人の打ち間違いの傾向を追えるように)。同意文の条件を変えたら `public/assets/js/typing-consent.js` の版を上げ、前の版で同意した参加からは受け付けず、同意を聞き直します。
- 集めるのは、お手本の文の id・打鍵 (`[code, key, 押した時刻, 離した時刻]`、時刻は打ち始めからのミリ秒。Backspace の押しっぱなしは繰り返しのたびに 1 つ入り、離した時刻は null)・かかった時間と、任意の質問 (タッチタイピング・キーボードの配列) の答え、OS です。打った文字は送らず、打鍵から組み立てます (`public/assets/js/typing-check.js` の `replay`。直したあとの文字列を、打った文字列と取り違えないように)。自由に書いた文章を送る欄はありません。
- 1 文の打鍵は読み 1 字あたり 12 打までにして、1 文の書き込みをお手本の長さで抑えます。1 つの参加から送れるのは 1 日に 1000 文までです。参加を始められるのは 1 つの IP から 10 分に 3 回・1 日に 6 回で、報告とは別に数えます (`rate_events.kind`)。
- タイピング調査のデータは、報告とは別の D1 (`TYPING_DB`、`meltype-typing`。マイグレーションは `migrations-typing/`) に入れます。タイピングのデータで容量が埋まっても、報告を受け付けられるようにするためです。回数制限 (`rate_events`) だけは報告と同じ D1 で数えます。
- 参加者は参加ごとのランダムな番号 (`typing_sessions.id`) で分けます。IP はハッシュにして回数の制限にだけ使い、調査のデータには入れません。
- お手本と関係ない入力 (名前など) が続く文は、ページが送らず、サーバーも受け付けません。お手本のローマ字のどの字の隣のキーでもない字が 3 打続くか、かなにしてお手本と違う字が 4 字続いたら止めます。
- 打鍵とお手本から計算できる値 (打った文字・一致率・打鍵の数・お手本の文と読み) は保存しません。読み替えの規則を直しても、古い行だけ古い規則の値で残らないように、使うたびに計算します。
- 一致率 (打った文字とお手本をかなにして比べる。英語の部分も同じ規則でかなにするので、正しく打てば一致する) が 50% 未満の文は、ダッシュボードの「お手本とかけ離れた送信」に出ます (新しい 5000 文から計算する)。名前など関係ない入力が残っていれば、その参加のかけ離れた文を消せます。
- 調査は雪代 (Yukishiro) が Meltype のために行い、データの持ち主も雪代です (同意文に書いてある。問い合わせ先は Meltype の Discord と GitHub の Issue)。
- 研究用に公開するときは、集めたデータ (お手本の文を含む) を CC BY-SA 4.0 で公開します (同意文にもそう書いてある。GPLv3 へは一方向に互換があるので、Meltype の学習にも使える)。同意文の条件を変えると、それより前に送られたデータは新しい条件に同意していないので、変えるときは前のデータの扱いも決める。
- データは消さずに残します (Cron の掃除の対象外)。ダッシュボードにお手本の文ごとに打たれた数を出し、JSON Lines で書き出せます (書き出すときに、お手本の文と読みを id から引いて付ける) (`/api/dashboard/typing/*`、Access の内側)。
- お手本は `sentences/*.txt` に、1 行に 1 文の「表示する文|読み」の形で書きます。ファイル名は「並び順-種類の id.txt」(例: `3-chat.txt`) で、先頭に「種類: 表示名」の行を書きます。新しい種類は、ファイルを足せばページの選択肢に出ます。読みはひらがなと「ー、。！？〜…「」（）」(英語の部分は英小文字・数字・「-」・空白) で 40 字まで (「！？〜「」（）」は `!` `?` `~` `[` `]` `(` `)`、「…」は `.` を 3 回打つ)、文と読みの「、。」の数をそろえます。`pnpm run sentences` (`dev`・`test`・`deploy` でも動く) が確かめて `public/assets/js/typing-sentences.js` を作ります (コミットしない)。
- 文の id は文と読みから作るので、直すと別の文になります。集めたデータは id でお手本を指すので、一度出したお手本の行は消しません。出すのをやめるときは、行の頭に「出さない:」を付けます (ページには出なくなり、id からは引ける)。
