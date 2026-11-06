// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt
// PR の件数制限と、投稿者の返事を待っている Issue・PR の期限管理。
// 件数の超過は Draft にせず「PR上限超過」を付けて順番待ちにし (枠が空いたら外す)、PR要対応・情報待ちは通知から 14 日で閉じる。
// 件数には Draft も含める (Draft を作業中の下書きにだけ使い、上限を避けるために Draft にしなくてよいように)。
// メンテナーが「次にレビュー」を付けた PR は、件数を数えるときに古い PR より先に枠へ入れる。
// GitHub の本文・差分はデータとして読むだけ。PR のコードは実行しない。
import { createHash } from 'node:crypto';
import * as v from 'valibot';
import { createGitHub, readEvent, type Comment, type GitHub, type Issue, type IssueEvent, type Pull } from '../lib/github-api.ts';

export const LABELS = { limit: 'PR上限超過', rule: 'PR要対応', info: '情報待ち', next: '次にレビュー' };
export const WAIT_MS = 14 * 24 * 60 * 60 * 1000;
// 件数の超過は 14 日で閉じる通知 (版 1)、24 時間で Draft にする通知 (版 2) から、順番待ちの通知 (版 3) に変えた。
// 前の版の通知は、順番待ちの通知に書き直す。
const VERSIONS = { limit: 3, rule: 1, info: 1 };
const MARKER = '<!-- meltype-bot --><!-- policy -->';
const BOT_LOGINS = new Set(['github-actions[bot]', 'meltype-helper[bot]']);

// 通知のコメントに bot が埋め込んだ記録。読めない記録は期限切れに使わず、新しく通知する
const NoticeStateSchema = v.looseObject({
  kind: v.string(),
  version: v.number(),
  cycle: v.optional(v.string()),
  status: v.optional(v.string()),
  since: v.optional(v.string()),
  bodyHash: v.optional(v.string()),
  head: v.nullish(v.string()),
});
type NoticeState = v.InferOutput<typeof NoticeStateSchema>;
const EventSchema = v.looseObject({
  issue: v.optional(v.looseObject({ number: v.number(), pull_request: v.optional(v.unknown()) })),
});
type Event = v.InferOutput<typeof EventSchema>;

type Kind = 'limit' | 'rule' | 'info';
type Notice = { comment: Comment; state: NoticeState };
/** Issue か、Pull Request の Issue 側と PR 側を合わせたもの。 */
type Item = Issue & Partial<Pull>;

const names = (item: { labels: { name: string }[] } | undefined) => new Set((item?.labels ?? []).map(l => l.name));
const hash = (body: string | null | undefined) => createHash('sha256').update(body ?? '').digest('hex');
const isTrusted = (comment: Comment) => comment.user?.type === 'Bot' && BOT_LOGINS.has(comment.user.login);

/** メンテナー・Bot の PR を除き (Draft は含める)、作者ごとに 2 件を残す。「次にレビュー」の PR を先に、次に作成が古い順、同時刻なら番号順。 */
export function excessPulls(pulls: Pull[]) {
  const counts = new Map<string, number>();
  return new Set(pulls.filter(p => p.state === 'open'
    && p.user.type !== 'Bot'
    && !['OWNER', 'MEMBER', 'COLLABORATOR'].includes(p.author_association))
    .sort((a, b) => Number(names(b).has(LABELS.next)) - Number(names(a).has(LABELS.next))
      || a.created_at.localeCompare(b.created_at) || a.number - b.number)
    .filter(p => {
      const author = p.user.login.toLowerCase();
      const count = (counts.get(author) ?? 0) + 1;
      counts.set(author, count);
      return count > 2;
    }).map(p => p.number));
}

/** ラベルを付け直した場合・再オープン・Draft から戻した場合は新しい待機期間にする。 */
function cycleOf(events: IssueEvent[], label: string) {
  const relevant = events.filter(e =>
    (['labeled', 'unlabeled'].includes(e.event) && e.label?.name === label)
    || ['reopened', 'ready_for_review', 'converted_to_draft'].includes(e.event));
  const last = relevant.sort((a, b) => a.created_at.localeCompare(b.created_at) || a.id - b.id).at(-1);
  return last ? String(last.id) : 'initial';
}

function readNotice(comments: Comment[], kind: Kind): Notice | null {
  for (const comment of [...comments].reverse()) {
    if (!isTrusted(comment) || !comment.body?.startsWith(MARKER)) continue;
    const match = comment.body.match(/<!-- policy-state:(\{[^\n]+\}) -->/);
    if (!match) continue;
    try {
      const state = v.safeParse(NoticeStateSchema, JSON.parse(match[1] ?? ''));
      if (state.success && state.output.kind === kind && [1, 2, 3].includes(state.output.version)) return { comment, state: state.output };
    } catch { /* 壊れた記録は期限切れに使わず、新しく通知する。 */ }
  }
  return null;
}

