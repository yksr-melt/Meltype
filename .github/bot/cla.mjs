// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// 貢献者ライセンス同意 (CLA) の bot (.github/workflows/cla.yml から呼ぶ)。
// 初めて Pull Request を出した人に同意の文面を見せ、「CLA に同意します」とコメントしてもらう。
// 同意した人は、別のブランチ (cla-signatures) の signatures.json に記録し、次からは聞かない。
// Pull Request の結果に「CLA」のチェックを出す (同意するまで失敗)。
// Pull Request のコードは動かさない (このスクリプトは main のものを使う)。
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
// コミット status は同じ SHA を使う別の PR と共有されるため、PR ごとにも作者の同意を記録する。
// このラベルは bot 専用で、手動では付けない。Mergify の queue_conditions が参照する。
const VerifiedLabel = 'CLA確認済み';

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

async function setStatus(sha, signed, login) {
  await gh('POST', `/statuses/${sha}`, {
    state: signed ? 'success' : 'failure',
    context: 'CLA',
    description: signed ? `${login} さんは CLA に同意済み` : `${login} さんの CLA への同意が必要です (コメントで「CLA に同意します」)`,
  });
}

/** PR ごとの作者確認。存在しなければ作り、作成が競合したときは再取得する。 */
async function setVerifiedLabel(number, signed) {
  const label = encodeURIComponent(VerifiedLabel);
  if (!signed) {
    await gh('DELETE', `/issues/${number}/labels/${label}`);
    return;
  }
  if (!await gh('GET', `/labels/${label}`)) {
    try {
      await gh('POST', '/labels', { name: VerifiedLabel, color: '0e8a16', description: 'CLA bot が PR 作者の同意を確認 (手動で付けない)' });
    } catch (error) {
      if (!await gh('GET', `/labels/${label}`)) throw error;
    }
  }
  const labels = await gh('POST', `/issues/${number}/labels`, { labels: [VerifiedLabel] });
  if (!labels?.some(l => l.name === VerifiedLabel)) throw new Error(`Could not add ${VerifiedLabel} to #${number}`);
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

/** Pull Request が作られた・更新された: 同意済みか確かめ、まだならお願いのコメントを出す。 */
async function onPullRequest() {
  const pr = event.pull_request;
  const author = pr.user;
  if (exempt(pr.author_association, author.type)) {
    await setStatus(pr.head.sha, true, author.login);
    await setVerifiedLabel(pr.number, true);
    return;
  }
  const { signatures } = await readSignatures();
  if (hasSigned(signatures, author.id)) {
    await setStatus(pr.head.sha, true, author.login);
    await setVerifiedLabel(pr.number, true);
    return;
  }
  await setVerifiedLabel(pr.number, false);
  await setStatus(pr.head.sha, false, author.login);
  await upsertComment(pr.number, [
    `@${author.login} さん、Pull Request ありがとうございます！`,
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
  ].join('\n'));
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
  await setStatus(pr.head.sha, true, user.login);
  await setVerifiedLabel(pr.number, true);
  await upsertComment(pr.number, `✅ @${user.login} さん、CLA への同意ありがとうございます。記録しました ([コメント](${event.comment.html_url}))。次の Pull Request からは不要です。`, true);
}

try {
  if (event.pull_request) await onPullRequest();
  else if (event.comment) await onComment();
} catch (error) {
  console.error(error);
  process.exitCode = 1;
}
