// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// CodeQL の指摘を日本語でまとめる bot (.github/workflows/code-scanning.yml から呼ぶ)。
//   CodeQL (codeql.yml) が Pull Request で終わったら、その Pull Request で新しく出た指摘
//   (base のブランチに無い指摘) を、日本語の表にして Pull Request にコメントする。
//   GitHub の bot (github-advanced-security) の英語のコメントはそのまま残る。これは読みやすくするためのまとめ。
//   指摘が無くなったら、コメントを「新しい指摘はありません」に書き換える (前にコメントしていなければ何もしない)。
// Pull Request のコードは動かさない。GitHub の API で、Code scanning の結果を読むだけ。
import fs from 'node:fs';

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;

const Marker = '<!-- meltype-bot --><!-- code-scanning -->';
const Tool = 'CodeQL';

/** よく出るルールの日本語の説明。無いルールは CodeQL の英語の説明をそのまま出す。 */
const Rules = {
  'js/incomplete-sanitization': '文字列のエスケープが足りません (一部の文字しか置き換えていない、またはバックスラッシュを置き換えていない)',
  'js/incomplete-multi-character-sanitization': '複数文字のパターンを 1 回だけ取り除いているため、取り除いた後に同じパターンが残ることがあります',
  'js/regex/missing-regexp-anchor': '正規表現に ^ や $ が無いため、URL などの一部だけに一致して、確かめたつもりの値をすり抜けられます',
  'js/incomplete-url-substring-sanitization': 'URL を文字列の一部の一致だけで確かめているため、別のホストの URL を通してしまいます',
  'js/polynomial-redos': '正規表現が、入力によって極端に遅くなります (ReDoS)',
  'js/redos': '正規表現が、入力によって極端に遅くなります (ReDoS)',
  'js/xss': '利用者の入力を、エスケープせずに HTML に埋め込んでいます (XSS)',
  'js/xss-through-dom': 'DOM から読んだ値を、エスケープせずに HTML に埋め込んでいます (XSS)',
  'js/reflected-xss': '受け取った値を、エスケープせずにそのまま返しています (XSS)',
  'js/sql-injection': '利用者の入力から SQL を組み立てています (SQL インジェクション)。プレースホルダーを使ってください',
  'js/command-line-injection': '利用者の入力から、シェルのコマンドを組み立てています',
  'js/shell-command-injection-from-environment': '環境の値 (パスなど) から、シェルのコマンドを組み立てています',
  'js/path-injection': '利用者の入力から、ファイルのパスを組み立てています (想定外のファイルを読み書きされます)',
  'js/prototype-pollution-utility': 'オブジェクトを再帰的にまとめる処理で、__proto__ などを書き換えられます',
  'js/request-forgery': '利用者の入力から、サーバーが接続する先の URL を組み立てています (SSRF)',
  'js/clear-text-logging': 'パスワードやトークンなどの秘密の値を、そのままログに出しています',
  'js/insecure-randomness': '安全さが必要な値に Math.random を使っています。crypto の乱数を使ってください',
  'js/unused-local-variable': '使っていない変数があります',
  'js/useless-assignment-to-local': '代入した値が使われていません',
  'js/trivial-conditional': '条件がいつも同じ結果になります',
  'js/missing-rate-limiting': '重い処理やログインの処理に、回数の制限がありません',
  'cs/path-injection': '利用者の入力から、ファイルのパスを組み立てています (想定外のファイルを読み書きされます)',
  'cs/command-line-injection': '利用者の入力から、実行するコマンドを組み立てています',
  'cs/zipslip': 'zip の中のファイル名をそのまま使って展開しています (../ で展開先の外に書き込まれます)',
  'cs/cleartext-storage-of-sensitive-information': '秘密の値を、暗号化せずに保存しています',
  'cs/exposure-of-sensitive-information': '秘密の値を、外に出しています',
  'cs/insecure-randomness': '安全さが必要な値に System.Random を使っています。RandomNumberGenerator を使ってください',
  'cs/useless-assignment-to-local': '代入した値が使われていません',
  'cs/catch-of-all-exceptions': 'すべての例外を catch しています。想定した例外だけを catch してください',
  'cs/dereferenced-value-may-be-null': 'null かもしれない値を、確かめずに使っています',
  'cs/index-out-of-bounds': '配列の範囲の外を読むことがあります',
  'cs/web/missing-token-validation': 'フォームの送信に、CSRF のトークンの確認がありません',
  'cpp/overflowing-snprintf': 'snprintf の戻り値をそのまま使っているため、バッファーの外を指すことがあります',
  'cpp/unbounded-write': 'バッファーの大きさを確かめずに書き込んでいます (バッファーオーバーフロー)',
  'cpp/overrunning-write': 'バッファーの大きさを超えて書き込みます (バッファーオーバーフロー)',
  'cpp/static-buffer-overflow': '固定の大きさのバッファーの外に書き込みます',
  'cpp/uncontrolled-allocation-size': '入力から決めた大きさでメモリーを確保しています',
  'cpp/use-after-free': '解放したメモリーを使っています',
  'cpp/double-free': '同じメモリーを 2 回解放しています',
  'cpp/missing-null-test': 'null かもしれないポインターを、確かめずに使っています',
  'cpp/integer-multiplication-cast-to-long': '掛け算が大きい型に変える前に溢れることがあります',
  'cpp/comparison-with-wider-type': 'ループの変数の型が狭く、条件の値まで届かずに無限ループになることがあります',
  'py/path-injection': '利用者の入力から、ファイルのパスを組み立てています (想定外のファイルを読み書きされます)',
  'py/command-line-injection': '利用者の入力から、実行するコマンドを組み立てています',
  'py/shell-command-constructed-from-input': '入力から、シェルのコマンドを組み立てています',
  'py/clear-text-logging-sensitive-data': '秘密の値を、そのままログに出しています',
  'py/unused-import': '使っていない import があります',
  'py/unused-local-variable': '使っていない変数があります',
  'actions/untrusted-checkout/critical': '権限の強いワークフローで、Pull Request のコードをチェックアウトして動かしています',
  'actions/untrusted-checkout/high': '権限の強いワークフローで、Pull Request のコードをチェックアウトしています',
  'actions/untrusted-checkout/medium': '権限の強いワークフローで、Pull Request のコードをチェックアウトしています',
  'actions/code-injection/critical': '外から書ける値 (Issue の本文・ブランチ名など) を、run: の中に ${{ }} で埋め込んでいます。環境変数で渡してください',
  'actions/code-injection/medium': '外から書ける値 (Issue の本文・ブランチ名など) を、run: の中に ${{ }} で埋め込んでいます。環境変数で渡してください',
  'actions/missing-workflow-permissions': 'ワークフローに permissions がありません。必要な権限だけを書いてください',
  'actions/unpinned-tag': 'アクションを版のタグで指定しています。commit の SHA で固定してください',
  'actions/artifact-poisoning/critical': 'Pull Request が作った実行結果 (Artifacts) を、権限の強いワークフローで確かめずに使っています',
  'actions/artifact-poisoning/medium': 'Pull Request が作った実行結果 (Artifacts) を、確かめずに使っています',
  'actions/cache-poisoning/direct-cache': 'Pull Request のコードから、ほかのワークフローが使うキャッシュを書き換えられます',
};

