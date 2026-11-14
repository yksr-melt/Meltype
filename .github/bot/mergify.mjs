// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Mergify のコメントを日本語でまとめる bot (.github/workflows/mergify.yml から呼ぶ)。
//   Mergify (mergify[bot]) が Pull Request にコメントを書いた・書き換えたら、その Pull Request の Mergify のコメントを
//   すべて日本語にして、bot のコメント 1 つにまとめる (毎回すべて読み直すので、こちらで状態を持たない)。
//   Mergify のコメントはそのまま残す。チェックボックスなどの操作は Mergify のコメントにしか効かないため。
// Pull Request のコードは動かさない。GitHub の API で、Mergify のコメントの本文を読むだけ。
import fs from 'node:fs';

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;

const Marker = '<!-- meltype-bot --><!-- mergify -->';
const Author = 'mergify[bot]';

/** キューの状況のコメントの state (コメントの先頭の Mergify Payload)。無い state は英語の見出しを出す。 */
const States = {
  queued: '🕐 マージキューに入っています',
  waiting_for_ci: '🕐 マージキューで CI を待っています',
  running_checks: '🕐 マージキューで CI を動かしています',
  merged: '✅ マージしました',
  dequeued: '⚠️ マージキューから外れました',
  failed: '❌ マージキューで失敗しました',
};

/** 「So far」の各行の先頭の太字。無いものは英語のまま出す。 */
const Steps = {
  'Entered the queue': 'キューに入りました',
  'Checks skipped': 'CI を省きました (すでに main の最新を取り込んでいます)',
  'Checks running': 'CI を動かしています',
  'Checks passed': 'CI が通りました',
  'Checks failed': 'CI が失敗しました',
  'Merged': 'マージしました',
  'Dequeued': 'キューから外れました',
  'Left the queue': 'キューから外れました',
};

/** 状況のコメント以外の Mergify のコメント。英語の文が含まれていれば、その日本語を出す。 */
const Phrases = [
  ['Tick the box to add this pull request to the merge queue',
    'Mergify のコメントの「Queue this pull request」にチェックを入れると、この Pull Request がマージキューに入ります (`@mergifyio queue` とコメントするのと同じです)。まだキューの条件を満たしていなければ、満たした時点でキューに入ります。'],
  ['Naming a queue is deprecated',
    '⚠️ キューの名前を付けるのは非推奨になりました。`@mergifyio queue` とだけコメントしてください (条件に合う最初のキューに入ります)。'],
];

/** Markdown を崩さず、@ で通知しないようにする。 */
const plain = text => String(text ?? '').replace(/@/g, '@​');
const quote = text => plain(text).split(/\r?\n/).map(l => `> ${l}`).join('\n');

function payloadOf(body) {
  const match = /-\*- Mergify Payload -\*-\s*([\s\S]*?)\s*-\*- Mergify Payload End -\*-/.exec(body);
  return match ? JSON.parse(match[1]) : null;
}

