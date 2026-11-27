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

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;
// workflow_dispatch とリリースのタグの作成は job の GITHUB_TOKEN で叩く (App に Actions の権限が無い・App で作ったタグは push でも release.yml を起動してしまう)
const dispatchToken = process.env.BOT_DISPATCH_TOKEN || token;
const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
const Marker = '<!-- meltype-bot -->';
const Maintainers = ['OWNER', 'MEMBER', 'COLLABORATOR'];
const MaxKeys = 300;
const MaxRepros = 10;
// /learn の手順の数と、確定した語の長さの上限 (Repro.cs の MaxLessons と同じ数)
const MaxLessons = 5;
const MaxLessonText = 50;

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

async function react(content) {
  if (event.comment) await gh('POST', `/issues/comments/${event.comment.id}/reactions`, { content }).catch(() => {});
}

const Help = [
  'Meltype bot のコマンド (コメントの行の先頭に書いてください。`/release`・`/pre-release` は 1 行目だけ。1 つのコメントに 1 つ。`/repro` だけは 1 行に 1 つずつ複数書けます。`/learn` は `/repro`・`/ca` と同じコメントに書きます):',
  '',
  '| コマンド | すること | 使える人 |',
  '|---|---|---|',
  '| `/repro <打ったキー> [space\\|enter\\|none]` | main と最新リリースのコードで、Mozc だけと Mozc + Zenzai で打つ | だれでも |',
  '| `/repro <SHA> <打ったキー> [space\\|enter\\|none]` | `/repro` を PR の前・PR・main のコードで行い、PR で結果が変わったか比べる | メンテナー (PR のみ) |',
  '| `/explain <打ったキー>` | 英数状態の検知での 1 文字ずつの判定理由 | だれでも |',
  '| `/ca <打ったキー>` | Space で変換して、文節の区切りと最初の文節の候補 (9 件まで) を出す | だれでも |',
  `| \`/learn <打ったキー> [→ <確定した語>]\` | 同じコメントの \`/repro\`・\`/ca\` の前に、別の入力欄で打って確定し、学習させる (確定した語があれば Space で変換して候補から選ぶ)。${MaxLessons} 行まで | だれでも |`,
  '| `/jht <出てほしい文> [/ 読み]` | 考えられるローマ字打ちで変換テスト (読みはオプション) | だれでも |',
  '| `/test <SHA>` | Pull Request のコードでテストを流す | メンテナー |',
  '| `/pack <SHA>` | Pull Request のコードでテスト版のインストーラー (setup.exe) を作る | メンテナー |',
  '| `/test-jht <SHA> <文> [/ 読み]` | PR のコードで `/jht` | メンテナー |',
  '| `/test-explain <SHA> <キー>` | PR のコードで `/explain` | メンテナー |',
  '| `/release <版>` | 版 (例: `1.2.3`) のタグとリリースの下書きを作り、Windows・Mac・Linux の配布物のビルドを始める。src/Directory.Build.props・Info.plist の版が <版> でないか、docs/release-notes/<版>.md が無ければ止める | 管理者 |',
  '| `/pre-release <版>` | `/release` のプレリリース版 (版は `1.2.3-pre.1`・`1.2.3-rc.1` の形) | 管理者 |',
  '| `/self-assign` | 自分を Issue の Assignees に入れる (担当になる)。`要トリアージ` の間はメンテナーだけ | だれでも |',
  '| `/help` | この一覧 | だれでも |',
  '',
  'PR のコードを動かすコマンドの `<SHA>` は、中身を確かめた commit の SHA (40 桁)。PR の今の head と違えば動かしません。',
].join('\n');

