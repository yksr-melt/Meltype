# Meltype ホームページ

Meltype の紹介用ウェブサイトと、「問題を報告する」フォーム・報告ダッシュボードです。Cloudflare Workerを使うなら動的サイトにしてもよかったのですが、雪代さんの返信を待っている間に静的で作り始めてしまったのでそのまま静的でいかせてもらいました。

## テスト

```bash
cp .dev.vars.example .dev.vars
npm run dev
npm test
```

→ `http://localhost:8787`

`npm test` は D1 の代わりに `node:sqlite` を使うので、Node 22.13 以降で動かしてください (CI は Node 24)。`.dev.vars.example` には手元用の値が入っています。TurnstilehaCloudflareのテスト用キーで、テスト用キーの hostname は `example.com` になるので `TURNSTILE_HOSTNAMES` は空にしてあります。`DASHBOARD_DEV_BYPASS=1` のときは、localhostからのダッシュボードだけアクセス確認を飛ばします。

## 画像を作り直す

リンクを貼ったときの画像 (`public/og.png`) は `scripts/og.html` をブラウザーで 1200x630 に描いて作ります。文字は Google Fonts の Zen Maru Gothic なので、ネットにつながった状態で実行してください。

```bash
npm i --no-save playwright-core
node scripts/make-images.mjs
```

Chrome / Edge / Chromium が見つからないときは `CHROMIUM_PATH` に場所を入れてください。`logo-small.png` は Windows の PowerShell で `scripts/make-images.ps1` を実行して作ります。

アイコン (雪の結晶 + M) の元は `public/favicon.svg` です。変えたら `node scripts/make-icons.mjs` で `apple-touch-icon.png`・`native/tip/meltype.ico` (IME と Meltype.exe)・`linux/icon.png`・`mac/Resources/icon.tiff` (メニューバー用の白黒テンプレート) を作り直してください。

## デプロイ

初回:

```bash
npx wrangler login
npx wrangler deploy
npm run db:migrate:remote
npx wrangler secret put IP_HASH_SALT
npx wrangler secret put TURNSTILE_SECRET
```

- **R2 は無くても動きます。** R2 を使えないときは `wrangler.jsonc` の `r2_buckets` をコメントにしておくと、報告の画面に添付の欄を出さず、文章だけ受け付けます (添付付きの送信は断ります)。R2 を有効にしたら (Cloudflare のダッシュボード → R2。支払い方法の登録が要ることがあります)、`r2_buckets` のコメントを外してデプロイし直すと、添付も受け付けます。
- 最初の `wrangler deploy` で D1 (`meltype-reports`)・R2 (`meltype-report-files`)・KV (`CACHE`) が作られ、`database_id` と KV の `id` が `wrangler.jsonc` に書き戻されます。先に自分で作る場合は `npx wrangler d1 create meltype-reports`・`npx wrangler r2 bucket create meltype-report-files`・`npx wrangler kv namespace create CACHE` を実行して、ID を `wrangler.jsonc` に手動で書いてください。
- `workers_dev` を `false` にしているので、`*.workers.dev` では開けません。Cloudflare のダッシュボード → Workers → `meltype-web` → 設定 → ドメインとルート で、`meltype.moe` をカスタム ドメインに追加してください。
- `IP_HASH_SALT` には長いランダムな文字列 (`openssl rand -base64 32` など) を入れてください。ない場合、報告は503で受け付けません。IPアドレスはこの値と合わせたハッシュにしてから、レートリミットのためにそれぞれ1日間保存します。
- Turnstile: Cloudflareのダッシュボード → Turnstileでウィジェットを作り (ホスト名は `meltype.moe`)、サイトキーを `wrangler.jsonc` の `TURNSTILE_SITE_KEY` に、シークレットキーを `wrangler secret put TURNSTILE_SECRET` で入れます。`TURNSTILE_SECRET` がない場合、報告は503で受け付けません。
- GitHub APIの回数制限が気になるときは、`npx wrangler secret put GITHUB_TOKEN` で読み取り専用のトークンを追加可能です。

`wrangler.jsonc` の `vars`:

| 名前 | 中身 |
|---|---|
| `GITHUB_REPO` | Stars・リリース・コントリビューターを読むリポジトリ |
| `SITE_ORIGIN` | og:image・og:url・canonicalの絶対URLに使うオリジン |
| `ACCESS_TEAM_DOMAIN`・`ACCESS_AUD` | Accessのチーム ドメインとアプリのAUDタグ |
| `TURNSTILE_SITE_KEY` | Turnstileのサイトキー |
| `TURNSTILE_HOSTNAMES` | 空でなければ、Turnstileの確認結果のhostnameがこの中にあるかも確かめます |
| `FILE_RETENTION_DAYS`・`REPORT_RETENTION_DAYS` | 添付と報告を残す日数 (30 日・180 日) |

2回目以降は `npm run deploy` で変更を反映しデプロイできます。

## 報告の受付について

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

## Meltype のアプリから送信

フォームはURLで項目を埋められます。`form` (bug / misdetection / dictionary / idea) か、GitHub のテンプレートの名前の `template` (1-bug.yml など) で種類を選び、他は項目の名前 (`os`・`version`・`environment`・`typed` など) で渡します。

アプリの `ProjectInfo.ReportForm` に次の形で入れれば、今の置き換えの仕組み (`=Windows+11`・`=0.0.0`・`=ENV`) のまま使えます。

```text
https://meltype.moe/report/?form=bug&os=Windows+11&version=0.0.0&environment=ENV
```

## Cloudflare Access

`/dashboard`・`/api/dashboard/*` (報告の本文・連絡先・添付) は、Cloudflare Accessを通ったメンテナーだけが見られます。WorkerもAccessのJWT (`Cf-Access-Jwt-Assertion` ヘッダー) を署名・AUD・発行元・期限で確認するので、Accessの設定を外したりworkers.devから開いたりしても素通しになりません。

1. Zero Trust → Access → アプリケーション で「セルフホスト」のアプリを作り、`meltype.moe/dashboard` と `meltype.moe/api/dashboard` の 2 つのパスを入れる。ポリシーは「許可」で、Includeにメンテナー全員のメールアドレスを入れる。ログイン方法に GitHub を足すと、メンテナーは GitHub のアカウントで入れます
2. アプリの「Application Audiance (AUD) タグ」と、チーム ドメイン (`<チーム名>.cloudflareaccess.com`) を `wrangler.jsonc` の `ACCESS_AUD`・`ACCESS_TEAM_DOMAIN` に書く
3. どちらかが空のあいだ、ダッシュボードは 503 で閉じたままです (開けっぱなしにはなりません)

`wrangler.jsonc` で `workers_dev` と `preview_urls` を `false` にしています。Access は `meltype.moe` にしか掛からないため、`*.workers.dev` を開けると Access の外から入れてしまいます。
