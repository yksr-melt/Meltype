// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Meltype の GitHub bot (.github/workflows/bot.yml から呼ぶ)。外部のパッケージは使わない (Node.js の fetch だけ)。
//   node meltype-bot.mjs parse    … Issue の作成・編集 / コメントのコマンドを読み、次の job に何をするかを渡す
//   node meltype-bot.mjs report   … 再現・テスト・パッケージの結果をコメントする
// Issue の本文・コメントは誰でも書けるので、コマンドとして実行しない (値として次の job に渡すだけ)。
import fs from 'node:fs';
import path from 'node:path';
import { codeCell } from './markdown.mjs';
import { findRelatedIssues, formatRelatedIssueComment } from './issue-relation-candidates.mjs';

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;
// workflow_dispatch は bot の App に Actions の権限が無いので、job の GITHUB_TOKEN (actions: write) で叩く
const dispatchToken = process.env.BOT_DISPATCH_TOKEN || token;
const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
const Marker = '<!-- meltype-bot -->';
const Maintainers = ['OWNER', 'MEMBER', 'COLLABORATOR'];
const MaxKeys = 300;
const MaxRepros = 10;

async function gh(method, url, body, auth = token) {
  const response = await fetch(`${api}/repos/${repo}${url}`, {
    method,
    headers: { Authorization: `Bearer ${auth}`, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-bot' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`${method} ${url}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

function output(values) {
  const lines = Object.entries(values).map(([k, v]) => {
    const delimiter = `EOF_${Math.random().toString(36).slice(2)}`;
    return `${k}<<${delimiter}\n${v ?? ''}\n${delimiter}`;
  });
  fs.appendFileSync(process.env.GITHUB_OUTPUT, lines.join('\n') + '\n');
}

function parseForm(body) {
  const fields = {};
  for (const part of (body ?? '').split(/^### /m).slice(1)) {
    const newline = part.indexOf('\n');
    const title = part.slice(0, newline).trim();
    let value = part.slice(newline + 1).trim();
    if (value === '_No response_') value = '';
    fields[title] = value;
  }
  return fields;
}

const field = (form, prefix) => Object.entries(form).find(([k]) => k.startsWith(prefix))?.[1] ?? '';

function osLabels(os) {
  const labels = [];
  if (/Windows/i.test(os)) labels.push('windows');
  if (/Mac/i.test(os)) labels.push('mac');
  if (/Linux/i.test(os)) labels.push('linux');
  return labels;
}

function lastKey(text) {
  if (/^Space/.test(text)) return 'space';
  if (/途中/.test(text)) return 'none';
  return 'enter';
}

const isKeys = text => text.length > 0 && /^[\x20-\x7e]+$/.test(text);

async function findBotComment(issue, kind) {
  const marker = `${Marker}<!-- ${kind} -->`;
  for (let page = 1; ; page++) {
    const comments = await gh('GET', `/issues/${issue}/comments?per_page=100&page=${page}`);
    const mine = comments.find(c => c.user?.type === 'Bot' && c.body?.includes(marker));
    if (mine || comments.length < 100) return mine ?? null;
  }
}

async function upsertComment(issue, kind, text) {
  const marker = `${Marker}<!-- ${kind} -->`;
  const mine = await findBotComment(issue, kind);
  const body = `${marker}\n${text}`;
  if (mine) await gh('PATCH', `/issues/comments/${mine.id}`, { body });
  else await gh('POST', `/issues/${issue}/comments`, { body });
}

async function searchIssues(query) {
  const response = await fetch(api + '/search/issues?q=' + encodeURIComponent(query) + '&per_page=100&page=1', {
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-bot' },
  });
  if (!response.ok) throw new Error('Issue search failed: ' + response.status);
  return response.json();
}

async function updateRelatedIssues(issue) {
  try {
    const candidates = await findRelatedIssues(issue, { repo, search: searchIssues, getIssue: async number => gh('GET', `/issues/${number}`) });
    if (candidates.length > 0) {
      await upsertComment(issue.number, 'related-issues', formatRelatedIssueComment(issue, candidates, repo));
    } else {
      const existing = await findBotComment(issue.number, 'related-issues');
      if (existing) await gh('DELETE', `/issues/comments/${existing.id}`);
    }
  } catch (error) {
    console.warn('Related-issue search failed; existing comment was left unchanged (' + (error?.message ?? 'unknown error') + ').');
  }
}

async function react(content) {
  if (event.comment) await gh('POST', `/issues/comments/${event.comment.id}/reactions`, { content }).catch(() => {});
}

const LabelInfo = {
  '要トリアージ': ['FBCA04', '作者がまだ確認していない報告'],
  'win11': ['0078D4', 'Windows 11'],
  'win10': ['5E9ED6', 'Windows 10'],
  '企業PC': ['5319E7', 'Windows の Enterprise / Education (管理が厳しいことがある)'],
  '管理者で実行': ['D93F0B', 'Meltype を管理者として実行していた'],
  'Mozcなし': ['E99695', 'Mozc の変換ヘルパーが無い (Microsoft IME だけで変換)'],
  'かな入力': ['FEF2C0', 'かな入力 (JIS)'],
  'USキーボード': ['FEF2C0', '日本語 (JIS) 以外のキーボード'],
  '高DPI': ['FEF2C0', '画面の拡大率が 100% より大きい'],
  '優先: 高': ['B60205', '入力できない・止まる・文字が消えるなど'],
  '優先: 中': ['E99695', 'よくある不具合'],
  '優先: 低': ['C5DEF5', '見た目だけ・一度だけ起きた・回避方法がある など'],
  'アプリ: チャット': ['BFDADC', 'Discord・Slack・LINE など'],
  'アプリ: コード': ['BFDADC', 'VS Code・ターミナル・Maya など'],
  'アプリ: ブラウザー': ['BFDADC', 'Chrome・Edge・Firefox など'],
  'アプリ: ゲーム': ['BFDADC', 'ゲーム'],
  'アプリ: Office': ['BFDADC', 'Word・Excel・Outlook など'],
  'インストール': ['BFDADC', 'インストール・更新・起動'],
};

const knownLabels = new Set();
async function ensureLabel(name) {
  if (knownLabels.has(name)) return;
  knownLabels.add(name);
  const [color, description] = LabelInfo[name] ?? (/^v\d/.test(name) ? ['EDEDED', 'Meltype の版'] : [null, null]);
  if (!color) return;
  await gh('POST', '/labels', { name, color, description })
    .catch(() => gh('PATCH', `/labels/${encodeURIComponent(name)}`, { color, description }))
    .catch(() => {});
}

function environmentLabels(text) {
  const env = {};
  for (const line of text.replace(/```\w*|<\/?details>|<summary>.*?<\/summary>/g, '').split('\n')) {
    const colon = line.indexOf(':');
    if (colon > 0) env[line.slice(0, colon).trim()] = line.slice(colon + 1).trim();
  }
  const result = [];
  const os = env['OS'] ?? '';
  if (/^Windows 11/.test(os)) result.push({ label: 'win11', reason: `OS が ${md(os)}` });
  else if (/^Windows 10/.test(os)) result.push({ label: 'win10', reason: `OS が ${md(os)}` });
  if (/Enterprise|Education/i.test(os)) result.push({ label: '企業PC', reason: 'Windows のエディションが Enterprise / Education (会社・学校の PC は制限があることが多い)' });
  if (/^Meltype \S/.test(`Meltype ${env['Meltype'] ?? ''}`) && /^\d+\.\d+\.\d+/.test(env['Meltype'] ?? '')) result.push({ label: `v${env['Meltype'].match(/^\d+\.\d+\.\d+/)[0]}`, reason: '実行環境の版' });
  if (/はい/.test(env['管理者として実行'] ?? '')) result.push({ label: '管理者で実行', reason: 'Meltype を管理者として実行していた' });
  if (/Mozc:\s*なし/.test(env['変換エンジン'] ?? '')) result.push({ label: 'Mozcなし', reason: 'Mozc の変換ヘルパーが無い' });
  if (/Kana/.test(env['入力方式'] ?? '')) result.push({ label: 'かな入力', reason: '入力方式がかな入力' });
  if (/日本語以外/.test(env['キーボード'] ?? '')) result.push({ label: 'USキーボード', reason: `キーボードが ${md(env['キーボード'])}` });
  const scale = Number((env['画面'] ?? '').match(/拡大率\s*(\d+)/)?.[1] ?? 100);
  if (scale > 100) result.push({ label: '高DPI', reason: `画面の拡大率が ${scale}%` });
  return result;
}

function triageLabels(form, labels) {
  const result = [];
  const text = Object.entries(form).filter(([k]) => !/実行環境|ログ/.test(k)).map(([, v]) => v).join('\n');
  const app = field(form, 'どのアプリで') + '\n' + text;
  const isBug = labels.has('bug') || 'どうなったか' in form;
  if (isBug && ![...labels].some(l => l.startsWith('優先: '))) {
    const severe = text.match(/止ま|落ち|固ま|フリーズ|クラッシュ|入力できな|打てな|文字が消え|消えた|起動しな|起動でき|インストールでき|動かな|反映され[なず]|違う文字|別の文字|化け|連続で入力|勝手に|二重に|重複/);
    const minor = text.match(/見た目|表示がずれ|位置がずれ|ちらつ|色が|文言|誤字|デザイン|アイコン/);
    const once = field(form, '起きる頻度') === '一度だけ';
    result.push(severe
      ? { label: '優先: 高', reason: `本文に「${severe[0]}」とある (入力できない・止まる・違う文字が入る系)` }
      : minor || once
        ? { label: '優先: 低', reason: minor ? `本文に「${minor[0]}」とある (見た目の問題)` : '起きる頻度が「一度だけ」' }
        : { label: '優先: 中', reason: '不具合の報告 (止まる・入力できない等の言葉はない)' });
    const install = text.match(/インストール|アンインストール|更新|アップデート|起動/);
    if (install) result.push({ label: 'インストール', reason: `本文に「${install[0]}」とある` });
  }
  const areas = [
    ['アプリ: チャット', /Discord|Slack|LINE|Teams|Messenger|Telegram|チャット/i],
    ['アプリ: コード', /VS ?Code|Visual Studio|Cursor|JetBrains|IntelliJ|ターミナル|Terminal|PowerShell|コマンドプロンプト|Maya|Blender|エディター|エディタ/i],
    ['アプリ: ブラウザー', /Chrome|Edge|Firefox|Safari|Brave|ブラウザ/i],
    ['アプリ: ゲーム', /ゲーム|Steam|Minecraft|Valorant|原神/i],
    ['アプリ: Office', /Word|Excel|PowerPoint|Outlook|OneNote|Office/],
  ];
  for (const [label, pattern] of areas) {
    const match = app.match(pattern);
    if (match) result.push({ label, reason: `「${match[0]}」で起きた` });
  }
  return result;
}

const Help = [
  'Meltype bot のコマンド (コメントの行の先頭に書いてください。`/release`・`/pre-release` は 1 行目だけ。1 つのコメントに 1 つ。`/repro` だけは 1 行に 1 つずつ複数書けます):',
  '',
  '| コマンド | すること | 使える人 |',
  '|---|---|---|',
  '| `/repro <打ったキー> [space\\|enter\\|none]` | 最新のコードで打つ (Mozc があれば漢字も) | だれでも |',
  '| `/explain <打ったキー>` | 英数状態の検知での 1 文字ずつの判定理由 | だれでも |',
  '| `/ca <打ったキー>` | Space で変換して、文節の区切りと最初の文節の候補 (9 件まで) を出す | だれでも |',
  '| `/jht <出てほしい文> [/ 読み]` | 考えられるローマ字打ちで変換テスト (読みはオプション) | だれでも |',
  '| `/test <SHA>` | Pull Request のコードでテストを流す | メンテナー |',
  '| `/pack <SHA>` | Pull Request のコードでテスト版の zip を作る | メンテナー |',
  '| `/test-repro <SHA> <キー>` | PR のコードで `/repro` | メンテナー |',
  '| `/test-jht <SHA> <文> [/ 読み]` | PR のコードで `/jht` | メンテナー |',
  '| `/test-explain <SHA> <キー>` | PR のコードで `/explain` | メンテナー |',
  '| `/release <版>` | 版 (例: `1.2.3`) のタグとリリースの下書きを作り、Windows・Mac・Linux の配布物のビルドを始める。csproj・Info.plist の版が <版> でないか、docs/release-notes/<版>.md が無ければ止める | 管理者 |',
  '| `/pre-release <版>` | `/release` のプレリリース版 (版は `1.2.3-pre.1` の形) | 管理者 |',
  '| `/self-assign` | 自分を Issue の Assignees に入れる (担当になる)。`要トリアージ` の間はメンテナーだけ | だれでも |',
  '| `/help` | この一覧 | だれでも |',
  '',
  'PR のコードを動かすコマンドの `<SHA>` は、中身を確かめた commit の SHA (40 桁)。PR の今の head と違えば動かしません。',
].join('\n');

// /release <版>・/pre-release <版> (issue #665)。管理者 (admin / maintain) だけ。
//   1. 版の形を確かめる (release: 1.2.3 / pre-release: 1.2.3-pre.1)
//   2. main の csproj (2 つ)・Info.plist の版が <版> と合うか確かめる (版を上げ忘れた配布を防ぐ: issue #601)
//   3. タグ v<版> を main の先頭に作る (もうあれば止める)
//   4. リリースの下書きを作る。本文は main の docs/release-notes/<版>.md (配布物にも同じものを同梱する: issue #1207)。無ければ止める。pre-release は prerelease
//   5. 配布物のビルド (build / mac / linux) を、そのタグで始める。ビルドが終わると、下書きに配布物が付く
// タグを GITHUB_TOKEN で作ると push の起動が掛からないので、workflow_dispatch で明示的に起動する。
async function releaseCommand(issue, command, version) {
  const reply = text => upsertComment(issue, `release-${event.comment.id}`, text);
  const pre = command === 'pre-release';
  const permission = await gh('GET', `/collaborators/${event.comment.user.login}/permission`).then(r => r.permission).catch(() => 'none');
  if (!['admin', 'maintain'].includes(permission)) return reply(`\`/${command}\` は管理者 (admin / maintain) だけが使えます。`);
  const shape = pre ? /^\d+\.\d+\.\d+-pre\.\d+$/ : /^\d+\.\d+\.\d+$/;
  if (!shape.test(version)) return reply(`版を ${pre ? '`1.2.3-pre.1`' : '`1.2.3`'} の形で書いてください (例: \`/${command} ${pre ? '1.2.3-pre.1' : '1.2.3'}\`)。`);
  await react('eyes');
  const read = async file => Buffer.from((await gh('GET', `/contents/${file}?ref=main`)).content, 'base64').toString('utf8');
  const mismatches = [];
  for (const file of ['src/Meltype/Meltype.csproj', 'src/Meltype.Core/Meltype.Core.csproj']) {
    const found = (await read(file)).match(/<Version>([^<]+)<\/Version>/)?.[1];
    if (found !== version) mismatches.push(`${file}: ${found ?? '(版なし)'}`);
  }
  const plain = version.replace(/-.*$/, '');
  const plist = await read('mac/Resources/Info.plist');
  const plistVersion = plist.match(/<key>CFBundleShortVersionString<\/key>\s*<string>([^<]+)<\/string>/)?.[1];
  if (plistVersion !== plain) mismatches.push(`mac/Resources/Info.plist: ${plistVersion ?? '(版なし)'} (期待 ${plain})`);
  // リリースノートは配布物に同梱し、更新したあとの初めての起動で見せるので、先に main に入れておく
  const notesFile = `docs/release-notes/${version}.md`;
  const notes = await read(notesFile).catch(() => null);
  if (!notes?.trim()) mismatches.push(`${notesFile}: (無い。リリースノートを書いて main に入れてください)`);
  if (mismatches.length > 0) return reply(`❌ main で ${version} のリリースの準備 (版・リリースノート) ができていないので、リリースしません (docs/RELEASE.md の「版を上げる」を先に main に入れてください):\n\n${mismatches.map(m => `- ${m}`).join('\n')}`);
  const tag = `v${version}`;
  if (await gh('GET', `/git/ref/tags/${tag}`).then(() => true).catch(() => false)) return reply(`❌ タグ \`${tag}\` はもうあります。`);
  const sha = (await gh('GET', '/git/ref/heads/main')).object.sha;
  await gh('POST', '/git/refs', { ref: `refs/tags/${tag}`, sha });
  await gh('POST', '/releases', { tag_name: tag, name: `Meltype ${version}`, draft: true, prerelease: pre, body: notes });
  const started = [], failed = [];
  for (const workflow of ['build.yml', 'mac.yml', 'linux.yml']) {
    try {
      await gh('POST', `/actions/workflows/${workflow}/dispatches`, { ref: tag }, dispatchToken);
      started.push(workflow);
    } catch (error) {
      failed.push(`${workflow}: ${error.message.slice(0, 120)}`);
    }
  }
  await reply([
    `✅ タグ \`${tag}\` (${sha.slice(0, 7)}) とリリースの下書き (${pre ? 'プレリリース' : '通常'}、本文は \`${notesFile}\`) を作りました。`,
    started.length > 0 ? `\nビルドを始めました: ${started.join('・')}。終わると下書きに配布物が付くので、中身を確かめてから公開してください。` : '',
    failed.length > 0 ? `\n⚠ 始められなかったビルド (Actions の画面から ${tag} で手で実行してください):\n${failed.map(f => `- ${f}`).join('\n')}` : '',
  ].join('\n'));
}

// /self-assign で、コメントした人を Assignees に入れる (issue #1098)。
// 「着手します」「対応します」などだけのコメントには、/self-assign の案内を返す (言葉では割り当てない)。
// 案内は 1 行目がその言葉だけのときに限る (「対応しますか?」「だれか対応しますか」などの質問や、文の途中の言葉には返さない)
// 「要トリアージ」が付いている間は、メンテナー以外は割り当てない (メンテナーが進めると決めてから /self-assign する)
const Untriaged = '要トリアージ';
const UntriagedNote = 'この Issue はまだメンテナーが確認していません (`要トリアージ`)。対応したい場合は、そのことをコメントで残しておいてください。メンテナーが進めると決めて `要トリアージ` を外したら、`/self-assign` とだけコメントしてから対応してください。';
const untriaged = () => event.issue.labels?.some(l => (l.name ?? l) === Untriaged);
const isClaim = line => /^(これ[はをも]?|この\s*issue\s*[はをも]?|私が|わたしが|僕が|ぼくが|自分が)?\s*(着手|対応|担当|作業)(します|しています|してみます|させてください|させていただきます)[。.!！]*$/i.test(line.replace(/\s+$/, ''));

async function selfAssign(issue, login) {
  if (event.issue.assignees?.some(a => a.login === login)) return react('+1');
  // 割り当てられない人 (Issue に関われない人) は、GitHub が黙って外す。入ったかどうかは返事で確かめる
  const updated = await gh('POST', `/issues/${issue}/assignees`, { assignees: [login] });
  if (updated.assignees?.some(a => a.login === login)) return react('+1');
  await upsertComment(issue, `error-${event.comment.id}`, 'Assignees に入れられませんでした (GitHub の制限で、この Issue に割り当てられない人がいます)。メンテナーに頼んでください。');
}

async function parse() {
  const issue = event.issue.number;
  const isPr = Boolean(event.issue.pull_request);

  if (!event.comment) {
    if (event.action !== 'opened' && event.action !== 'edited') return output({ action: 'none' });
    const rerunOnEdit = process.env.SEARCH_RELATED_ISSUES_ON_EDIT === 'true';
    if (event.action === 'opened' || rerunOnEdit) await updateRelatedIssues(event.issue);
    const form = parseForm(event.issue.body);
    const labels = new Set(event.issue.labels.map(l => l.name));
    const add = new Set(osLabels(field(form, 'OS')));
    if (event.action === 'opened') add.add('要トリアージ');
    const reasons = [];
    for (const { label, reason } of [...environmentLabels(field(form, '実行環境')), ...triageLabels(form, labels)]) {
      if (!add.has(label)) reasons.push(`\`${label}\`: ${reason}`);
      add.add(label);
    }

    const problems = [];
    const version = field(form, 'Meltype の版');
    if ('Meltype の版' in form && !/\d+\.\d+|^main$/i.test(version)) problems.push('**Meltype の版** を書いてください (Windows: トレイのアイコンを右クリック →「Meltype について...」)。');
    const misdetection = labels.has('誤判定') || '出たもの' in form;
    const keys = field(form, '打ったもの');
    if (misdetection && !isKeys(keys)) problems.push('**打ったもの** は、変換された文字ではなく、押したキーをそのまま英字で書いてください (例: `nihongowohanasu`)。');

    if (problems.length > 0) add.add('情報待ち');
    else if (labels.has('情報待ち')) await gh('DELETE', `/issues/${issue}/labels/${encodeURIComponent('情報待ち')}`).catch(() => {});
    for (const label of ['優先: 高', '優先: 中', '優先: 低', ...add]) await ensureLabel(label);
    if (add.size > 0) await gh('POST', `/issues/${issue}/labels`, { labels: [...add] });

    const text = [];
    if (problems.length > 0) text.push('報告ありがとうございます。確認のために、もう少し教えてください。', '', ...problems.map(p => `- ${p}`), '', '本文を編集して直してもらえれば、自動でもう一度確認します。', '');
    if (reasons.length > 0) text.push('<details><summary>🤖 自動の仕分け</summary>', '', ...reasons.map(r => `- ${r}`), '', '優先度・分類は本文からの見当です。作者が確認したら `要トリアージ` を外します。', '</details>');
    if (text.length > 0) await upsertComment(issue, 'triage', text.join('\n'));

    // 再現に使うのは打ったものと最後のキーだけなので、ほかの情報が足りなくても打ってみる
    if (misdetection && isKeys(keys)) {
      return output({
        action: 'repro', issue, repros: JSON.stringify([{ keys: keys.slice(0, MaxKeys), last: lastKey(field(form, '最後に押したキー')) }]),
        reported: field(form, '出たもの'), expected: field(form, '期待した結果'), kind: field(form, '種類'),
      });
    }
    return output({ action: 'none' });
  }

  const maintainer = Maintainers.includes(event.comment.author_association);
  const lines = (event.comment.body ?? '').split('\n').map(l => l.trim());
  if (!isPr && event.issue.state === 'open' && event.comment.user?.type !== 'Bot' && isClaim(lines[0])
      && !event.issue.assignees?.some(a => a.login === event.comment.user.login)) {
    await upsertComment(issue, 'self-assign-hint', untriaged() && !maintainer ? UntriagedNote : 'この Issue の担当になるときは、`/self-assign` とだけコメントしてください。bot が Assignees に入れます。');
    return output({ action: 'none' });
  }
  // コマンドはどの行に書いてもよい。/test/foo のようなパスはコマンドにしない。
  // /release・/pre-release は 1 行目だけ (手順の説明やコードブロックの中の行でタグとリリースを作らないため)
  const commands = lines.map(l => l.match(/^\/(self-assign|repro|explain|ca|jht|test-repro|test-jht|test-explain|test|pack|pre-release|release|help)(?=\s|$)\s*(.*)$/))
    .filter((m, i) => m && (i === 0 || !['release', 'pre-release'].includes(m[1])));
  if (commands.length === 0 || event.comment.user?.type === 'Bot') return output({ action: 'none' });
  if (commands.length > 1 && !commands.every(([, c]) => c === 'repro')) {
    await upsertComment(issue, `error-${event.comment.id}`, `1 つのコメントに書けるコマンドは 1 つです (\`/repro\` だけは 1 行に 1 つずつ ${MaxRepros} 個まで書けます)。`);
    return output({ action: 'none' });
  }
  if (commands.length > MaxRepros) {
    await upsertComment(issue, `error-${event.comment.id}`, `\`/repro\` は 1 つのコメントに ${MaxRepros} 個までです。`);
    return output({ action: 'none' });
  }
  const [, command, rest] = commands[0];
  const prOnly = ['test', 'pack', 'test-repro', 'test-jht', 'test-explain'];

  if (command === 'release' || command === 'pre-release') {
    // 権限の広い処理は parse では行わない (parse はだれのコメントでも動くので、権限は contents: read のまま)。
    // 別の job (release) に頼む。管理者かどうかの確認は release の中で、書き込みの前に行う
    return output({ action: 'none', release_command: command, release_version: rest.trim().replace(/^`|`$/g, '').replace(/^v/, '').slice(0, 40) });
  }
  if (command === 'self-assign') {
    if (isPr || event.issue.state !== 'open') {
      await upsertComment(issue, `error-${event.comment.id}`, '`/self-assign` は開いている Issue でだけ使えます。');
      return output({ action: 'none' });
    }
    if (untriaged() && !maintainer) {
      await upsertComment(issue, `error-${event.comment.id}`, UntriagedNote);
      return output({ action: 'none' });
    }
    await selfAssign(issue, event.comment.user.login);
    return output({ action: 'none' });
  }
  if (command === 'help') {
    await upsertComment(issue, `help-${event.comment.id}`, Help);
    return output({ action: 'none' });
  }
  if (prOnly.includes(command)) {
    if (!isPr) {
      await upsertComment(issue, `error-${event.comment.id}`, `\`/${command}\` は Pull Request でだけ使えます。`);
      return output({ action: 'none' });
    }
    if (!maintainer) {
      await upsertComment(issue, `error-${event.comment.id}`, `\`/${command}\` はメンテナーだけが使えます (Pull Request のコードを動かすため)。`);
      return output({ action: 'none' });
    }
    const pr = await gh('GET', `/pulls/${issue}`);
    // メンテナーが確かめた commit だけを動かす。コメントの後に head を差し替えられても、見ていないコードは動かさない。
    // 40 桁の完全一致だけを受け付ける (短い前方一致だと、先頭が同じ無害な commit と悪意のある commit を作って差し替えられる)
    const given = rest.trim().split(/\s+/)[0];
    const sha = given.replace(/^`|`$/g, '').toLowerCase();
    const args = rest.trim().slice(given.length).trim();
    const head = pr.head.sha;
    if (!/^[0-9a-f]{40}$/.test(sha) || sha !== head) {
      const usage = { 'test-repro': ' <キー>', 'test-jht': ' <文>', 'test-explain': ' <キー>' }[command] ?? '';
      const why = /^[0-9a-f]{40}$/.test(sha) ? `\`${sha}\` は PR の今の head と違います。` : `\`/${command}\` には、中身を確かめた commit の SHA (40 桁) を付けてください。`;
      await upsertComment(issue, `error-${event.comment.id}`, `${why}今の head は \`${head}\` です。確かめてから \`/${command} ${head}${usage}\` と書いてください。`);
      return output({ action: 'none' });
    }
    await react('eyes');
    if (command === 'test' || command === 'pack') {
      return output({ action: command, issue, sha: pr.head.sha, comment: event.comment.id });
    }
    if (command === 'test-jht') {
      const text = args.replace(/^`|`$/g, '');
      if (!text) {
        await upsertComment(issue, `error-${event.comment.id}`, `出てほしい文を書いてください (例: \`/test-jht ${head} 私はgoogleが好きです\` または \`/test-jht ${head} 私はgoogleが好きです / わたしはgoogleがすきです\`)。`);
        return output({ action: 'none' });
      }
      return output({ action: 'test-jht', issue, keys: text.slice(0, 500), sha: pr.head.sha, comment: event.comment.id });
    }
    const request = command === 'test-repro' ? reproRequest(args) : { keys: keysOf(args) };
    if (!isKeys(request.keys)) {
      await upsertComment(issue, `error-${event.comment.id}`, `打ったキーを英字で書いてください (例: \`/${command} ${head} nihongowohanasu\`)。`);
      return output({ action: 'none' });
    }
    if (command === 'test-repro') return output({ action: command, issue, repros: JSON.stringify([request]), reported: '', expected: '', kind: '', sha: pr.head.sha, comment: event.comment.id });
    return output({ action: command, issue, keys: request.keys, sha: pr.head.sha, comment: event.comment.id });
  }

  if (command === 'jht') {
    const text = rest.trim().replace(/^`|`$/g, '');
    if (!text) {
      await upsertComment(issue, `error-${event.comment.id}`, `出てほしい文を書いてください (例: \`/jht こんにちは\` または \`/jht 私はgoogleが好きです / わたしはgoogleがすきです\`)。\n\n${Help}`);
      return output({ action: 'none' });
    }
    await react('eyes');
    return output({ action: 'jht', issue, keys: text.slice(0, 500), comment: event.comment.id });
  }

  // ca は末尾の space / enter / none も打つ文字として渡す。最後のキーの指定か本当に打った文字かを bot は見分けられないため (issue #239)
  const requests = command === 'repro' ? commands.map(([, , r]) => reproRequest(r))
    : [{ keys: keysOf(rest), last: 'space' }];
  if (!requests.every(r => isKeys(r.keys))) {
    const example = command === 'ca' ? 'kisha' : 'nihongowohanasu';
    await upsertComment(issue, `error-${event.comment.id}`, `打ったキーを英字で書いてください (例: \`/${command} ${example}\`)。\n\n${Help}`);
    return output({ action: 'none' });
  }
  await react('eyes');
  if (command === 'explain') return output({ action: command, issue, keys: requests[0].keys, comment: event.comment.id });
  return output({ action: command, issue, repros: JSON.stringify(requests), reported: '', expected: '', kind: '', comment: event.comment.id });
}

const keysOf = text => text.trim().split(/\s+/).join(' ').replace(/^`|`$/g, '').slice(0, MaxKeys);

// /repro・/test-repro の「<打ったキー> [space|enter|none]」
function reproRequest(text) {
  const words = text.trim().split(/\s+/);
  const last = ['space', 'enter', 'none'].includes(words.at(-1)) && words.length > 1 ? words.pop() : 'enter';
  return { keys: keysOf(words.join(' ')), last };
}

// 改行は空白にする (空行でコードスパンが切れ、後ろが Markdown として表示されるため)
const code = text => '`' + String(text ?? '').replace(/\r?\n|\r/g, ' ').replace(/`/g, 'ˋ') + '`';
// 表のセルは記号を文字参照にし、元の文字を保って表示する。
const cell = codeCell;
// 利用者が書いた文をそのまま出す。リンク・画像・HTML にならないよう ASCII の記号を \ でエスケープし、
// @ の後ろに幅の無い空白を入れてメンションにしない
// 改行は空白にする (表のセルの中でも 1 行に収める)
const md = text => String(text ?? '').replace(/\r?\n|\r/g, ' ').replace(/[!-/:-?[-`{-~]/g, '\\$&').replace(/@/g, '@\u200b');
// 複数行の出力を、そのまま見せるコードブロックにする。PR のコードが出した結果は ``` を含められるので、
// 中のどのバッククォートの並びより長い区切りにして、途中で閉じられないようにする
const fence = text => {
  const body = String(text ?? '');
  const longest = Math.max(0, ...(body.match(/`+/g) ?? []).map(run => run.length));
  const mark = '`'.repeat(Math.max(3, longest + 1));
  return `${mark}\n${body}\n${mark}`;
};

function readJson(file) {
  try {
    const lines = fs.readFileSync(file, 'utf8').trim().split('\n');
    return JSON.parse(lines.at(-1));
  } catch {
    return null;
  }
}

function readText(file, max = 6000) {
  try {
    const text = fs.readFileSync(file, 'utf8');
    return text.length > max ? '…\n' + text.slice(-max) : text;
  } catch {
    return null;
  }
}

async function report() {
  const action = process.env.BOT_ACTION;
  const issue = process.env.BOT_ISSUE;
  const dir = process.env.BOT_RESULTS ?? 'results';
  const runUrl = `${process.env.GITHUB_SERVER_URL}/${repo}/actions/runs/${process.env.GITHUB_RUN_ID}`;
  const keys = process.env.BOT_KEYS;

  // repro・ca の結果は results/<repros の番号>/repro-<版>.json
  const repros = JSON.parse(process.env.BOT_REPROS || '[]');
  const reproResults = i => {
    const sub = path.join(dir, String(i));
    const files = fs.existsSync(sub) ? fs.readdirSync(sub).filter(f => f.startsWith('repro-')).sort() : [];
    return files.map(file => ({ name: file.replace(/^repro-/, '').replace(/\.json$/, ''), r: readJson(path.join(sub, file)) }));
  };

  if (action === 'repro' || action === 'test-repro') {
    const marker = process.env.BOT_COMMENT_ID ? `repro-${process.env.BOT_COMMENT_ID}` : 'repro';
    const reported = process.env.BOT_REPORTED, expected = process.env.BOT_EXPECTED;
    const runs = repros.map((r, i) => ({ ...r, results: reproResults(i) }));
    if (runs.every(r => r.results.length === 0)) {
      await upsertComment(issue, marker, `再現を試せませんでした ([実行結果](${runUrl}))。`);
      return;
    }
    const sections = [], verdicts = [];
    let engine = '';
    for (const { keys, last, results } of runs) {
      if (results.length === 0) {
        sections.push(`${code(keys)} は再現を試せませんでした。`);
        continue;
      }
      const rows = [];
      let verdict;
      for (const { name, r } of results) {
        if (!r) { rows.push(`| ${md(name)} | (失敗) |`); continue; }
        if (r.engine) engine = r.engine;
        const result = (r.committed ?? '') + (r.composing ?? '');
        const extra = r.converted && r.converted !== result ? ` / Space: ${cell(r.converted)}` : '';
        rows.push(`| ${md(name)} | ${cell(result)}${extra} |`);
        if ((name === 'main' || name === 'pr') && !verdict) {
          if (expected && (result === expected || r.converted === expected)) verdict = 'match';
          else if (reported && (result === reported || r.converted === reported)) verdict = 'reproduced';
          else verdict = 'different';
        }
      }
      verdicts.push(verdict);
      sections.push([
        `${code(keys)} を打ってみました (最後のキー: ${last}${action === 'test-repro' ? `、PR ${process.env.BOT_SHA?.slice(0, 7)}` : ''})。`,
        '',
        '| 版 | 結果 |',
        '|---|---|',
        ...rows,
      ].join('\n'));
    }
    const hasMozc = /Mozc/i.test(engine);
    const lines = [sections.join('\n\n'), ''];
    if (!hasMozc) lines.push('ℹ️ Mozc の変換ヘルパーが使えなかったため、**漢字変換は検証していません** (日本語 / 英語の判定と入力結果のみ)。');
    else if (verdicts.includes('reproduced')) lines.push('🔁 報告と同じ結果になりました。**再現しました。**');
    else if (verdicts.includes('match')) lines.push('✅ 最新のコードでは期待どおりでした。次の版で直っているかもしれません。');
    else if (expected || reported) lines.push('報告とは違う結果になりました。アプリ・前後の文・学習で変わることがあります。');
    lines.push('', `<sub>変換エンジン: ${engine ? md(engine) : 'なし (判定だけ)'}。${!hasMozc ? '漢字の正しさは未確認。' : ''}[実行結果](${runUrl})</sub>`);
    await upsertComment(issue, marker, lines.join('\n'));
    if (verdicts.includes('reproduced')) await gh('POST', `/issues/${issue}/labels`, { labels: ['再現済み'] }).catch(() => {});
    return;
  }

  if (action === 'ca') {
    const marker = `ca-${process.env.BOT_COMMENT_ID}`;
    const keys = repros[0]?.keys;
    const results = reproResults(0);
    // 結果が無い・どれも読めない (dotnet run の失敗など) ときは、Mozc が無いせいにせず実行の失敗として返す
    if (results.every(v => !v.r)) {
      await upsertComment(issue, marker, `候補を出せませんでした ([実行結果](${runUrl}))。`);
      return;
    }
    const engine = results.map(v => v.r?.engine).find(Boolean) ?? '';
    if (!results.some(v => /Mozc/i.test(v.r?.engine ?? ''))) {
      await upsertComment(issue, marker, `ℹ️ Mozc の変換ヘルパーが使えなかったため、候補を出せませんでした。\n\n[実行結果](${runUrl})`);
      return;
    }
    const clauseCell = ({ r }) => {
      if (!r) return '(失敗)';
      if (r.clauses) return r.clauses.map(cell).join(' ｜ ');
      // 変換中でも文節に分かれていないときは clauses が無い (Repro.cs)。変換した結果をそのまま出す
      if (r.candidates) return cell(r.converted);
      return `変換されず ${cell((r.committed ?? '') + (r.composing ?? ''))} と確定しました`;
    };
    const names = results.map(v => md(v.name));
    const lines = [
      `${code(keys)} を Space で変換しました。`,
      '',
      '| 版 | 文節 |',
      '|---|---|',
      ...results.map(v => `| ${md(v.name)} | ${clauseCell(v)} |`),
      '',
    ];
    const count = Math.max(0, ...results.map(v => v.r?.candidates?.length ?? 0));
    if (count > 0) {
      lines.push('最初の文節の候補:', '', `| # | ${names.join(' | ')} |`, `|---|${names.map(() => '---').join('|')}|`);
      for (let i = 0; i < count; i++) {
        const cells = results.map(v => { const c = v.r?.candidates?.[i]; return c == null ? '' : cell(c); });
        lines.push(`| ${i + 1} | ${cells.join(' | ')} |`);
      }
      lines.push('');
    }
    lines.push(`<sub>変換エンジン: ${md(engine)}。[実行結果](${runUrl})</sub>`);
    await upsertComment(issue, marker, lines.join('\n'));
    return;
  }

  if (action === 'explain' || action === 'test-explain') {
    const text = readText(path.join(dir, 'explain.txt'));
    const head = action === 'test-explain' ? ` (PR ${process.env.BOT_SHA?.slice(0, 7)})` : '';
    await upsertComment(issue, `explain-${process.env.BOT_COMMENT_ID}`,
      text ? `${code(keys)} の判定理由 (英数状態の検知)${head}:\n\n${fence(text.trim())}` : `判定理由を出せませんでした ([実行結果](${runUrl}))。`);
    return;
  }

  if (action === 'jht' || action === 'test-jht') {
    const data = readJson(path.join(dir, 'jht.json'));
    if (!data) {
      await upsertComment(issue, `jht-${process.env.BOT_COMMENT_ID}`, `jht を試せませんでした ([実行結果](${runUrl}))。`);
      return;
    }
    if (data.error) {
      await upsertComment(issue, `jht-${process.env.BOT_COMMENT_ID}`, `⚠️ ${md(data.error)}\n\n漢字を含む文は \`/jht 文 / 読み\` の形で読みを書いてください (例: \`/jht 私はgoogleが好きです / わたしはgoogleがすきです\`)。\n\n[実行結果](${runUrl})`);
      return;
    }
    const results = data.results ?? [];
    const okLive = results.filter(r => r.liveOk).length;
    const okFirst = results.filter(r => r.firstOk).length;
    const rows = results.slice(0, 12).map(r => {
      const mark = r.liveOk ? '✅' : r.firstOk ? '🔶' : '❌';
      const note = (r.notes ?? []).slice(0, 2).map(md).join('; ');
      return `| ${mark} ${cell(r.keys)} | ${cell(r.entered)} | ${cell(r.first)} | ${note || ''} |`;
    });
    const head = action === 'test-jht' ? ` (PR ${process.env.BOT_SHA?.slice(0, 7)})` : '';
    const lines = [
      `**jht**${head}: ${code(data.expected)}`,
      data.reading ? `読み: ${code(data.reading)}` : '',
      `打ち方 ${results.length} 通り (Enter 一致 ${okLive}/${results.length}、Space 先頭 ${okFirst}/${results.length})。変換エンジン: ${data.engine ? md(data.engine) : 'なし (判定だけ)'}${/Mozc/i.test(data.engine ?? '') ? '' : ' — **漢字変換は未検証**'}`,
      '',
      '| 打ち方 | Enter | Space 先頭 | メモ |',
      '|---|---|---|---|',
      ...rows,
      results.length > 12 ? `| … | (残り ${results.length - 12} 通り) | | |` : '',
      '',
      `[実行結果](${runUrl})`,
    ].filter(Boolean);
    await upsertComment(issue, `jht-${process.env.BOT_COMMENT_ID}`, lines.join('\n'));
    return;
  }

  if (action === 'test') {
    const text = readText(path.join(dir, 'test.txt'));
    const summary = text?.match(/(\d+)\/(\d+) passed/);
    const ok = summary && summary[1] === summary[2];
    const failures = text ? text.split('\n').filter(l => /FAIL/.test(l)).slice(0, 20).join('\n') : '';
    await upsertComment(issue, `test-${process.env.BOT_COMMENT_ID}`, [
      `${ok ? '✅' : '❌'} テスト (${process.env.BOT_SHA?.slice(0, 7)}): ${summary ? `${summary[1]}/${summary[2]} 通過` : '結果を読めませんでした'}`,
      failures ? `\n${fence(failures)}` : '',
      `\n[実行結果](${runUrl})`,
    ].join('\n'));
    return;
  }

  if (action === 'pack') {
    const ok = process.env.BOT_JOB_RESULT === 'success';
    await upsertComment(issue, `pack-${process.env.BOT_COMMENT_ID}`, ok
      ? `📦 テスト版の zip を作りました (${process.env.BOT_SHA?.slice(0, 7)})。[実行結果](${runUrl}) の Artifacts の **meltype-pr-${issue}** からダウンロードできます (Windows 用)。`
      : `❌ zip を作れませんでした。[実行結果](${runUrl})`);
  }
}

const mode = process.argv[2];
try {
  if (mode === 'parse') await parse();
  else if (mode === 'release') {
    const command = process.env.BOT_RELEASE_COMMAND;
    if (command === 'release' || command === 'pre-release') await releaseCommand(event.issue.number, command, process.env.BOT_RELEASE_VERSION ?? '');
  }
  else if (mode === 'report') {
    await report();
    await react(process.env.BOT_JOB_RESULT === 'success' ? 'rocket' : 'confused');
  }
} catch (error) {
  console.error(error);
  process.exitCode = 1;
}
