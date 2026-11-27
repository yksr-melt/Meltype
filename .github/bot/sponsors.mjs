// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
//
// README の「寄付してくださった方々」を、GitHub Sponsors の一覧から作り直す (.github/workflows/sponsors.yml から呼ぶ)。
//   - 名前を公開にしてくださった方 (privacyLevel が PUBLIC) だけを載せる。非公開の方は載せない
//   - 今は応援をやめた方も、一度応援してくださった方として載せる (古い順)
//   - README の <!-- sponsors:start --> と <!-- sponsors:end --> の間を書き換える
// node .github/bot/sponsors.mjs            … README.md を書き換える (変わらなければそのまま)
// node .github/bot/sponsors.mjs --self-test … 書き換えの形だけを確かめる (GitHub には問い合わせない)
import fs from 'node:fs';

const Start = '<!-- sponsors:start -->';
const End = '<!-- sponsors:end -->';
const Login = process.env.SPONSORS_LOGIN ?? 'yksr-melt';
const SponsorUrl = `https://github.com/sponsors/${Login}`;

async function graphql(query, variables) {
  const response = await fetch(`${process.env.GITHUB_GRAPHQL_URL ?? 'https://api.github.com/graphql'}`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${process.env.GITHUB_TOKEN}`, 'Content-Type': 'application/json', 'User-Agent': 'meltype-bot' },
    body: JSON.stringify({ query, variables }),
  });
  const result = await response.json();
  if (!response.ok || result.errors) throw new Error(`GraphQL: ${response.status} ${JSON.stringify(result.errors ?? result)}`);
  return result.data;
}

/** 名前を公開にしてくださった方 (古い順。同じ方は 1 回だけ)。 */
async function publicSponsors() {
  const query = `query($login: String!, $after: String) {
    user(login: $login) {
      sponsorshipsAsMaintainer(first: 100, after: $after, activeOnly: false, includePrivate: false, orderBy: {field: CREATED_AT, direction: ASC}) {
        pageInfo { hasNextPage endCursor }
        nodes { privacyLevel sponsorEntity { ... on User { login } ... on Organization { login } } }
      }
    }
  }`;
  const logins = [];
  let after = null;
  for (let page = 0; page < 20; page++) {
    const data = await graphql(query, { login: Login, after });
    const sponsorships = data.user?.sponsorshipsAsMaintainer;
    if (!sponsorships) break;
    for (const node of sponsorships.nodes) {
      const login = node.sponsorEntity?.login;
      if (node.privacyLevel === 'PUBLIC' && login && !logins.includes(login)) logins.push(login);
    }
    if (!sponsorships.pageInfo.hasNextPage) break;
    after = sponsorships.pageInfo.endCursor;
  }
  return logins;
}

/** <!-- sponsors:start --> と <!-- sponsors:end --> の間に入れる HTML。 */
function render(logins) {
  if (logins.length === 0) {
    return `<p align="center">\n  <sub>まだいません。最初の 1 人を募集中です！ → <a href="${SponsorUrl}">GitHub Sponsors</a></sub>\n</p>`;
  }
  const icons = logins.map(login =>
    `  <a href="https://github.com/${login}"><img src="https://github.com/${login}.png?size=120" width="60" alt="@${login}" title="@${login}"></a>`);
  return `<p align="center">\n${icons.join('\n')}\n</p>`;
}

/** README の印の間を入れ替えたもの。印が無ければ例外。 */
function replace(readme, html) {
  const start = readme.indexOf(Start);
  const end = readme.indexOf(End);
  if (start < 0 || end < start) throw new Error(`README に ${Start} と ${End} がありません`);
  return readme.slice(0, start + Start.length) + '\n' + html + '\n' + readme.slice(end);
}

function selfTest() {
  const readme = `前\n${Start}\n古い\n${End}\n後\n`;
  const checks = [
    ['誰もいない', replace(readme, render([])).includes('まだいません')],
    ['2 人', (() => { const r = replace(readme, render(['alice', 'bob'])); return r.includes('github.com/alice.png') && r.includes('@bob') && !r.includes('古い'); })()],
    ['印の外は変えない', replace(readme, render(['alice'])).startsWith('前\n') && replace(readme, render(['alice'])).endsWith(`${End}\n後\n`)],
    ['2 回入れ替えても同じ', replace(replace(readme, render(['alice'])), render(['alice'])) === replace(readme, render(['alice']))],
  ];
  let failed = 0;
  for (const [name, ok] of checks) {
    if (!ok) failed++;
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`);
  }
  if (failed) process.exit(1);
}

async function main() {
  const logins = await publicSponsors();
  console.log(`名前を公開している方: ${logins.length} 人${logins.length ? ` (${logins.join(', ')})` : ''}`);
  const readme = fs.readFileSync('README.md', 'utf8');
  const next = replace(readme, render(logins));
  if (next === readme) console.log('README は変わりません');
  else fs.writeFileSync('README.md', next);
}

if (process.argv[2] === '--self-test') selfTest();
else await main();