/** キューの状況のコメント。 */
function status(body, payload) {
  const heading = /^### (.*)$/m.exec(body)?.[1] ?? '';
  const merged = /Merged into `([^`]+)` as `([^`]+)`/.exec(heading);
  const title = merged ? `✅ \`${merged[1]}\` にマージしました (\`${merged[2]}\`)` : States[payload.state] ?? plain(heading);
  const steps = [...body.matchAll(/^- (\S+) \*\*(.+?)\*\*(.*)$/gm)]
    .map(([, icon, label, rest]) => `- ${icon} **${Steps[label] ?? plain(label)}**${plain(rest)}`);
  const met = /(\d+) merge conditions? met/.exec(body);
  return [
    `**${title}**`,
    ...(steps.length ? ['', ...steps] : []),
    ...(met ? ['', `マージの条件を ${met[1]} 個満たしています。`] : []),
  ];
}

/** Mergify のコメント 1 つの日本語の行。分からなければ英語を引用する (文面が変わっても黙って消さないため)。 */
function translate(body) {
  const payload = payloadOf(body);
  if (payload) return status(body, payload);
  const found = Phrases.filter(([en]) => body.includes(en)).map(([, ja]) => ja);
  return found.length ? found : ['日本語の説明はまだありません。', '', quote(body)];
}

function message(comments) {
  return [
    '### 🤖 Mergify のお知らせ (日本語)',
    ...comments.flatMap(c => ['', ...translate(c.body ?? ''), `<sub>[元のコメント](${c.html_url})</sub>`]),
    '',
    '<sub>Mergify のコメントを日本語にしたものです。操作は Mergify のコメントでしてください。</sub>',
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
  const comments = [];
  for (let page = 1; page <= 10; page++) {
    const batch = await gh('GET', `/issues/${event.issue.number}/comments?per_page=100&page=${page}`);
    comments.push(...batch);
    if (batch.length < 100) break;
  }
  const mergify = comments.filter(c => c.user?.login === Author);
  if (!mergify.length) return console.log('Mergify のコメントがありません');
  const body = `${Marker}\n${message(mergify)}`;
  const mine = comments.find(c => c.user?.type === 'Bot' && c.body?.includes(Marker));
  if (mine?.body === body) return;
  if (mine) await gh('PATCH', `/issues/comments/${mine.id}`, { body });
  else await gh('POST', `/issues/${event.issue.number}/comments`, { body });
}

/** node .github/bot/mergify.mjs --self-test: 日本語にする部分だけを確かめる (GitHub には何も書かない)。 */
function selfTest() {
  let failed = 0;
  const check = (name, ok) => { if (!ok) failed++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`); };
  const box = 'Tick the box to add this pull request to the merge queue (same as `@mergifyio queue`). If it does not match the queue conditions yet, it enters the queue once it does.\n\n- [ ] Queue this pull request <!-- mergify:queue-control:queue -->';
  const deprecated = '> `queue main`\n\n⚠️ **Naming a queue is deprecated.** Post `@mergifyio queue` without a name instead.';
  // #1886 の Mergify のコメント (条件の一覧は短くした)
  const merged = '<!---\nDO NOT EDIT\n-*- Mergify Payload -*-\n{"version": 1, "state": "merged", "queue_rule_name": "main"}\n-*- Mergify Payload End -*-\n-->\n\n'
    + '### ✅ Merged into `main` as `4bbc87e` at 12:41 UTC\n\nLess than a minute in the queue, with no time running CI.\n\n**So far**\n\n'
    + '- ✅ **Entered the queue** Sat 14 Nov, 12:41 · rule `main` · triggered by @yksr-melt with the [`@mergifyio queue` command](https://example.com)\n'
    + '- ✅ **Checks skipped** · PR is already up-to-date\n- ✅ **Merged** 12:41 · `4bbc87e` · merge\n\n<details>\n<summary>5 merge conditions met</summary>\n\n- [x] `check-success = test`\n</details>';
  const text = message([box, deprecated, merged].map((body, i) => ({ body, html_url: `https://example.com/${i}` })));
  check('チェックボックスを日本語にする', text.includes('マージキューに入ります') && !text.includes('Tick the box'));
  check('キューの名前の非推奨を日本語にする', text.includes('非推奨'));
  check('マージした見出しを日本語にする', text.includes('`main` にマージしました (`4bbc87e`)'));
  check('経過の行を日本語にする', text.includes('**キューに入りました** Sat 14 Nov') && text.includes('**マージしました** 12:41'));
  check('満たした条件の数を出す', text.includes('マージの条件を 5 個満たしています'));
  check('@ で通知しない', !/@yksr-melt/.test(text));
  const queued = translate('<!---\n-*- Mergify Payload -*-\n{"state": "queued"}\n-*- Mergify Payload End -*-\n-->\n### Queued');
  check('payload の state で見出しを出す', queued[0] === '**🕐 マージキューに入っています**');
  const unknown = translate('Something new\nping @someone').join('\n');
  check('知らない文は英語を引用する', unknown.includes('> Something new') && !unknown.includes('@someone'));
  if (failed) process.exit(1);
}

if (process.argv[2] === '--self-test') selfTest();
else await main();