export function decision(item: Item, kind: Kind, comments: Comment[], events: IssueEvent[], now: number) {
  const cycle = cycleOf(events, LABELS[kind]);
  const notice = readNotice(comments, kind);
  if (!notice || notice.state.cycle !== cycle || notice.state.status === 'resolved'
      || notice.state.version !== VERSIONS[kind]) return { action: 'warn', cycle, notice } as const;
  const state = notice.state;
  const limit = kind === 'limit';
  if (!limit && state.status === 'paused') return { action: 'paused', notice } as const;
  const recorded = Date.parse(state.since ?? '');
  if (state.status !== 'waiting' || !Number.isFinite(recorded) || !state.bodyHash
      || (item.pull_request && !state.head)) return { action: 'warn', cycle, notice } as const;
  // 実行開始から通知の投稿までに時間がかかっても、通知から丸 14 日は待つ。
  const since = Math.max(recorded, Date.parse(notice.comment.created_at) || 0, Date.parse(notice.comment.updated_at ?? '') || 0);
  // フォーム経由など bot 名義の報告では、人からの補足も返事として扱う。
  const replied = comments.some(c => c.user?.type !== 'Bot'
    && (c.user?.login === item.user.login || item.user.type === 'Bot')
    && c.body?.trim() && Math.max(Date.parse(c.created_at) || 0, Date.parse(c.updated_at ?? '') || 0) >= since);
  if (!limit && (replied || hash(item.body) !== state.bodyHash || (item.head && item.head.sha !== state.head))) {
    return { action: 'pause', notice } as const;
  }
  // 件数の超過は順番待ちにするだけで、期限で Draft にも閉じもしない。
  if (limit) return { action: 'wait', notice } as const;
  return { action: now - since >= WAIT_MS ? 'close' : 'wait', notice } as const;
}

function render(state: NoticeState) {
  if (state.kind === 'limit' && state.status === 'waiting') {
    return `${MARKER}\n<!-- policy-state:${JSON.stringify(state)} -->\n`
      + `同時に開いている PR は、Draft も含めて 1 人 2 件までレビューします。この PR は \`${LABELS.limit}\` を付けて順番待ちにしました (Draft にも、閉じもしません)。\n\n`
      + `先の PR がマージ・クローズされて枠が空くと、bot がラベルを外してこの通知を解除します。メンテナーが \`${LABELS.next}\` を付けた PR は、古い PR より先に枠へ入れます。`;
  }
  const reason = ({
    rule: 'メンテナーが、この PR に対応が必要と判断しました。メンテナーの指摘を確認し、返信または修正をお願いします。',
    info: 'この報告には、確認に必要な情報が足りません。前の質問を確認し、本文の追記またはコメントで返信をお願いします。',
  } as Record<string, string>)[state.kind];
  const status = state.status === 'resolved' ? '✅ この通知の対象ではなくなりました。'
    : state.status === 'paused' ? '⏸️ 返信または変更を確認したため、自動クローズを止めました。メンテナーの確認をお待ちください。'
      : `${reason}\n\nこの通知から 14 日間、投稿者の返信・本文の変更${state.head ? '・追加の push' : ''}がなければ、自動で閉じます。期限: ${new Date(Date.parse(state.since ?? '') + WAIT_MS).toISOString().slice(0, 10)} (UTC)。情報が揃った場合や対応を再開する場合は、再オープンを依頼できます。`;
  return `${MARKER}\n<!-- policy-state:${JSON.stringify(state)} -->\n${status}`;
}

