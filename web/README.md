# Meltype ホームページ

Meltype の紹介用ウェブサイトと、「問題を報告する」フォーム・報告ダッシュボードです。Cloudflare Workerを使うなら動的サイトにしてもよかったのですが、雪代さんの返信を待っている間に静的で作り始めてしまったのでそのまま静的でいかせてもらいました。

## テスト

```bash
npm run dev
```

→ `http://localhost:8787`

## デプロイ

初回:

```bash
npx wrangler login
npx wrangler deploy
npm run db:migrate:remote
npx wrangler secret put IP_HASH_SALT
```

- 最初の `wrangler deploy` で D1 (`meltype-reports`) と R2 (`meltype-report-files`) が作られ、`database_id` が `wrangler.jsonc` に書き戻されます。先に自分で作る場合は `npx wrangler d1 create meltype-reports` と `npx wrangler r2 bucket create meltype-report-files` を実行して、`database_id` を `wrangler.jsonc` に手動で書いてください。
- `IP_HASH_SALT` には長いランダムな文字列を入力しててください。IPアドレスはこの値と合わせたハッシュにしてから、レートリミットのためにそれぞれ1日間保存します。
- GitHub APIの回数制限が気になるときは、`npx wrangler secret put GITHUB_TOKEN` で読み取り専用のトークンを入れられます (Optional)

2回目以降は `npm run deploy` で、表の変更を反映しデプロイできます。

## 報告の受付について

- 種類は 不具合・変換や判定の間違い・辞書に足してほしい語・改善や機能の提案 の 4 つで、GitHub の Issue のテンプレートに合わせています。
- 同じ IP (IPv6 は /64 ごと) から送れるのは 10分に3件・1日に10 件まで。超えた場合は`429 / Retry-After:` をレスポンスに含めます。
- 添付は 5 ファイル・合計50MBまで。画像 (PNG・JPEG・WebP・GIF)・動画 (MP4・WebM)・ログ (.log・.txt)・設定 (.json) だけです。
- 添付は報告の受付のあとに1ファイルずつR2に送ります (WorkerのメモリとCPU時間に収めるため)。受付から1時間だけ使える番号で送り、全部届くと状態が「届いた」になります
- 隠した入力欄と、ページを開いてから3秒より早い送信は受け付けません

## Meltype のアプリから送信

フォームは URL で項目を埋められます。`form` (bug / misdetection / dictionary / idea) か、GitHub のテンプレートの名前の `template` (1-bug.yml など) で種類を選び、ほかは項目の名前 (`os`・`version`・`environment`・`typed` など) で渡します。

アプリの `ProjectInfo.ReportForm` に次の形で入れると、今の置き換えの仕組み (`=Windows+11`・`=0.0.0`・`=ENV`) のまま使えます。

```text
https://meltype.moe/report/?form=bug&os=Windows+11&version=0.0.0&environment=ENV
```

## ダッシュボード読み取りを管理者限定にする(保留)

ダッシュボードと API は `/dashboard/` と `/api/dashboard/` の下にまとめてあります。Cloudflare Zero Trust の Access でこの 2 つのパスにポリシーを付ければ、コードを変えずに管理者だけにできます。報告が誰でも見れることに特に問題はないため、面倒ならマージしてデプロイだけして放置で構いません。