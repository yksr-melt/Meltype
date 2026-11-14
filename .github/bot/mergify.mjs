// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Mergify のコメントを日本語でまとめる bot (.github/workflows/mergify.yml から呼ぶ)。
//   Mergify (mergify[bot]) がマージキューの状況のコメントを書いた・書き換えたら、状態と時刻だけを日本語にして
//   bot のコメント 1 つに書く。理由や条件などの詳しいことは訳さず、Mergify のコメントへのリンクで済ませる。
//   チェックボックスのコメントは .mergify.yml の queue_controls_comment: false で出さない。
// Pull Request のコードは動かさない。GitHub の API で、Mergify のコメントの本文を読むだけ。
import fs from 'node:fs';

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;

const Marker = '<!-- meltype-bot --><!-- mergify -->';
const Author = 'mergify[bot]';

/**
 * キューの状況の state の日本語。Mergify の英語の文面は読まず、コメントの先頭の Mergify Payload (JSON) だけを読む。
 * 文面は Mergify がよく変えるが、Payload は機械向けで変わりにくいため。無い state は名前をそのまま出す。
 */
const States = {
  queued: '🕐 マージキューに入っています',
  waiting_for_ci: '🕐 マージキューで CI を待っています',
  running_checks: '🕐 マージキューで CI を動かしています',
  merged: '✅ マージしました',
  dequeued: '⚠️ マージキューから外れました',
  failed: '❌ マージキューで失敗しました',
};

/** ISO の時刻を日本時間の「11/14 21:41」にする。 */
const time = iso => iso && new Date(iso).toLocaleString('ja-JP', { timeZone: 'Asia/Tokyo', month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' });

function payloadOf(body) {
  const match = /-\*- Mergify Payload -\*-\s*([\s\S]*?)\s*-\*- Mergify Payload End -\*-/.exec(body ?? '');
  return match ? JSON.parse(match[1]) : null;
}

/** キューの状況の日本語の行。 */
function message(payload, url) {
  const lines = [
    `### ${States[payload.state] ?? `マージキューの状態: \`${payload.state}\``}`,
    '',
    payload.queued_at && `- キューに入った時刻: ${time(payload.queued_at)}`,
    payload.estimated_time_of_merge && `- マージの見込み: ${time(payload.estimated_time_of_merge)}`,
    payload.left_at && `- キューを出た時刻: ${time(payload.left_at)}`,
    '',
    `<sub>時刻は日本時間です。理由や条件などの詳しいことは、[Mergify のコメント](${url}) を見てください。</sub>`,
  ];
  return lines.filter(l => typeof l === 'string').join('\n');
}

async function gh(method, url, body) {
  const response = await fetch(`${api}/repos/${repo}${url}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-bot' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`${method} ${url}: ${response.status} ${await response.text()}`);
  return response.json();
}

async function main() {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const comments = [];
  for (let page = 1; page <= 10; page++) {
    const batch = await gh('GET', `/issues/${event.issue.number}/comments?per_page=100&page=${page}`);
    comments.push(...batch);
    if (batch.length < 100) break;
  }
  // キューの状況のコメントは PR に 1 つで、Mergify が書き換えていく
  const status = comments.find(c => c.user?.login === Author && payloadOf(c.body));
  if (!status) return console.log('Mergify のキューの状況のコメントがありません');
  const body = `${Marker}\n${message(payloadOf(status.body), status.html_url)}`;
  const mine = comments.find(c => c.user?.type === 'Bot' && c.body?.includes(Marker));
  if (mine?.body === body) return;
  if (mine) await gh('PATCH', `/issues/comments/${mine.id}`, { body });
  else await gh('POST', `/issues/${event.issue.number}/comments`, { body });
}

/** node .github/bot/mergify.mjs --self-test: 日本語にする部分だけを確かめる (GitHub には何も書かない)。 */
function selfTest() {
  let failed = 0;
  const check = (name, ok) => { if (!ok) failed++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`); };
  // #1886 の Mergify のコメントの先頭
  const merged = '<!---\nDO NOT EDIT\n-*- Mergify Payload -*-\n{"version": 1, "state": "merged", "queue_rule_name": "main", "queued_at": "2026-11-14T12:41:15.442558+00:00", "estimated_time_of_merge": null, "speculative_check_pr": null, "required_conditions": [], "left_at": "2026-11-14T12:41:23.914996+00:00"}\n-*- Mergify Payload End -*-\n-->\n\n### ✅ Merged into `main`';
  const text = message(payloadOf(merged), 'https://example.com/1');
  check('state を日本語にする', text.startsWith('### ✅ マージしました'));
  check('時刻を日本時間で出す', text.includes('キューに入った時刻: 11/14 21:41'));
  check('null の項目は出さない', !text.includes('見込み'));
  check('知らない state は名前を出す', message({ state: 'something_new' }, '').includes('`something_new`'));
  check('Payload の無いコメントは読まない', payloadOf('Tick the box') === null);
  if (failed) process.exit(1);
}

if (process.argv[2] === '--self-test') selfTest();
else await main();
