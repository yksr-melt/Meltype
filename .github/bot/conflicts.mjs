// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// コンフリクトのお知らせ (.github/workflows/conflicts.yml)。
//   node conflicts.mjs main      … main に push されたとき: 開いている Pull Request を全部調べる
//   node conflicts.mjs pr <番号>  … Pull Request が更新されたとき: その Pull Request を調べる
//
// 2 つのことを知らせる。
//   1. いまのコンフリクト: main とコンフリクトしている Pull Request に「コンフリクト」のラベルとコメントを付ける。
//      原因になった Pull Request (分かれば) と、両方で変えたファイルも書く。解消されたらラベルを外し、コメントを書き換える。
//   2. マージしたあとのコンフリクトの予告: 「この Pull Request をマージすると、どの Pull Request がコンフリクトするか」を、
//      その Pull Request にコメントする (マージの順番を決めるときに使う)。
//
// コンフリクトは git merge-tree で、手元の git の中だけで確かめる (作業ツリーには何も書かない)。
// Pull Request のコードは取ってくるだけで、実行はしない (ビルドもスクリプトも動かさない)。
import { execFileSync } from 'node:child_process';

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const NowMarker = '<!-- meltype-bot --><!-- conflict -->';
const ForecastMarker = '<!-- meltype-bot --><!-- conflict-forecast -->';
const Label = 'コンフリクト';
const MaxFiles = 20;