export async function runPolicy(gh: GitHub, event: Event, eventName: string | undefined, now = Date.now()) {
  const canClose = ['schedule', 'workflow_dispatch'].includes(eventName ?? '');
  let pulls = await gh.listOpenPulls();
  const excess = excessPulls(pulls);
  // 手動判断用ラベルも初回に用意する。
  const labels = await gh.listLabels();
  for (const [name, description] of [
    [LABELS.limit, '同時オープン PR (Draft を含む) が 2 件を超えていて、枠が空くのを待っている'],
    [LABELS.rule, '投稿者によるルールへの対応を待つ (14 日間無反応なら閉じる)'],
    [LABELS.next, '取り込めそうなので、上限の枠に古い PR より先に入れる (メンテナーが付ける)'],
  ] as const) {
    if (!labels.some(l => l.name === name)) {
      await gh.createLabel(name, 'FBCA04', description);
    }
  }
  const removedLimits = new Set<number>();
  for (const pr of pulls) {
    const has = names(pr).has(LABELS.limit);
    if (excess.has(pr.number) && !has) {
      await gh.addLabels(pr.number, [LABELS.limit]);
      pr.labels.push({ name: LABELS.limit });
    } else if (!excess.has(pr.number) && has) {
      await gh.removeLabel(pr.number, LABELS.limit);
      pr.labels = pr.labels.filter(l => l.name !== LABELS.limit);
      removedLimits.add(pr.number);
    }
  }

  const candidates = new Map(pulls.filter(pr => names(pr).has(LABELS.limit) || names(pr).has(LABELS.rule)
    || removedLimits.has(pr.number) || pr.number === event.issue?.number).map(pr => [pr.number, true]));
  if (canClose) {
    for (const issue of await gh.listOpenIssuesWithLabel(LABELS.info)) {
      if (!issue.pull_request) candidates.set(issue.number, false);
    }
  } else if (event.issue && !event.issue.pull_request) candidates.set(event.issue.number, false);

  for (const [number, isPr] of candidates) {
    await inspect(number, isPr);
  }

  async function snapshot(number: number, isPr: boolean) {
    const issue = await gh.getIssue(number);
    if (!issue) throw new Error(`#${number} が見つかりません`);
    const pull = isPr ? await gh.getPull(number) : null;
    if (isPr && !pull) throw new Error(`#${number} の Pull Request が見つかりません`);
    const item: Item = { ...issue, ...pull };
    const comments = await gh.listComments(number);
    if (isPr) {
      comments.push(...await gh.listPullReviewComments(number));
      for (const r of await gh.listPullReviews(number)) {
        if (r.submitted_at) comments.push({ ...r, created_at: r.submitted_at });
      }
    }
    const events = await gh.listIssueEvents(number);
    return { item, comments, events };
  }

  function isActive(item: Item, kind: Kind, currentPulls: Pull[]) {
    if (item.state !== 'open' || (item.pull_request && item.draft && kind !== 'limit')) return false;
    if (!names(item).has(LABELS[kind])) return false;
    return kind !== 'limit' || excessPulls(currentPulls).has(item.number);
  }

  async function writeNotice(number: number, state: NoticeState, notice: Notice | null) {
    if (notice) await gh.updateComment(notice.comment.id, render(state));
    else await gh.postComment(number, render(state));
  }

  async function inspect(number: number, isPr: boolean) {
    // ルール待ちラベルのない PR は、過去の通知を対象外にする場合だけ読む。
    const initial = pulls.find(p => p.number === number);
    if (isPr && !names(initial).has(LABELS.limit) && !names(initial).has(LABELS.rule)) {
      const comments = await gh.listComments(number);
      for (const kind of ['limit', 'rule'] as const) {
        const notice = readNotice(comments, kind);
        if (notice && notice.state.status !== 'resolved') await writeNotice(number, { ...notice.state, status: 'resolved' }, notice);
      }
      return;
    }
    const { item, comments, events } = await snapshot(number, isPr);
    const kinds: Kind[] = isPr ? ['limit', 'rule'] : ['info'];
    const due: Kind[] = [];
    let paused = false;
    for (const kind of kinds) {
      if (!isActive(item, kind, pulls)) {
        const notice = readNotice(comments, kind);
        if (notice && notice.state.status !== 'resolved') await writeNotice(number, { ...notice.state, status: 'resolved' }, notice);
        continue;
      }
      const d = decision(item, kind, comments, events, now);
      if (d.action === 'warn') {
        await writeNotice(number, {
          version: VERSIONS[kind], kind, cycle: d.cycle, status: 'waiting', since: new Date(now).toISOString(),
          bodyHash: hash(item.body), head: isPr ? item.head?.sha : null,
        }, d.notice);
      } else if (d.action === 'pause') {
        paused = true;
        await writeNotice(number, { ...d.notice.state, status: 'paused' }, d.notice);
      } else if (d.action === 'paused') paused = true;
      else if (d.action === 'close') due.push(kind);
    }
    if (!canClose || due.length === 0) return;

    // 閉じる直前に、返信・push・Draft 化・ラベルをもう一度確認する。
    pulls = await gh.listOpenPulls();
    const fresh = await snapshot(number, isPr);
    const isExpired = (kind: Kind) => isActive(fresh.item, kind, pulls)
      && decision(fresh.item, kind, fresh.comments, fresh.events, now).action === 'close';
    const closeKind = paused ? null : due.find(kind => isExpired(kind));
    if (closeKind && !kinds.some(kind => isActive(fresh.item, kind, pulls)
        && ['pause', 'paused'].includes(decision(fresh.item, kind, fresh.comments, fresh.events, now).action))) {
      await gh.postComment(number, `${MARKER}\n${closeMessage(closeKind)}`);
      await gh.closeIssueAsNotPlanned(number);
      return;
    }
  }
}

function closeMessage(kind: Kind) {
  return kind === 'info'
    ? `確認に必要な情報が届かなかったため、閉じます (${LABELS.info}: 通知から 14 日間、投稿者の返信・本文の追記がありませんでした)。情報が揃ったら、この Issue にコメントして再オープンを依頼してください。`
    : `通知から 14 日間、投稿者の返信・変更がなかったため閉じます (${LABELS[kind]})。対応を再開する場合は、再オープンを依頼してください。`;
}

if (import.meta.main) {
  await runPolicy(createGitHub(), readEvent(EventSchema), process.env.GITHUB_EVENT_NAME);
}
