// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt
// PR の件数制限と、投稿者の返事を待っている Issue・PR の期限管理。
// 件数の超過は Draft にせず「PR上限超過」を付けて順番待ちにし (枠が空いたら外す)、PR要対応・情報待ちは通知から 14 日で閉じる。
// 件数には Draft も含める (Draft を作業中の下書きにだけ使い、上限を避けるために Draft にしなくてよいように)。
// メンテナーが「次にレビュー」を付けた PR は、件数を数えるときに古い PR より先に枠へ入れる。
// No Close の付いた PR・Issue は、自動で閉じない。
// GitHub の本文・差分はデータとして読むだけ。PR のコードは実行しない。
import fs from 'node:fs';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';

export const Labels = { limit: 'PR上限超過', rule: 'PR要対応', info: '情報待ち', exempt: 'No Close', next: '次にレビュー' };
export const WaitMs = 14 * 24 * 60 * 60 * 1000;
// 件数の超過は 14 日で閉じる通知 (版 1)、24 時間で Draft にする通知 (版 2) から、順番待ちの通知 (版 3) に変えた。
// 前の版の通知は、順番待ちの通知に書き直す。
const Versions = { limit: 3, rule: 1, info: 1 };
const Marker = '<!-- meltype-bot --><!-- policy -->';
const BotLogins = new Set(['github-actions[bot]', 'meltype-helper[bot]']);
const names = item => new Set((item?.labels ?? []).map(l => l.name ?? l));
const hash = body => createHash('sha256').update(body ?? '').digest('hex');
const trusted = comment => comment.user?.type === 'Bot' && BotLogins.has(comment.user.login);

/** メンテナー・Bot の PR を除き (Draft は含める)、作者ごとに 2 件を残す。「次にレビュー」の PR を先に、次に作成が古い順、同時刻なら番号順。 */
export function excessPulls(pulls) {
  const counts = new Map();
  return new Set(pulls.filter(p => p.state === 'open'
    && p.user.type !== 'Bot'
    && !['OWNER', 'MEMBER', 'COLLABORATOR'].includes(p.author_association))
    .sort((a, b) => names(b).has(Labels.next) - names(a).has(Labels.next)
      || a.created_at.localeCompare(b.created_at) || a.number - b.number)
    .filter(p => {
      const author = p.user.login.toLowerCase();
      const count = (counts.get(author) ?? 0) + 1;
      counts.set(author, count);
      return count > 2;
    }).map(p => p.number));
}

/** ラベルを付け直した場合・No Close を付け外しした場合・再オープン・Draft から戻した場合は新しい待機期間にする。 */
function cycleOf(events, label) {
  const relevant = events.filter(e =>
    (['labeled', 'unlabeled'].includes(e.event) && [label, Labels.exempt].includes(e.label?.name))
    || ['reopened', 'ready_for_review', 'converted_to_draft'].includes(e.event));
  const last = relevant.sort((a, b) => a.created_at.localeCompare(b.created_at) || a.id - b.id).at(-1);
  return last ? String(last.id) : 'initial';
}

function readNotice(comments, kind) {
  for (const comment of [...comments].reverse()) {
    if (!trusted(comment) || !comment.body?.startsWith(Marker)) continue;
    const match = comment.body.match(/<!-- policy-state:(\{[^\n]+\}) -->/);
    if (!match) continue;
    try {
      const state = JSON.parse(match[1]);
      if (state.kind === kind && [1, 2, 3].includes(state.version)) return { comment, state };
    } catch { /* 壊れた記録は期限切れに使わず、新しく通知する。 */ }
  }
  return null;
}

