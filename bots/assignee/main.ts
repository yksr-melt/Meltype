// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// PR の作者が、本文の「Fixes #123」などで閉じる Issue の担当 (Assignees) に入っていなければ、PR を Draft にする (issue #1208)。
// 担当のいない Issue・関連 Issue の書かれていない PR はそのまま。メンテナー (OWNER / MEMBER / COLLABORATOR) の PR も対象にしない。
// PR のイベントではその PR を、定期実行では開いているすべての PR を確かめる (イベントの job が取り消されても、次の定期実行で拾う)。
// GitHub の本文はデータとして読むだけ。PR のコードは実行しない。
import * as v from 'valibot';
import { createGitHub, readEvent, type GitHub, type Issue, type Pull } from '../lib/github-api.ts';

const MARKER = '<!-- meltype-bot --><!-- assignee -->';
const MAINTAINERS = new Set(['OWNER', 'MEMBER', 'COLLABORATOR']);
const ACTIONS = new Set(['opened', 'reopened', 'ready_for_review', 'edited']);
const BOT_LOGINS = new Set(['github-actions[bot]', 'meltype-helper[bot]']);

const EventSchema = v.looseObject({
  action: v.optional(v.string()),
  changes: v.optional(v.looseObject({ body: v.optional(v.unknown()) })),
  pull_request: v.optional(v.looseObject({ number: v.number() })),
});
type Event = v.InferOutput<typeof EventSchema>;

/**
 * 本文で閉じる Issue の番号 (重複なし)。テンプレートの説明の <!-- --> の中は読まない。
 * Fixes / Closes / Resolves のあとの #123 と、このリポジトリ (repository = owner/name) の Issue の URL・owner/name#123 を読む。
 */
export function linkedIssues(body: string | null | undefined, repository = ''): number[] {
  // <!-- を入れ子にした本文でも残らないよう、変わらなくなるまで取り除く
  let text = body ?? '', previous;
  do {
    previous = text;
    text = text.replace(/<!--[\s\S]*?-->/g, '');
  } while (text !== previous);
  const repo = repository.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const target = repo ? `(?:https?://github\\.com/${repo}/issues/|${repo}#|#)` : '#';
  const pattern = new RegExp(`\\b(?:fix(?:e[sd])?|close[sd]?|resolve[sd]?)\\s*:?\\s+${target}(\\d+)\\b`, 'gi');
  const numbers = [...text.matchAll(pattern)].map(m => Number(m[1]));
  return [...new Set(numbers)];
}

/** 担当がいて、その中に作者がいない Issue。 */
export function mismatched<T extends Pick<Issue, 'assignees'>>(author: string, issues: T[]): T[] {
  const login = author.toLowerCase();
  return issues.filter(i => (i.assignees ?? []).length > 0 && !i.assignees?.some(a => a.login.toLowerCase() === login));
}

export function render(author: string, issues: Issue[]): string {
  return `${MARKER}\n`
    + `この PR が閉じる Issue の担当 (Assignees) に、作成者の \`${author}\` が入っていないため、Draft にしました。同じ Issue の作業が重ならないようにするためです。\n\n`
    + issues.map(i => `- #${i.number} の担当: ${(i.assignees ?? []).map(a => `\`${a.login}\``).join(', ')}`).join('\n')
    + '\n\n対応したい場合は、その Issue のコメントで担当を相談してください。担当に入ったら「Ready for review」で戻せます (戻したときにもう一度確かめます)。';
}

/**
 * 1 つの PR を確かめ、担当でなければ Draft にして理由をコメントする (前のコメントがあれば書き換える)。
 * pr は確かめる直前に取った PR (本文・開いているか・Draft か は、イベントの時点ではなく今のもの)。
 */
async function checkPull(gh: GitHub, pr: Pull | null, repository: string) {
  if (!pr || pr.state !== 'open' || pr.draft) return 'skip';
  if (MAINTAINERS.has(pr.author_association) || pr.user.type === 'Bot') return 'skip';
  const issues: Issue[] = [];
  for (const number of linkedIssues(pr.body, repository)) {
    const issue = await gh.getIssue(number);
    // 消えた Issue・PR を指す番号は数えない
    if (issue && !issue.pull_request) issues.push(issue);
  }
  const wrong = mismatched(pr.user.login, issues);
  if (wrong.length === 0) return 'ok';
  await gh.convertPullToDraft(pr.node_id);
  const comments = await gh.listComments(pr.number);
  const previous = comments.find(c => c.user?.type === 'Bot' && BOT_LOGINS.has(c.user.login) && c.body?.startsWith(MARKER));
  const body = render(pr.user.login, wrong);
  if (previous) await gh.updateComment(previous.id, body);
  else await gh.postComment(pr.number, body);
  return 'draft';
}

/**
 * PR のイベントならその PR を、定期実行・手動の実行なら開いているすべての PR を確かめる。
 * 結果は PR 番号ごとの 'skip' / 'ok' / 'draft'。
 */
export async function runAssigneeCheck(gh: GitHub, event: Event, eventName: string | undefined, repository = '') {
  const results: Record<number, string> = {};
  if (eventName === 'schedule' || eventName === 'workflow_dispatch') {
    for (const pr of await gh.listOpenPulls()) results[pr.number] = await checkPull(gh, pr, repository);
    return results;
  }
  const number = event.pull_request?.number;
  if (!number || !event.action || !ACTIONS.has(event.action)) return results;
  if (event.action === 'edited' && !event.changes?.body) return results;
  // イベントが届くまでの間に本文の書き換え・Draft 化・クローズがあっても、今の PR で確かめる
  results[number] = await checkPull(gh, await gh.getPull(number), repository);
  return results;
}

if (import.meta.main) {
  console.log(await runAssigneeCheck(createGitHub(), readEvent(EventSchema), process.env.GITHUB_EVENT_NAME, process.env.GITHUB_REPOSITORY));
}
