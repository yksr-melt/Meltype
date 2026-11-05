// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// README の「寄付してくださった方々」を、GitHub Sponsors の一覧から作り直す (.github/workflows/sponsors.yml から呼ぶ)。
//   - 名前を公開にしてくださった方 (privacyLevel が PUBLIC) だけを載せる。非公開の方は載せない
//   - 今は応援をやめた方も、一度応援してくださった方として載せる (古い順)
//   - README の <!-- sponsors:start --> と <!-- sponsors:end --> の間を書き換える
// bun bots/sponsors/main.ts            … README.md を書き換える (変わらなければそのまま)
// bun bots/sponsors/main.ts --self-test … 書き換えの形だけを確かめる (GitHub には問い合わせない)
import fs from 'node:fs';
import { createGitHub } from '../lib/github-api.ts';

const START = '<!-- sponsors:start -->';
const END = '<!-- sponsors:end -->';
const LOGIN = process.env.SPONSORS_LOGIN ?? 'yksr-melt';
const SPONSOR_URL = `https://github.com/sponsors/${LOGIN}`;

/** <!-- sponsors:start --> と <!-- sponsors:end --> の間に入れる HTML。 */
function render(logins: string[]) {
  if (logins.length === 0) {
    return `<p align="center">\n  <sub>まだいません。最初の 1 人を募集中です！ → <a href="${SPONSOR_URL}">GitHub Sponsors</a></sub>\n</p>`;
  }
  const icons = logins.map(login =>
    `  <a href="https://github.com/${login}"><img src="https://github.com/${login}.png?size=120" width="60" alt="@${login}" title="@${login}"></a>`);
  return `<p align="center">\n${icons.join('\n')}\n</p>`;
}

/** README の印の間を入れ替えたもの。印が無ければ例外。 */
function replace(readme: string, html: string) {
  const start = readme.indexOf(START);
  const end = readme.indexOf(END);
  if (start < 0 || end < start) throw new Error(`README に ${START} と ${END} がありません`);
  return readme.slice(0, start + START.length) + '\n' + html + '\n' + readme.slice(end);
}

function selfTest() {
  const readme = `前\n${START}\n古い\n${END}\n後\n`;
  const checks: [string, boolean][] = [
    ['誰もいない', replace(readme, render([])).includes('まだいません')],
    ['2 人', (() => { const r = replace(readme, render(['alice', 'bob'])); return r.includes('github.com/alice.png') && r.includes('@bob') && !r.includes('古い'); })()],
    ['印の外は変えない', replace(readme, render(['alice'])).startsWith('前\n') && replace(readme, render(['alice'])).endsWith(`${END}\n後\n`)],
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
  const logins = await createGitHub().listPublicSponsors(LOGIN);
  console.log(`名前を公開している方: ${logins.length} 人${logins.length ? ` (${logins.join(', ')})` : ''}`);
  const readme = fs.readFileSync('README.md', 'utf8');
  const next = replace(readme, render(logins));
  if (next === readme) console.log('README は変わりません');
  else fs.writeFileSync('README.md', next);
}

if (process.argv[2] === '--self-test') selfTest();
else await main();