export function decision(item, kind, comments, events, now) {
  const cycle = cycleOf(events, Labels[kind]);
  const notice = readNotice(comments, kind);
  if (!notice || notice.state.cycle !== cycle || ['resolved', 'exempt'].includes(notice.state.status)
      || notice.state.version !== Versions[kind]) return { action: 'warn', cycle, notice };
  const state = notice.state;
  const limit = kind === 'limit';
  if (!limit && state.status === 'paused') return { action: 'paused', notice };
  const recorded = Date.parse(state.since);
  if (state.status !== 'waiting' || !Number.isFinite(recorded) || !state.bodyHash
      || (item.pull_request && !state.head)) return { action: 'warn', cycle, notice };
  // 実行開始から通知の投稿までに時間がかかっても、通知から丸 14 日は待つ。
  const since = Math.max(recorded, Date.parse(notice.comment.created_at) || 0, Date.parse(notice.comment.updated_at) || 0);
  // フォーム経由など bot 名義の報告では、人からの補足も返事として扱う。
  const replied = comments.some(c => c.user?.type !== 'Bot'
    && (c.user?.login === item.user.login || item.user.type === 'Bot')
    && c.body?.trim() && Math.max(Date.parse(c.created_at) || 0, Date.parse(c.updated_at) || 0) >= since);
  if (!limit && (replied || hash(item.body) !== state.bodyHash || (item.head && item.head.sha !== state.head))) {
    return { action: 'pause', notice };
  }
  // 件数の超過は順番待ちにするだけで、期限で Draft にも閉じもしない。
  if (limit) return { action: 'wait', notice };
  return { action: now - since >= WaitMs ? 'close' : 'wait', notice };
}

function render(state) {
  if (state.kind === 'limit' && state.status === 'waiting') {
    return `${Marker}\n<!-- policy-state:${JSON.stringify(state)} -->\n`
      + `同時に開いている PR は、Draft も含めて 1 人 2 件までレビューします。この PR は \`${Labels.limit}\` を付けて順番待ちにしました (Draft にも、閉じもしません)。\n\n`
      + `先の PR がマージ・クローズされて枠が空くと、bot がラベルを外してこの通知を解除します。メンテナーが \`${Labels.next}\` を付けた PR は、古い PR より先に枠へ入れます。`;
  }
  const reason = {
    rule: 'メンテナーが、この PR に対応が必要と判断しました。メンテナーの指摘を確認し、返信または修正をお願いします。',
    info: 'この報告には、確認に必要な情報が足りません。前の質問を確認し、本文の追記またはコメントで返信をお願いします。',
  }[state.kind];
  const status = state.status === 'resolved' ? '✅ この通知の対象ではなくなりました。'
    : state.status === 'exempt' ? `🛡️ \`${Labels.exempt}\` が付いているため、自動のクローズを止めています。ラベルを外すと、改めて通知して猶予を設けます。`
    : state.status === 'paused' ? '⏸️ 返信または変更を確認したため、自動クローズを止めました。メンテナーの確認をお待ちください。'
      : `${reason}\n\nこの通知から 14 日間、投稿者の返信・本文の変更${state.head ? '・追加の push' : ''}がなければ、自動で閉じます。期限: ${new Date(Date.parse(state.since) + WaitMs).toISOString().slice(0, 10)} (UTC)。情報が揃った場合や対応を再開する場合は、再オープンを依頼できます。`;
  return `${Marker}\n<!-- policy-state:${JSON.stringify(state)} -->\n${status}`;
}

