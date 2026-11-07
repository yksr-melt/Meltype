// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// 貢献者ライセンス同意 (CLA) の bot (.github/workflows/cla.yml から呼ぶ)。
// 初めて Pull Request を出した人に同意の文面を見せ、「CLA に同意します」とコメントしてもらう。
// 同意した人は、別のブランチ (cla-signatures) の signatures.json に記録し、次からは聞かない。
// Pull Request の結果に「CLA」のチェックを出す (同意するまで失敗)。
// Pull Request のコードは動かさない (このスクリプトは main のものを使う)。
import * as v from 'valibot';
import { createGitHub, readEvent } from '../lib/github-api.ts';

const gh = createGitHub();

const UserSchema = v.looseObject({ login: v.string(), id: v.number(), type: v.optional(v.string()) });
const EventPullSchema = v.looseObject({
  number: v.number(),
  author_association: v.string(),
  user: UserSchema,
  head: v.looseObject({ sha: v.string() }),
});
const EventCommentSchema = v.looseObject({
  id: v.number(),
  body: v.nullish(v.string()),
  html_url: v.string(),
  created_at: v.string(),
  user: UserSchema,
});
const event = readEvent(v.looseObject({
  pull_request: v.optional(EventPullSchema),
  issue: v.optional(v.looseObject({ number: v.number(), pull_request: v.optional(v.unknown()) })),
  comment: v.optional(EventCommentSchema),
}));
const SignaturesSchema = v.looseObject({ signed: v.array(v.looseObject({ id: v.number(), version: v.number() })) });
type Signatures = v.InferOutput<typeof SignaturesSchema>;

/** 同意の文面の版。文面を変えたら上げる (前の版に同意した人にも、もう一度同意してもらう)。 */
const CLA_VERSION = 1;
const BRANCH = 'cla-signatures';
const SIGNATURES_FILE = 'signatures.json';
const MARKER = '<!-- meltype-bot --><!-- cla -->';
const AGREE = /^\s*(CLA に同意します|CLAに同意します|I agree to the CLA)[。.!！]?\s*$/i;
const MAINTAINERS = ['OWNER', 'MEMBER', 'COLLABORATOR'];

const CLA_TEXT = [
  '> 私は、この Pull Request で提供する貢献 (コード・辞書・文書など) について、次のことに同意します。',
  '>',
  '> 1. 貢献は私自身が作成したもので、私にはそれを提供する権利がある。',
  '> 2. 私は Meltype の作者に対し、貢献を複製・改変・配布・サブライセンスする、無償で取り消し不能な、世界的・非独占的な権利を許諾する。',
  '>    これには、GNU GPL v3 以外の条件 (作者が個別に認める利用を含む) で配布することを含む。',
  '> 3. 貢献の著作権は私に残り、私は自分の貢献を自由に利用できる。',
].join('\n');

/** 同意の記録 (無ければ空)。sha は書き込むときに要る。 */
async function readSignatures(): Promise<{ signatures: Signatures; sha: string | undefined }> {
  const file = await gh.getFile(SIGNATURES_FILE, BRANCH);
  if (!file) return { signatures: { signed: [] }, sha: undefined };
  return { signatures: v.parse(SignaturesSchema, JSON.parse(file.text)), sha: file.sha };
}

const hasSigned = (signatures: Signatures, userId: number) => signatures.signed.some(s => s.id === userId && s.version >= CLA_VERSION);

async function ensureBranch() {
  if (await gh.branchExists(BRANCH)) return;
  // 何も無いブランチを作る (main の履歴とは別。記録のファイルだけを置く)
  const sha = await gh.createRootCommit('CLA の同意の記録を始める', 'README.md', '# CLA の同意の記録\n\nMeltype の bot (bots/cla/main.ts) が書きます。手で直さないでください。\n');
  await gh.createRef(`refs/heads/${BRANCH}`, sha);
}

async function setStatus(sha: string, signed: boolean, login: string) {
  await gh.setCommitStatus(sha, {
    state: signed ? 'success' : 'failure',
    context: 'CLA',
    description: signed ? `${login} さんは CLA に同意済み` : `${login} さんの CLA への同意が必要です (コメントで「CLA に同意します」)`,
  });
}

/** bot の CLA のコメントを書く。append なら、前の文 (同意をお願いした文面) を残して下に足す。 */
async function upsertComment(issue: number, body: string, append = false) {
  const mine = await gh.findBotComment(issue, MARKER);
  if (mine && append) await gh.updateComment(mine.id, `${mine.body}\n\n---\n\n${body}`);
  else if (mine) await gh.updateComment(mine.id, `${MARKER}\n${body}`);
  else await gh.postComment(issue, `${MARKER}\n${body}`);
}

const isExempt = (association: string, type: string | undefined) => MAINTAINERS.includes(association) || type === 'Bot';

/** Pull Request が作られた・更新された: 同意済みか確かめ、まだならお願いのコメントを出す。 */
async function onPullRequest(pr: v.InferOutput<typeof EventPullSchema>) {
  const author = pr.user;
  if (isExempt(pr.author_association, author.type)) {
    await setStatus(pr.head.sha, true, author.login);
    return;
  }
  const { signatures } = await readSignatures();
  if (hasSigned(signatures, author.id)) {
    await setStatus(pr.head.sha, true, author.login);
    return;
  }
  await setStatus(pr.head.sha, false, author.login);
  await upsertComment(pr.number, [
    `@${author.login} さん、Pull Request ありがとうございます！`,
    '',
    'Meltype は GNU GPL v3 で公開していますが、GPL v3 の条件で使えない方には作者が個別に利用を認めることがあります。これを続けられるように、初めての方には次の貢献者ライセンス同意 (CLA) をお願いしています ([詳しく](https://github.com/yksr-melt/Meltype/blob/main/CONTRIBUTING.md))。',
    '',
    CLA_TEXT,
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
async function onComment(comment: v.InferOutput<typeof EventCommentSchema>) {
  if (!event.issue?.pull_request || !AGREE.test(comment.body ?? '')) return;
  const pr = await gh.getPull(event.issue.number);
  if (!pr) return;
  const user = comment.user;
  if (user.id !== pr.user.id) return; // 本人以外の同意は受け付けない
  await ensureBranch();
  const { signatures, sha } = await readSignatures();
  if (!hasSigned(signatures, user.id)) {
    signatures.signed.push({
      login: user.login,
      id: user.id,
      version: CLA_VERSION,
      pullRequest: pr.number,
      comment: comment.html_url,
      createdAt: comment.created_at,
    });
    await gh.putFile(SIGNATURES_FILE, {
      message: `CLA: ${user.login} さんが同意 (#${pr.number})`,
      text: JSON.stringify(signatures, null, 2) + '\n',
      branch: BRANCH,
      sha,
    });
  }
  await gh.reactToComment(comment.id, 'heart').catch(() => {});
  await setStatus(pr.head.sha, true, user.login);
  await upsertComment(pr.number, `✅ @${user.login} さん、CLA への同意ありがとうございます。記録しました ([コメント](${comment.html_url}))。次の Pull Request からは不要です。`, true);
}

try {
  if (event.pull_request) await onPullRequest(event.pull_request);
  else if (event.comment) await onComment(event.comment);
} catch (error) {
  console.error(error);
  process.exitCode = 1;
}
