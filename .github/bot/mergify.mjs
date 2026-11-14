// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Mergify のコメントを日本語でまとめる bot (.github/workflows/mergify.yml から呼ぶ)。
//   Mergify (mergify[bot]) が Pull Request にコメントを書いた・書き換えたら、その内容を日本語にして、
//   bot のコメント 1 つに書く (Mergify がコメントを書き換えるたびに、こちらも書き換える)。
//   Mergify のコメントはそのまま残す。チェックボックスなどの操作は Mergify のコメントにしか効かないため。
// Pull Request のコードは動かさない。イベントに入っている Mergify のコメントの本文を読むだけ。
import fs from 'node:fs';

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;

const Marker = '<!-- meltype-bot --><!-- mergify -->';
const Author = 'mergify[bot]';

/**
 * よく出る Mergify の文の日本語。英語の文が含まれていれば、その日本語を出す。
 * 無い文は、Mergify の英語をそのまま引用する (Mergify が文面を変えても、黙って消さないため)。
 */
const Phrases = [
  ['Tick the box to add this pull request to the merge queue',
    'Mergify のコメントの「Queue this pull request」にチェックを入れると、この Pull Request がマージキューに入ります (`@mergifyio queue` とコメントするのと同じです)。まだキューの条件を満たしていなければ、満たした時点でキューに入ります。'],
  ['Tick the box to remove this pull request from the merge queue',
    'Mergify のコメントのチェックを入れると、この Pull Request をマージキューから外します。'],
  ['has been queued', '🕐 マージキューに入りました。順番が来ると CI を確かめてからマージします。'],
  ['is queued', '🕐 マージキューに入っています。順番が来ると CI を確かめてからマージします。'],
  ['has been dequeued', '⚠️ マージキューから外れました。理由は Mergify のコメントを見てください。'],
  ['has been merged', '✅ マージキューからマージしました。'],
  ['has been removed from the queue', '⚠️ マージキューから外れました。理由は Mergify のコメントを見てください。'],
  ['checks have failed', '❌ キューで確かめた CI が失敗しました。'],
  ['conflict', '⚠️ main とのコンフリクトがあります。'],
];

/** Mergify のコメントの日本語。分かる文が無ければ null。 */
function translate(body) {
  const lower = String(body ?? '').toLowerCase();
  const found = Phrases.filter(([en]) => lower.includes(en.toLowerCase())).map(([, ja]) => ja);
  return found.length ? [...new Set(found)] : null;
}

/** 引用。Mergify の本文に @ があっても通知しないようにする。 */
const quote = text => String(text ?? '').replace(/@/g, '@​').split(/\r?\n/).map(l => `> ${l}`).join('\n');

function message(comment) {
  const ja = translate(comment.body);
  return [
    '### 🤖 Mergify のお知らせ (日本語)',
    '',
    ...(ja ?? ['日本語の説明はまだありません。Mergify の英語のコメントを引用します。', '', quote(comment.body)]),
    '',
    `<sub>[Mergify のコメント](${comment.html_url}) を日本語にしたものです。操作は Mergify のコメントでしてください。</sub>`,
  ].join('\n');
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
  const { comment, issue } = event;
  if (comment.user?.login !== Author) return console.log(`Mergify のコメントではありません (${comment.user?.login})`);
  const body = `${Marker}\n${message(comment)}`;
  const comments = await gh('GET', `/issues/${issue.number}/comments?per_page=100`);
  const mine = comments.find(c => c.user?.type === 'Bot' && c.body?.includes(Marker));
  if (mine?.body === body) return;
  if (mine) await gh('PATCH', `/issues/comments/${mine.id}`, { body });
  else await gh('POST', `/issues/${issue.number}/comments`, { body });
}

/** node .github/bot/mergify.mjs --self-test: 日本語にする部分だけを確かめる (GitHub には何も書かない)。 */
function selfTest() {
  let failed = 0;
  const check = (name, ok) => { if (!ok) failed++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`); };
  const box = 'Tick the box to add this pull request to the merge queue (same as `@mergifyio queue`). If it does not match the queue conditions yet, it enters the queue once it does.\n\n- [ ] Queue this pull request <!-- mergify:queue-control:queue -->';
  const text = message({ body: box, html_url: 'https://example.com/1' });
  check('キューに入れるチェックボックスを日本語にする', text.includes('マージキューに入ります') && !text.includes('Tick the box'));
  const unknown = message({ body: 'Something new\nping @someone', html_url: 'https://example.com/2' });
  check('知らない文は英語を引用する', unknown.includes('> Something new') && unknown.includes('日本語の説明はまだありません'));
  check('引用で @ の通知をしない', !unknown.includes('@someone'));
  check('同じ日本語を 2 回出さない', translate('This pull request has been dequeued. has been dequeued').length === 1);
  if (failed) process.exit(1);
}

if (process.argv[2] === '--self-test') selfTest();
else await main();
