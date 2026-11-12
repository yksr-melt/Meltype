// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// 貢献者ライセンス同意 (CLA) の bot (.github/workflows/cla.yml から呼ぶ)。
// 初めて Pull Request を出した人に同意の文面を見せ、「CLA に同意します」とコメントしてもらう。
// 同意した人は、別のブランチ (cla-signatures) の signatures.json に記録し、次からは聞かない。
// Pull Request の head に「CLA」の status を出す (同意するまで失敗)。必須チェックではなく、マージの前の確認はレビューする人がする。
// Pull Request のイベントでは動かさない (pull_request_target を使わない)。Pull Request で必ず流れる core.yml が
// 終わったとき (workflow_run) に、その head を確かめる。Pull Request のコードは動かさない (このスクリプトは main のものを使う)。
import fs from 'node:fs';

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;
const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));

/** 同意の文面の版。文面を変えたら上げる (前の版に同意した人にも、もう一度同意してもらう)。 */
const ClaVersion = 1;
const Branch = 'cla-signatures';
const File = 'signatures.json';
const Marker = '<!-- meltype-bot --><!-- cla -->';
const Agree = /^\s*(CLA に同意します|CLAに同意します|I agree to the CLA)[。.!！]?\s*$/i;
const Maintainers = ['OWNER', 'MEMBER', 'COLLABORATOR'];