// /release <版>・/pre-release <版> (issue #665)。管理者 (admin / maintain) だけ。
//   1. 版の形を確かめる (release: 1.2.3 / pre-release: 1.2.3-pre.1・1.2.3-rc.1)
//   2. main の src/Directory.Build.props・Info.plist の版が <版> と合うか確かめる (版を上げ忘れた配布を防ぐ: issue #601)
//   3. タグ v<版> を main の先頭に作る (もうあれば止める)
//   4. リリースの下書きを作る。本文は main の docs/release-notes/<版>.md (配布物にも同じものを同梱する: issue #1207)。無ければ止める。pre-release は prerelease
//   5. 配布物のビルド (release.yml) を、そのタグで始める。ビルドが終わると、下書きに配布物を付けて公開する
// タグは job の GITHUB_TOKEN (dispatchToken) で作る。GITHUB_TOKEN の push では release.yml の push: tags が起動しないので、
// workflow_dispatch で明示的に起動する。App のトークンで作ると push でも起動し、ビルドが 2 回走る。
async function releaseCommand(issue, command, version) {
  const reply = text => upsertComment(issue, `release-${event.comment.id}`, text);
  const pre = command === 'pre-release';
  const permission = await gh('GET', `/collaborators/${event.comment.user.login}/permission`).then(r => r.permission).catch(() => 'none');
  if (!['admin', 'maintain'].includes(permission)) return reply(`\`/${command}\` は管理者 (admin / maintain) だけが使えます。`);
  const shape = pre ? /^\d+\.\d+\.\d+-(pre|rc)\.\d+$/ : /^\d+\.\d+\.\d+$/;
  if (!shape.test(version)) return reply(`版を ${pre ? '`1.2.3-pre.1`・`1.2.3-rc.1`' : '`1.2.3`'} の形で書いてください (例: \`/${command} ${pre ? '1.2.3-pre.1' : '1.2.3'}\`)。`);
  await react('eyes');
  const read = async file => Buffer.from((await gh('GET', `/contents/${file}?ref=main`)).content, 'base64').toString('utf8');
  const mismatches = [];
  const props = (await read('src/Directory.Build.props')).match(/<Version>([^<]+)<\/Version>/)?.[1];
  if (props !== version) mismatches.push(`src/Directory.Build.props: ${props ?? '(版なし)'}`);
  const plain = version.replace(/-.*$/, '');
  const plist = await read('mac/Resources/Info.plist');
  const plistVersion = plist.match(/<key>CFBundleShortVersionString<\/key>\s*<string>([^<]+)<\/string>/)?.[1];
  if (plistVersion !== plain) mismatches.push(`mac/Resources/Info.plist: ${plistVersion ?? '(版なし)'} (期待 ${plain})`);
  // リリースノートは配布物に同梱し、更新したあとの初めての起動で見せるので、先に main に入れておく
  const notesFile = `docs/release-notes/${version}.md`;
  const notes = await read(notesFile).catch(() => null);
  if (!notes?.trim()) mismatches.push(`${notesFile}: (無い。リリースノートを書いて main に入れてください)`);
  if (mismatches.length > 0) return reply(`❌ main で ${version} のリリースの準備 (版・リリースノート) ができていないので、リリースしません (docs/dev/RELEASE.md の「版を上げる」を先に main に入れてください):\n\n${mismatches.map(m => `- ${m}`).join('\n')}`);
  const tag = `v${version}`;
  if (await gh('GET', `/git/ref/tags/${tag}`).then(() => true).catch(() => false)) return reply(`❌ タグ \`${tag}\` はもうあります。`);
  const sha = (await gh('GET', '/git/ref/heads/main')).object.sha;
  await gh('POST', '/git/refs', { ref: `refs/tags/${tag}`, sha }, dispatchToken);
  await gh('POST', '/releases', { tag_name: tag, name: `Meltype ${version}`, draft: true, prerelease: pre, body: notes });
  const failed = await gh('POST', '/actions/workflows/release.yml/dispatches', { ref: tag }, dispatchToken)
    .then(() => null, error => error.message.slice(0, 120));
  await reply([
    `✅ タグ \`${tag}\` (${sha.slice(0, 7)}) とリリースの下書き (${pre ? 'プレリリース' : '通常'}、本文は \`${notesFile}\`) を作りました。`,
    failed === null
      ? '\nビルド (release.yml) を始めました。終わると下書きに配布物を付けて公開します。'
      : `\n⚠ ビルド (release.yml) を始められませんでした (Actions の画面から ${tag} で手で実行してください): ${failed}`,
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
    if (event.action === 'opened') await gh('POST', `/issues/${issue}/labels`, { labels: [Untriaged] });

    const form = parseForm(event.issue.body);
    const misdetection = event.issue.labels.some(l => l.name === '誤判定') || '出たもの' in form;
    const keys = field(form, '打ったもの');
    if (misdetection && isKeys(keys)) {
      return output({
        action: 'repro', issue, repros: JSON.stringify([{ keys: keys.slice(0, MaxKeys), last: lastKey(field(form, '最後に押したキー')) }]),
        reported: field(form, '出たもの'), expected: field(form, '期待した結果'),
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
  const matched = lines.map(l => l.match(/^\/(self-assign|repro|explain|ca|jht|test-jht|test-explain|test|pack|pre-release|release|help|learn)(?=\s|$)\s*(.*)$/))
    .filter((m, i) => m && (i === 0 || !['release', 'pre-release'].includes(m[1])));
  if (matched.length === 0 || event.comment.user?.type === 'Bot') return output({ action: 'none' });
  // /learn は、同じコメントの /repro・/ca の前に学習させる手順 (学習した後で起きる誤変換を再現する)
  const lessons = matched.filter(([, c]) => c === 'learn').map(([, , r]) => learnRequest(r));
  const commands = matched.filter(([, c]) => c !== 'learn');
  if (lessons.length > 0) {
    const fail = text => upsertComment(issue, `error-${event.comment.id}`, `${text}\n\n${Help}`);
    if (!['repro', 'ca'].includes(commands[0]?.[1])) {
      await fail('`/learn` は、`/repro` か `/ca` と同じコメントに書いてください。');
      return output({ action: 'none' });
    }
    if (lessons.length > MaxLessons) {
      await fail(`\`/learn\` は 1 つのコメントに ${MaxLessons} 個までです。`);
      return output({ action: 'none' });
    }
    if (!lessons.every(l => isKeys(l.keys) && l.text.length <= MaxLessonText)) {
      await fail(`\`/learn\` には、打ったキーを英字で書き、確定した語は ${MaxLessonText} 文字までにしてください (例: \`/learn niti → （日）\`)。`);
      return output({ action: 'none' });
    }
  }
  if (commands.length > 1 && !commands.every(([, c]) => c === 'repro')) {
    await upsertComment(issue, `error-${event.comment.id}`, `1 つのコメントに書けるコマンドは 1 つです (\`/repro\` だけは 1 行に 1 つずつ ${MaxRepros} 個まで書けます)。`);
    return output({ action: 'none' });
  }
  if (commands.length > MaxRepros) {
    await upsertComment(issue, `error-${event.comment.id}`, `\`/repro\` は 1 つのコメントに ${MaxRepros} 個までです。`);
    return output({ action: 'none' });
  }
  const [, command, rest] = commands[0];
  const prOnly = ['test', 'pack', 'test-jht', 'test-explain'];

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
  // PR のコードを動かすのは、メンテナーが確かめた commit だけ。コメントの後に head を差し替えられても、見ていないコードは動かさない。
  // 40 桁の完全一致だけを受け付ける (短い前方一致だと、先頭が同じ無害な commit と悪意のある commit を作って差し替えられる)
  // 通れば PR の head を返し、通らなければ理由をコメントして null を返す
  const verifiedHead = async (sha, usage) => {
    const fail = async text => { await upsertComment(issue, `error-${event.comment.id}`, text); return null; };
    const name = command === 'repro' ? '/repro <SHA>' : `/${command}`;
    if (!isPr) return fail(`\`${name}\` は Pull Request でだけ使えます。`);
    if (!maintainer) return fail(`\`${name}\` はメンテナーだけが使えます (Pull Request のコードを動かすため)。`);
    const head = (await gh('GET', `/pulls/${issue}`)).head.sha;
    if (sha === head) return head;
    const why = isSha(sha) ? `\`${sha}\` は PR の今の head と違います。` : `\`/${command}\` には、中身を確かめた commit の SHA (40 桁) を付けてください。`;
    return fail(`${why}今の head は \`${head}\` です。確かめてから \`/${command} ${head}${usage}\` と書いてください。`);
  };

  if (prOnly.includes(command)) {
    const given = rest.trim().split(/\s+/)[0];
    const args = rest.trim().slice(given.length).trim();
    const head = await verifiedHead(unquote(given).toLowerCase(), { 'test-jht': ' <文>', 'test-explain': ' <キー>' }[command] ?? '');
    if (!head) return output({ action: 'none' });
    await react('eyes');
    if (command === 'test' || command === 'pack') {
      return output({ action: command, issue, sha: head, comment: event.comment.id });
    }
    if (command === 'test-jht') {
      const text = args.replace(/^`|`$/g, '');
      if (!text) {
        await upsertComment(issue, `error-${event.comment.id}`, `出てほしい文を書いてください (例: \`/test-jht ${head} 私はgoogleが好きです\` または \`/test-jht ${head} 私はgoogleが好きです / わたしはgoogleがすきです\`)。`);
        return output({ action: 'none' });
      }
      return output({ action: 'test-jht', issue, keys: text.slice(0, 500), sha: head, comment: event.comment.id });
    }
    const keys = keysOf(args);
    if (!isKeys(keys)) {
      await upsertComment(issue, `error-${event.comment.id}`, `打ったキーを英字で書いてください (例: \`/${command} ${head} nihongowohanasu\`)。`);
      return output({ action: 'none' });
    }
    return output({ action: command, issue, keys, sha: head, comment: event.comment.id });
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
  // /repro <SHA> は、PR・PR の前 (main との merge base)・main の commit で打って比べる。どの行にも同じ SHA を書く
  const shas = new Set(requests.map(r => r.sha));
  if (shas.size > 1) {
    await upsertComment(issue, `error-${event.comment.id}`, '`/repro` を複数行書くときは、どの行にも同じ SHA を付けるか、どの行にも付けないでください。');
    return output({ action: 'none' });
  }
  const [sha] = shas;
  let versions = '';
  if (sha) {
    const head = await verifiedHead(sha, ' <キー>');
    if (!head) return output({ action: 'none' });
    const main = (await gh('GET', '/git/ref/heads/main')).object.sha;
    const base = (await gh('GET', `/compare/${main}...${head}`)).merge_base_commit.sha;
    versions = JSON.stringify([{ name: 'pr', sha: head }, { name: 'base', sha: base }, { name: 'main', sha: main }]);
  }
  await react('eyes');
  if (command === 'explain') return output({ action: command, issue, keys: requests[0].keys, comment: event.comment.id });
  // 手順は Repro.cs の --learn に「打ったキー → 確定した語」で渡す
  const learn = lessons.map(l => l.text ? `${l.keys} → ${l.text}` : l.keys);
  return output({ action: command, issue, repros: JSON.stringify(requests.map(({ keys, last }) => ({ keys, last, learn }))), versions, reported: '', expected: '', comment: event.comment.id });
}

const unquote = text => text.replace(/^`|`$/g, '');
const isSha = text => /^[0-9a-f]{40}$/.test(text);
const keysOf = text => unquote(text.trim().split(/\s+/).join(' ')).slice(0, MaxKeys);

// /learn の「<打ったキー> [→ <確定した語>]」。→ の代わりに前後に空白のある -> も使える (キーの - と > を分けるため)
function learnRequest(text) {
  const [keys, word = ''] = text.split(/\s*→\s*|\s+->\s+/, 2);
  return { keys: keysOf(keys), text: unquote(word.trim()) };
}

// /repro の「[<SHA>] <打ったキー> [space|enter|none]」
function reproRequest(text) {
  const words = text.trim().split(/\s+/);
  const sha = words.length > 1 && isSha(unquote(words[0]).toLowerCase()) ? unquote(words.shift()).toLowerCase() : undefined;
  const last = ['space', 'enter', 'none'].includes(words.at(-1)) && words.length > 1 ? words.pop() : 'enter';
  return { sha, keys: keysOf(words.join(' ')), last };
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

  // repro・ca の結果は results/<repros の番号>/repro-<版>.json。/repro <SHA> の版は BOT_VERSIONS の name で、その順に並べる
  const repros = JSON.parse(process.env.BOT_REPROS || '[]');
  const versions = JSON.parse(process.env.BOT_VERSIONS || '[]');
  const order = name => versions.findIndex(v => v.name === name);
  const reproResults = i => {
    const sub = path.join(dir, String(i));
    const files = fs.existsSync(sub) ? fs.readdirSync(sub).filter(f => f.startsWith('repro-')).sort() : [];
    return files.map(file => ({ name: file.replace(/^repro-/, '').replace(/\.json$/, ''), r: readJson(path.join(sub, file)) }))
      .sort((a, b) => order(a.name) - order(b.name));
  };
  // /repro <SHA> は同じ commit の版を 1 度だけ打つので、その commit の版の名前を = でつなぐ (例: PR の前 = main)
  const VersionLabels = { pr: 'PR', base: 'PR の前', main: 'main' };
  const versionLabel = name => {
    const sha = versions.find(v => v.name === name)?.sha;
    if (!sha) return name;
    return `${versions.filter(v => v.sha === sha).map(v => VersionLabels[v.name]).join(' = ')} (${sha.slice(0, 7)})`;
  };
  // repro の結果は Mozc だけのもの (一番上) と、Zenzai も使ったもの (zenzai。Zenzai を使えなかったら zenzai.error) の 2 つ。
  // zenzai が無いのは Zenzai を試さない版 (Repro.cs が zenzai を出す前の版) で、表には — を出す
  const typedOf = r => (r.committed ?? '') + (r.composing ?? '');
  const outcomeCell = r => {
    if (!r) return '—';
    if (r.error) return `⚠️ ${md(r.error)}`;
    const typed = typedOf(r);
    return cell(typed) + (r.converted && r.converted !== typed ? ` / Space: ${cell(r.converted)}` : '');
  };
  // /learn の手順。learned (手順ごとに確定したもの) が無いのは、学習に対応する前の版 (結果は学習していない)
  const lessonOf = text => { const [keys, word] = text.split(' → '); return { keys, word }; };
  const unlearned = (learn, r) => learn?.length > 0 && r && !r.error && !Array.isArray(r.learned);
  const learnNotes = (learn, results) => {
    if (!(learn?.length > 0)) return [];
    const lines = [`先に別の入力欄で打って確定し、学習させました: ${learn.map(lessonOf).map(l => code(l.keys) + (l.word ? ` → ${cell(l.word)}` : '')).join('、')}`];
    for (const { name, r } of results) {
      if (unlearned(learn, r)) { lines.push(`ℹ️ ${md(versionLabel(name))} は学習に対応していない版なので、学習させずに打ちました。`); continue; }
      learn.map(lessonOf).forEach((l, i) => {
        const committed = r?.learned?.[i];
        if (l.word && committed != null && committed !== l.word) lines.push(`⚠️ ${md(versionLabel(name))}: ${code(l.keys)} の候補に ${cell(l.word)} が無く、${cell(committed)} と確定しました。`);
      });
    }
    return [...lines, ''];
  };
  const usable = o => o && !o.error;
  const outcomes = r => [r, r.zenzai].filter(usable);
  // Zenzai の結果は、どちらの版でも使えたときだけ比べる
  const sameOutcome = (a, b) => outcomeCell(a) === outcomeCell(b)
    && (!usable(a.zenzai) || !usable(b.zenzai) || outcomeCell(a.zenzai) === outcomeCell(b.zenzai));

  if (action === 'repro') {
    const marker = process.env.BOT_COMMENT_ID ? `repro-${process.env.BOT_COMMENT_ID}` : 'repro';
    const reported = process.env.BOT_REPORTED, expected = process.env.BOT_EXPECTED;
    const runs = repros.map((r, i) => ({ ...r, results: reproResults(i) }));
    if (runs.every(r => r.results.length === 0)) {
      await upsertComment(issue, marker, `再現を試せませんでした ([実行結果](${runUrl}))。`);
      return;
    }
    const sections = [], verdicts = [];
    let engine = '';
    for (const { keys, last, learn, results } of runs) {
      if (results.length === 0) {
        sections.push(`${code(keys)} は再現を試せませんでした。`);
        continue;
      }
      const withZenzai = results.some(v => v.r?.zenzai);
      const rows = [];
      let verdict;
      for (const { name, r } of results) {
        if (!r) { rows.push(`| ${md(versionLabel(name))} | (失敗) |${withZenzai ? ' (失敗) |' : ''}`); continue; }
        if (r.engine) engine = r.engine;
        rows.push(`| ${md(versionLabel(name))} | ${outcomeCell(r)} |${withZenzai ? ` ${outcomeCell(r.zenzai)} |` : ''}`);
        if ((name === 'main' || name === 'pr') && !verdict && !unlearned(learn, r)) {
          // Mozc だけか Mozc + Zenzai のどちらかで報告と同じなら、もう一方が期待どおりでも再現とする
          // (Zenzai が遅い PC では Mozc の結果になる。アプリは Zenzai が既定で ON)
          const texts = outcomes(r).flatMap(o => [typedOf(o), o.converted]);
          if (reported && texts.includes(reported)) verdict = 'reproduced';
          else if (expected && texts.includes(expected)) verdict = 'match';
          else verdict = 'different';
        }
      }
      verdicts.push(verdict);
      const pr = results.find(v => v.name === 'pr')?.r, base = results.find(v => v.name === 'base')?.r;
      const change = pr && base ? (sameOutcome(pr, base) ? 'PR の前と同じ結果です。' : 'PR で結果が変わりました。') : '';
      sections.push([
        ...learnNotes(learn, results),
        `${code(keys)} を打ってみました (最後のキー: ${last})。`,
        '',
        withZenzai ? '| 版 | Mozc | Mozc + Zenzai |' : '| 版 | 結果 |',
        withZenzai ? '|---|---|---|' : '|---|---|',
        ...rows,
        ...(change ? ['', change] : []),
      ].join('\n'));
    }
    const hasMozc = /Mozc/i.test(engine);
    const lines = [sections.join('\n\n'), ''];
    if (!hasMozc) lines.push('ℹ️ Mozc の変換ヘルパーが使えなかったため、**漢字変換は検証していません** (日本語 / 英語の判定と入力結果のみ)。');
    else if (verdicts.includes('reproduced')) lines.push('🔁 報告と同じ結果になりました。**再現しました。**');
    else if (verdicts.includes('match')) lines.push('✅ 最新のコードでは期待どおりでした。次の版で直っているかもしれません。');
    else if (expected || reported) lines.push('報告とは違う結果になりました。アプリ・前後の文・学習で変わることがあります (学習は `/learn` で先に学習させてから試せます)。');
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
      ...learnNotes(repros[0]?.learn, results),
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
    const ok = process.env.BOT_JOB_RESULT === 'success';
    // 失敗したら、出力の末尾を見せる (失敗したテストと集計。ビルドで止まったときは test.txt が無いのでビルドの出力)。
    // どちらも無ければ、キャンセル・タイムアウトや Mozc の取得の失敗などで、ビルドの前に止まっている
    const log = ok ? null : readText(path.join(dir, 'test.txt'), 3000) ?? readText(path.join(dir, 'build.txt'), 3000);
    const status = ok ? '✅ テストが通りました' : log ? '❌ テストが失敗しました' : '⚠️ テストを実行できませんでした';
    await upsertComment(issue, `test-${process.env.BOT_COMMENT_ID}`, [
      `${status} (${process.env.BOT_SHA?.slice(0, 7)})`,
      log ? `\n${fence(log.trim())}` : '',
      `\n[実行結果](${runUrl})`,
    ].join('\n'));
    return;
  }

  if (action === 'pack') {
    const ok = process.env.BOT_JOB_RESULT === 'success';
    await upsertComment(issue, `pack-${process.env.BOT_COMMENT_ID}`, ok
      ? `📦 テスト版のインストーラー (setup.exe) を作りました (${process.env.BOT_SHA?.slice(0, 7)})。[実行結果](${runUrl}) の Artifacts の **meltype-pr-${issue}** からダウンロードできます (Windows 用)。`
      : `❌ インストーラーを作れませんでした。[実行結果](${runUrl})`);
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