/** API の全ページを読む。上限到達や API エラー時は、欠けた一覧で判断せず停止する。 */
export function githubApi({ token, repository, api = 'https://api.github.com', fetcher = fetch }) {
  const request = async (method, path, body) => {
    const response = await fetcher(`${api}/repos/${repository}${path}`, {
      method,
      headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-bot' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) throw new Error(`${method} ${path}: ${response.status}`);
    return response.status === 204 ? null : response.json();
  };
  const list = async path => {
    const result = [];
    for (let page = 1; page <= 100; page++) {
      const batch = await request('GET', `${path}${path.includes('?') ? '&' : '?'}per_page=100&page=${page}`);
      if (!Array.isArray(batch)) throw new Error(`一覧ではありません: ${path}`);
      result.push(...batch);
      if (batch.length < 100) return result;
    }
    throw new Error(`一覧が大きすぎるため中止: ${path}`);
  };
  const graphql = async (query, variables) => {
    const response = await fetcher(`${api}/graphql`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-bot' },
      body: JSON.stringify({ query, variables }),
    });
    if (!response.ok) throw new Error(`POST /graphql: ${response.status}`);
    const result = await response.json();
    if (result.errors?.length) throw new Error(`GraphQL: ${result.errors.map(e => e.message).join(', ')}`);
    return result.data;
  };
  return { request, list, graphql };
}

export async function runPolicy(api, event, eventName, now = Date.now()) {
  const { request, list } = api;
  const canClose = ['schedule', 'workflow_dispatch'].includes(eventName);
  let pulls = await list('/pulls?state=open');
  const excess = excessPulls(pulls);
  // 手動判断用ラベルも初回に用意する。情報待ちは既存のトリアージ bot が作る。
  const labels = await list('/labels');
  for (const [name, description] of [
    [Labels.limit, '同時オープン PR (Draft を含む) が 2 件を超えていて、枠が空くのを待っている'],
    [Labels.rule, '投稿者によるルールへの対応を待つ (14 日間無反応なら閉じる)'],
    [Labels.exempt, '自動でクローズしない (メンテナーが付ける)'],
    [Labels.next, '取り込めそうなので、上限の枠に古い PR より先に入れる (メンテナーが付ける)'],
  ]) {
    if (!labels.some(l => l.name === name)) {
      await request('POST', '/labels', { name, color: 'FBCA04', description });
    }
  }
  const removedLimits = new Set();
  for (const pr of pulls) {
    const has = names(pr).has(Labels.limit);
    if (excess.has(pr.number) && !has) {
      await request('POST', `/issues/${pr.number}/labels`, { labels: [Labels.limit] });
      pr.labels.push({ name: Labels.limit });
    } else if (!excess.has(pr.number) && has) {
      await request('DELETE', `/issues/${pr.number}/labels/${encodeURIComponent(Labels.limit)}`);
      pr.labels = pr.labels.filter(l => (l.name ?? l) !== Labels.limit);
      removedLimits.add(pr.number);
    }
  }

  const candidates = new Map(pulls.filter(pr => names(pr).has(Labels.limit) || names(pr).has(Labels.rule)
    || removedLimits.has(pr.number) || pr.number === event.issue?.number).map(pr => [pr.number, true]));
  if (canClose) {
    for (const issue of await list(`/issues?state=open&labels=${encodeURIComponent(Labels.info)}`)) {
      if (!issue.pull_request) candidates.set(issue.number, false);
    }
  } else if (event.issue && !event.issue.pull_request) candidates.set(event.issue.number, false);

  for (const [number, isPr] of candidates) {
    await inspect(number, isPr);
  }

  async function snapshot(number, isPr) {
    const item = await request('GET', `/issues/${number}`);
    if (isPr) Object.assign(item, await request('GET', `/pulls/${number}`));
    const comments = await list(`/issues/${number}/comments`);
    if (isPr) {
      comments.push(...await list(`/pulls/${number}/comments`));
      comments.push(...(await list(`/pulls/${number}/reviews`)).filter(r => r.submitted_at).map(r => ({
        ...r, created_at: r.submitted_at,
      })));
    }
    const events = await list(`/issues/${number}/events`);
    return { item, comments, events };
  }

  function active(item, kind, currentPulls) {
    if (item.state !== 'open' || (item.pull_request && item.draft && kind !== 'limit')) return false;
    if (!names(item).has(Labels[kind])) return false;
    return kind !== 'limit' || excessPulls(currentPulls).has(item.number);
  }

  async function writeNotice(number, state, notice) {
    const path = notice ? `/issues/comments/${notice.comment.id}` : `/issues/${number}/comments`;
    await request(notice ? 'PATCH' : 'POST', path, { body: render(state) });
  }

  async function inspect(number, isPr) {
    // ルール待ちラベルのない PR は、過去の通知を対象外にする場合だけ読む。
    const initial = pulls.find(p => p.number === number);
    if (isPr && !names(initial).has(Labels.limit) && !names(initial).has(Labels.rule)) {
      const comments = await list(`/issues/${number}/comments`);
      for (const kind of ['limit', 'rule']) {
        const notice = readNotice(comments, kind);
        if (notice && notice.state.status !== 'resolved') await writeNotice(number, { ...notice.state, status: 'resolved' }, notice);
      }
      return;
    }
    const { item, comments, events } = await snapshot(number, isPr);
    const kinds = isPr ? ['limit', 'rule'] : ['info'];
    const due = [];
    let paused = false;
    for (const kind of kinds) {
      if (!active(item, kind, pulls)) {
        const notice = readNotice(comments, kind);
        if (notice && notice.state.status !== 'resolved') await writeNotice(number, { ...notice.state, status: 'resolved' }, notice);
        continue;
      }
      // No Close の間は期限を数えない。外したときは新しい待機期間にする (cycleOf)。
      // 件数の超過では閉じないので、順番待ちの通知は No Close があってもそのままにする。
      if (kind !== 'limit' && names(item).has(Labels.exempt)) {
        const notice = readNotice(comments, kind);
        if (notice && !['resolved', 'exempt'].includes(notice.state.status)) await writeNotice(number, { ...notice.state, status: 'exempt' }, notice);
        continue;
      }
      const d = decision(item, kind, comments, events, now);
      if (d.action === 'warn') {
        await writeNotice(number, {
          version: Versions[kind], kind, cycle: d.cycle, status: 'waiting', since: new Date(now).toISOString(),
          bodyHash: hash(item.body), head: isPr ? item.head.sha : null,
        }, d.notice);
      } else if (d.action === 'pause') {
        paused = true;
        await writeNotice(number, { ...d.notice.state, status: 'paused' }, d.notice);
      } else if (d.action === 'paused') paused = true;
      else if (d.action === 'close') due.push(kind);
    }
    if (!canClose || due.length === 0) return;

    // 閉じる直前に、返信・push・Draft 化・ラベルをもう一度確認する。
    pulls = await list('/pulls?state=open');
    const fresh = await snapshot(number, isPr);
    const expired = kind => active(fresh.item, kind, pulls) && !names(fresh.item).has(Labels.exempt)
      && decision(fresh.item, kind, fresh.comments, fresh.events, now).action === 'close';
    const closeKind = paused ? null : due.find(kind => expired(kind));
    if (closeKind && !kinds.some(kind => active(fresh.item, kind, pulls)
        && ['pause', 'paused'].includes(decision(fresh.item, kind, fresh.comments, fresh.events, now).action))) {
      await request('POST', `/issues/${number}/comments`, { body: `${Marker}\n${closeMessage(closeKind)}` });
      await request('PATCH', `/issues/${number}`, { state: 'closed', state_reason: 'not_planned' });
      return;
    }
  }
}

function closeMessage(kind) {
  return kind === 'info'
    ? `確認に必要な情報が届かなかったため、閉じます (${Labels.info}: 通知から 14 日間、投稿者の返信・本文の追記がありませんでした)。情報が揃ったら、この Issue にコメントして再オープンを依頼してください。`
    : `通知から 14 日間、投稿者の返信・変更がなかったため閉じます (${Labels[kind]})。対応を再開する場合は、再オープンを依頼してください。`;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  await runPolicy(githubApi({ token: process.env.GITHUB_TOKEN, repository: process.env.GITHUB_REPOSITORY }), event, process.env.GITHUB_EVENT_NAME);
}