async function gh(method, url, body) {
  const response = await fetch(`${api}/repos/${repo}${url}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-bot' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`${method} ${url}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

const ClaText = [
  '> 私は、この Pull Request で提供する貢献 (コード・辞書・文書など) について、次のことに同意します。',
  '>',
  '> 1. 貢献は私自身が作成したもので、私にはそれを提供する権利がある。',
  '> 2. 私は Meltype の作者に対し、貢献を複製・改変・配布・サブライセンスする、無償で取り消し不能な、世界的・非独占的な権利を許諾する。',
  '>    これには、GNU GPL v3 以外の条件 (作者が個別に認める利用を含む) で配布することを含む。',
  '> 3. 貢献の著作権は私に残り、私は自分の貢献を自由に利用できる。',
].join('\n');

/** 同意の記録 (無ければ空)。sha は書き込むときに要る。 */
async function readSignatures() {
  const file = await gh('GET', `/contents/${File}?ref=${Branch}`);
  if (!file) return { signatures: { signed: [] }, sha: undefined };
  return { signatures: JSON.parse(Buffer.from(file.content, 'base64').toString('utf8')), sha: file.sha };
}

const hasSigned = (signatures, userId) => signatures.signed.some(s => s.id === userId && s.version >= ClaVersion);

async function ensureBranch() {
  if (await gh('GET', `/branches/${Branch}`)) return;
  // 何も無いブランチを作る (main の履歴とは別。記録のファイルだけを置く)
  const tree = await gh('POST', '/git/trees', { tree: [{ path: 'README.md', mode: '100644', type: 'blob', content: '# CLA の同意の記録\n\nMeltype の bot (.github/bot/cla.mjs) が書きます。手で直さないでください。\n' }] });
  const commit = await gh('POST', '/git/commits', { message: 'CLA の同意の記録を始める', tree: tree.sha, parents: [] });
  await gh('POST', '/git/refs', { ref: `refs/heads/${Branch}`, sha: commit.sha });
}

/** head の「CLA」の status を出す (unsigned は同意がまだの人)。 */
async function setStatus(sha, unsigned) {
  await gh('POST', `/statuses/${sha}`, {
    state: unsigned.length ? 'failure' : 'success',
    context: 'CLA',
    description: (unsigned.length ? `${unsigned.join('・')} さんの CLA への同意が必要です (コメントで「CLA に同意します」)` : 'CLA に同意済みです').slice(0, 140),
  });
}

/** 開いているすべての Pull Request。 */
async function openPulls() {
  const pulls = [];
  for (let page = 1; ; page++) {
    const batch = await gh('GET', `/pulls?state=open&per_page=100&page=${page}`);
    pulls.push(...batch);
    if (batch.length < 100) return pulls;
  }
}

/** 開いている Pull Request のうち、head が sha のもの。status は SHA ごとに 1 つなので、ブランチやフォークが違っても全部見る。 */
const pullsAt = async sha => (await openPulls()).filter(p => p.head.sha === sha);

/** sha の「CLA」の status を出す。sha を head にする Pull Request の作者が全員同意済みなら成功。同意がまだの Pull Request を返す。
 *  signatures を渡さなければ、cla-signatures ブランチから読む。 */
async function checkHead(sha, pulls, signatures) {
  signatures ??= (await readSignatures()).signatures;
  const unsigned = pulls.filter(p => !exempt(p.author_association, p.user.type) && !hasSigned(signatures, p.user.id));
  await setStatus(sha, [...new Set(unsigned.map(p => p.user.login))]);
  return unsigned;
}

/** bot の CLA のコメントを書く。append なら、前の文 (同意をお願いした文面) を残して下に足す。 */
async function upsertComment(issue, body, append = false) {
  const comments = await gh('GET', `/issues/${issue}/comments?per_page=100`) ?? [];
  const mine = comments.find(c => c.user?.type === 'Bot' && c.body?.includes(Marker));
  if (mine && append) await gh('PATCH', `/issues/comments/${mine.id}`, { body: `${mine.body}\n\n---\n\n${body}` });
  else if (mine) await gh('PATCH', `/issues/comments/${mine.id}`, { body: `${Marker}\n${body}` });
  else await gh('POST', `/issues/${issue}/comments`, { body: `${Marker}\n${body}` });
}

const exempt = (association, type) => Maintainers.includes(association) || type === 'Bot';

/** 同意のお願いのコメント。 */
const greeting = login => [
  `@${login} さん、Pull Request ありがとうございます！`,
  '',
  'Meltype は GNU GPL v3 で公開していますが、GPL v3 の条件で使えない方には作者が個別に利用を認めることがあります。これを続けられるように、初めての方には次の貢献者ライセンス同意 (CLA) をお願いしています ([詳しく](https://github.com/yksr-melt/Meltype/blob/main/CONTRIBUTING.md))。',
  '',
  ClaText,
  '',
  '同意していただける場合は、この Pull Request に次の 1 行だけをコメントしてください (一度同意すれば、次の Pull Request からは不要です)。',
  '',
  '```',
  'CLA に同意します',
  '```',
  '',
  '<sub>英語なら `I agree to the CLA` でも大丈夫です。</sub>',
  ].join('\n');

/** Pull Request の core.yml が終わった: その head の「CLA」の status を出し、同意がまだならお願いのコメントを出す。 */
async function onWorkflowRun() {
  const run = event.workflow_run;
  const pulls = await pullsAt(run.head_sha);
  if (!pulls.length) {
    console.log(`${run.head_sha} を head にする、開いている Pull Request がありません`);
    return;
  }
  for (const pr of await checkHead(run.head_sha, pulls)) await upsertComment(pr.number, greeting(pr.user.login));
}

/** コメント: Pull Request を出した本人が同意の 1 行を書いたら記録する。 */
async function onComment() {
  if (!event.issue.pull_request || !Agree.test(event.comment.body ?? '')) return;
  const pr = await gh('GET', `/pulls/${event.issue.number}`);
  const user = event.comment.user;
  if (user.id !== pr.user.id) return; // 本人以外の同意は受け付けない
  await ensureBranch();
  const { signatures, sha } = await readSignatures();
  if (!hasSigned(signatures, user.id)) {
    signatures.signed.push({
      login: user.login,
      id: user.id,
      version: ClaVersion,
      pullRequest: pr.number,
      comment: event.comment.html_url,
      createdAt: event.comment.created_at,
    });
    await gh('PUT', `/contents/${File}`, {
      message: `CLA: ${user.login} さんが同意 (#${pr.number})`,
      content: Buffer.from(JSON.stringify(signatures, null, 2) + '\n').toString('base64'),
      branch: Branch,
      sha,
    });
  }
  await gh('POST', `/issues/comments/${event.comment.id}/reactions`, { content: 'heart' }).catch(() => {});
  const pulls = await pullsAt(pr.head.sha);
  await checkHead(pr.head.sha, pulls.some(p => p.number === pr.number) ? pulls : [...pulls, pr], signatures);
  await upsertComment(pr.number, `✅ @${user.login} さん、CLA への同意ありがとうございます。記録しました ([コメント](${event.comment.html_url}))。次の Pull Request からは不要です。`, true);
}

try {
  if (event.workflow_run) await onWorkflowRun();
  else if (event.comment) await onComment();
} catch (error) {
  console.error(error);
  process.exitCode = 1;
}