/** 重要度。security_severity_level (セキュリティのルール) が無ければ、ルールの severity を使う。 */
const Severities = {
  critical: ['🔴', '重大'],
  high: ['🟠', '高'],
  medium: ['🟡', '中'],
  low: ['⚪', '低'],
  error: ['🟠', 'エラー'],
  warning: ['🟡', '警告'],
  note: ['⚪', '参考'],
};
const Order = Object.keys(Severities);

async function gh(method, url, body) {
  const response = await fetch(url.startsWith('http') ? url : `${api}/repos/${repo}${url}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-bot' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`${method} ${url}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

/** ref の開いている指摘 (CodeQL のものだけ)。 */
async function alerts(ref) {
  const list = [];
  for (let page = 1; page <= 10; page++) {
    const batch = await gh('GET', `/code-scanning/alerts?tool_name=${Tool}&state=open&ref=${encodeURIComponent(ref)}&per_page=100&page=${page}`) ?? [];
    list.push(...batch);
    if (batch.length < 100) break;
  }
  return list;
}

/** base に無い指摘 (Pull Request で新しく出たもの)。番号は ref をまたいで同じ指摘なら同じ。 */
function newAlerts(prAlerts, baseAlerts) {
  const base = new Set(baseAlerts.map(a => a.number));
  return prAlerts.filter(a => !base.has(a.number));
}

const severityOf = alert => alert.rule?.security_severity_level || alert.rule?.severity || 'warning';

/** 表のセル。パス・メッセージは PR の作者が書けるので、表と Markdown を崩さないようにする。 */
const cell = text => String(text ?? '').replace(/\r?\n|\r/g, ' ').replace(/[\\|`*_<>[\]]/g, '\\$&').replace(/@/g, '@\u200b');

function message(list) {
  const sorted = [...list].sort((a, b) => Order.indexOf(severityOf(a)) - Order.indexOf(severityOf(b)));
  const rows = sorted.map(a => {
    const [icon, label] = Severities[severityOf(a)] ?? ['⚪', severityOf(a)];
    const where = a.most_recent_instance?.location ?? {};
    const place = `${cell(where.path)}:${where.start_line ?? '?'}`;
    const ja = Rules[a.rule?.id];
    const text = ja ? cell(ja) : `${cell(a.rule?.description)} (日本語の説明はまだありません)`;
    return `| ${icon} ${label} | ${place} | ${text} | [#${a.number}](${a.html_url}) |`;
  });
  return [
    `### 🔍 CodeQL の新しい指摘 (${list.length} 件)`,
    '',
    'この Pull Request で新しく見つかった指摘を、日本語でまとめました (行ごとの英語のコメントは GitHub の bot が付けたものです)。',
    '',
    '| 重要度 | 場所 | 内容 | 詳しく |',
    '|---|---|---|---|',
    ...rows,
    '',
    '「詳しく」から、英語の説明と直し方の例が見られます。直して push すると、このコメントは自動で書き換わります。',
    '誤検出なら、メンテナーが「詳しく」のページで Dismiss できます。',
  ].join('\n');
}

async function upsert(number, body) {
  const comments = await gh('GET', `/issues/${number}/comments?per_page=100`) ?? [];
  const mine = comments.find(c => c.user?.type === 'Bot' && c.body?.includes(Marker));
  if (!body) {
    if (mine && !mine.body.includes('✅')) await gh('PATCH', `/issues/comments/${mine.id}`, { body: `${Marker}\n✅ CodeQL の新しい指摘はありません。` });
    return;
  }
  if (mine) await gh('PATCH', `/issues/comments/${mine.id}`, { body: `${Marker}\n${body}` });
  else await gh('POST', `/issues/${number}/comments`, { body: `${Marker}\n${body}` });
}

/** 終わった CodeQL の実行の Pull Request。フォークからの Pull Request は workflow_run に番号が入らないので、head の SHA で探す。 */
async function pullRequestOf(run) {
  const number = run.pull_requests?.[0]?.number;
  if (number) return gh('GET', `/pulls/${number}`);
  for (let page = 1; page <= 5; page++) {
    const batch = await gh('GET', `/pulls?state=open&per_page=100&page=${page}`) ?? [];
    const found = batch.find(p => p.head.sha === run.head_sha);
    if (found) return found;
    if (batch.length < 100) break;
  }
  return null;
}

async function main() {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const run = event.workflow_run;
  const pr = await pullRequestOf(run);
  if (!pr || pr.state !== 'open') return console.log('開いている Pull Request が見つかりません');
  // 古い commit の結果なら書かない (新しい commit の CodeQL が後で終わり、そのときに書く)
  if (pr.head.sha !== run.head_sha) return console.log(`head が変わっています (${run.head_sha} → ${pr.head.sha})`);
  const found = newAlerts(await alerts(`refs/pull/${pr.number}/merge`), await alerts(`refs/heads/${pr.base.ref}`));
  for (const a of found) console.log(`#${a.number} ${severityOf(a)} ${a.rule?.id} ${a.most_recent_instance?.location?.path}:${a.most_recent_instance?.location?.start_line}`);
  await upsert(pr.number, found.length ? message(found) : null);
}

/** node .github/bot/code-scanning.mjs --self-test: まとめ方だけを確かめる (GitHub には何も書かない)。 */
function selfTest() {
  const alert = (number, id, severity, security, path = 'tools/a.mjs') => ({
    number, html_url: `https://example.com/${number}`,
    rule: { id, severity, security_severity_level: security, description: 'English description' },
    most_recent_instance: { location: { path, start_line: 3 } },
  });
  let failed = 0;
  const check = (name, ok) => { if (!ok) failed++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`); };
  const pr = [alert(1, 'js/incomplete-sanitization', 'warning', 'high'), alert(2, 'cs/unknown-rule', 'note', null), alert(3, 'py/unused-import', 'note', null)];
  const found = newAlerts(pr, [alert(3, 'py/unused-import', 'note', null)]);
  check('base にある指摘は出さない', found.map(a => a.number).join() === '1,2');
  const text = message(found);
  check('日本語の説明がある指摘は日本語で出す', text.includes('文字列のエスケープが足りません'));
  check('日本語の説明が無い指摘は英語の説明を出す', text.includes('English description (日本語の説明はまだありません)'));
  check('重要度の高い順に並べる', text.indexOf('🟠 高') < text.indexOf('⚪ 参考'));
  check('パスの記号で表が崩れない', message([alert(4, 'x', 'note', null, 'a|b`c.mjs')]).includes('a\\|b\\`c.mjs'));
  check('説明が足りないルールが無い', Object.values(Rules).every(v => v.length > 0));
  if (failed) process.exit(1);
}

if (process.argv[2] === '--self-test') selfTest();
else await main();