async function gh(method, url, body) {
  const response = await fetch(`${api}/repos/${repo}${url}`, {
    method,
    headers: {
      Authorization: `Bearer ${process.env.GITHUB_TOKEN}`,
      Accept: 'application/vnd.github+json',
      'User-Agent': 'meltype-bot',
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (response.status === 204) return null;
  if (!response.ok) throw new Error(`${method} ${url}: ${response.status} ${await response.text()}`);
  return response.json();
}

async function paged(url) {
  const all = [];
  for (let page = 1; page <= 10; page++) {
    const items = await gh('GET', `${url}${url.includes('?') ? '&' : '?'}per_page=100&page=${page}`);
    all.push(...items);
    if (items.length < 100) break;
  }
  return all;
}

// ---- git (手元だけ) ----

function git(args, { allowFail = false } = {}) {
  try {
    return { ok: true, out: execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 64 << 20 }) };
  } catch (e) {
    if (!allowFail) throw e;
    return { ok: false, out: e.stdout?.toString() ?? '', status: e.status };
  }
}

const ref = number => `refs/meltype-pr/${number}`;

/** 開いている Pull Request の head を取ってくる。取れたものの番号を返す。 */
function fetchHeads(numbers) {
  const fetched = new Set();
  for (let i = 0; i < numbers.length; i += 50) {
    const chunk = numbers.slice(i, i + 50);
    const specs = chunk.map(n => `+refs/pull/${n}/head:${ref(n)}`);
    if (git(['fetch', '--quiet', '--no-tags', 'origin', ...specs], { allowFail: true }).ok) {
      chunk.forEach(n => fetched.add(n));
    } else {
      // まとめて取れなかったときは 1 つずつ (消された fork などを飛ばす)
      for (const n of chunk) if (git(['fetch', '--quiet', '--no-tags', 'origin', `+refs/pull/${n}/head:${ref(n)}`], { allowFail: true }).ok) fetched.add(n);
    }
  }
  return fetched;
}

/** 2 つの commit をマージしたときに、コンフリクトするファイル (無ければ空の配列)。 */
function conflictFiles(a, b) {
  const result = git(['merge-tree', '--write-tree', '--name-only', '--no-messages', a, b], { allowFail: true });
  if (result.ok) return [];
  if (result.status !== 1) throw new Error(`merge-tree ${a} ${b} に失敗しました`);
  return result.out.split('\n').slice(1).map(s => s.trim()).filter(Boolean);
}

/** main に Pull Request をマージした commit を、手元だけで作る (コンフリクトするなら null)。 */
function mergedCommit(base, head) {
  const result = git(['merge-tree', '--write-tree', '--no-messages', base, head], { allowFail: true });
  if (!result.ok) return null;
  const tree = result.out.split('\n')[0].trim();
  return git(['commit-tree', tree, '-p', base, '-p', head, '-m', 'forecast']).out.trim();
}

function changedFiles(base, head) {
  const result = git(['diff', '--name-only', `${base}...${head}`], { allowFail: true });
  return new Set(result.ok ? result.out.split('\n').filter(Boolean) : []);
}

// ---- コメント ----

async function findComment(number, marker) {
  const comments = await paged(`/issues/${number}/comments`);
  return comments.find(c => c.user?.type === 'Bot' && c.body?.includes(marker)) ?? null;
}

async function upsert(number, marker, text, { onlyUpdate = false } = {}) {
  const body = `${marker}\n${text}`;
  const mine = await findComment(number, marker);
  if (mine) {
    if (mine.body !== body) await gh('PATCH', `/issues/comments/${mine.id}`, { body });
  } else if (!onlyUpdate) {
    await gh('POST', `/issues/${number}/comments`, { body });
  }
}

async function ensureLabel() {
  try {
    await gh('GET', `/labels/${encodeURIComponent(Label)}`);
  } catch {
    await gh('POST', '/labels', { name: Label, color: 'e11d48', description: 'main とコンフリクトしています' }).catch(() => {});
  }
}

function fileList(files) {
  const lines = files.slice(0, MaxFiles).map(f => `  - \`${f}\``);
  if (files.length > MaxFiles) lines.push(`  - ほか ${files.length - MaxFiles} 件`);
  return lines;
}

// ---- 1. いまのコンフリクト ----

async function reportNow(pr, files, cause) {
  const labeled = pr.labels.some(l => l.name === Label);
  if (files.length && !labeled) {
    const lines = [`@${pr.user.login} この Pull Request は、main とコンフリクトしています。`];
    if (cause) lines.push('', `原因になったと思われる変更: #${cause.number} ${cause.title}`);
    lines.push('', 'コンフリクトしているファイル:', ...fileList(files));
    lines.push('', 'お手数ですが、main に追従して解消してください ([CONTRIBUTING.md](../blob/main/CONTRIBUTING.md))。解消されると、このコメントとラベルは自動で更新されます。');
    await gh('POST', `/issues/${pr.number}/labels`, { labels: [Label] });
    await upsert(pr.number, NowMarker, lines.join('\n'));
    console.log(`#${pr.number}: コンフリクトを知らせました`);
  } else if (!files.length && labeled) {
    await gh('DELETE', `/issues/${pr.number}/labels/${encodeURIComponent(Label)}`).catch(() => {});
    await upsert(pr.number, NowMarker, '✅ main とのコンフリクトは解消されました。ありがとうございます!', { onlyUpdate: true });
    console.log(`#${pr.number}: 解消を確認しました`);
  }
}

// ---- 2. マージしたあとのコンフリクトの予告 ----

/** pr をマージしたあと、いまはきれいな others のうちどれがコンフリクトするか。 */
function forecast(main, pr, others, changed) {
  const merged = mergedCommit(main, ref(pr.number));
  if (!merged) return null; // いま main とコンフリクトしているなら予告しない
  const mine = changed.get(pr.number);
  const hits = [];
  for (const other of others) {
    if (other.number === pr.number) continue;
    const theirs = changed.get(other.number);
    if (![...mine].some(f => theirs.has(f))) continue; // 同じファイルを変えていなければコンフリクトしない
    const files = conflictFiles(merged, ref(other.number));
    if (files.length) hits.push({ other, files });
  }
  return hits;
}

async function reportForecast(pr, hits) {
  if (hits === null) return;
  if (!hits.length) {
    // 前に予告していたときだけ書き換える (何もないときに毎回コメントしない)
    await upsert(pr.number, ForecastMarker, '🔮 いまは、この Pull Request をマージしてもコンフリクトする Pull Request はありません。', { onlyUpdate: true });
    return;
  }
  const lines = ['🔮 この Pull Request をマージすると、次の Pull Request が main とコンフリクトするようになります。', ''];
  for (const { other, files } of hits) {
    lines.push(`- #${other.number} ${other.title} (@${other.user.login})`, ...fileList(files));
  }
  lines.push('', 'マージの順番を決めるときの参考にしてください。先にマージした方が、もう片方の作者に追従をお願いすることになります。');
  await upsert(pr.number, ForecastMarker, lines.join('\n'));
  console.log(`#${pr.number}: マージ後に ${hits.length} 件コンフリクトする予告を出しました`);
}

// ---- 本体 ----

async function mergedPull(sha) {
  if (!sha) return null;
  const pulls = await gh('GET', `/commits/${sha}/pulls`).catch(() => []);
  return pulls.find(p => p.merged_at) ?? null;
}

const [mode, arg] = process.argv.slice(2);
if (!(mode === 'main' || (mode === 'pr' && /^\d+$/.test(arg ?? '')))) {
  console.error('使い方: node conflicts.mjs main | pr <番号>');
  process.exit(1);
}

await ensureLabel();
git(['fetch', '--quiet', '--no-tags', 'origin', '+refs/heads/main:refs/remotes/origin/main']);
const main = git(['rev-parse', 'refs/remotes/origin/main']).out.trim();

const open = (await paged('/pulls?state=open')).filter(p => p.base.ref === 'main');
const fetched = fetchHeads(open.map(p => p.number));
const prs = open.filter(p => fetched.has(p.number));

const now = new Map(); // 番号 → main とコンフリクトしているファイル
const changed = new Map(); // 番号 → main から変えたファイル
for (const pr of prs) {
  now.set(pr.number, conflictFiles(main, ref(pr.number)));
  changed.set(pr.number, changedFiles(main, ref(pr.number)));
}
const clean = prs.filter(p => !now.get(p.number).length);

const targets = mode === 'main' ? prs : prs.filter(p => p.number === Number(arg));
const cause = mode === 'main' ? await mergedPull(process.env.GITHUB_SHA) : null;

for (const pr of targets) {
  try {
    await reportNow(pr, now.get(pr.number), cause);
    await reportForecast(pr, forecast(main, pr, clean, changed));
  } catch (e) {
    console.log(`#${pr.number}: ${e.message}`);
  }
}

// pr のときは、相手側の予告も古くなるので、この Pull Request と同じファイルを変えている Pull Request の予告も更新する
if (mode === 'pr') {
  const self = prs.find(p => p.number === Number(arg));
  if (self) {
    const mine = changed.get(self.number);
    for (const other of prs) {
      if (other.number === self.number || ![...changed.get(other.number)].some(f => mine.has(f))) continue;
      try {
        await reportForecast(other, forecast(main, other, clean, changed));
      } catch (e) {
        console.log(`#${other.number}: ${e.message}`);
      }
    }
  }
}
