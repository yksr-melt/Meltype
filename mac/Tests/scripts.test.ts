// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
//
// mac/ のスクリプトのテスト。偽のコマンドを PATH の先頭に置いて動かし、インストール済みの入力メソッドには触らない。
//   node --test mac/Tests/scripts.test.ts

import assert from 'node:assert/strict';
import { spawn, spawnSync, type SpawnSyncReturns } from 'node:child_process';
import { once } from 'node:events';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { after, before, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';

const root = path.resolve(import.meta.dirname, '../..');
const lsregister = '/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister';

/** 本物の InstallSupport (カーネルの swap・lock)。before でビルドする。 */
let installTool = '';

type Result = { status: number; output: string };
type Work = string;

function makeWork(): Work {
  return fs.mkdtempSync('/tmp/meltype-script-tests-');
}

async function withWork(run: (work: Work) => void | Promise<void>): Promise<void> {
  const work = makeWork();
  try {
    await run(work);
  } finally {
    fs.rmSync(work, { recursive: true, force: true });
  }
}

/** 前のファイルの inode を使い回さない (macOS は実行ファイルの署名を inode ごとに控える)。 */
function writeFile(file: string, text: string): void {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.rmSync(file, { force: true });
  fs.writeFileSync(file, text);
}

function read(file: string): string {
  return fs.readFileSync(file, 'utf8');
}

function readLines(file: string): string[] {
  const text = read(file);
  return text === '' ? [] : text.replace(/\n$/, '').split('\n');
}

const calls = (work: Work): string[] => readLines(path.join(work, 'calls'));
const clearCalls = (work: Work): void => writeFile(path.join(work, 'calls'), '');

function fakeTool(work: Work, name: string, body: string): void {
  const file = path.join(work, name);
  writeFile(file, `#!/bin/bash\n${body}`);
  fs.chmodSync(file, 0o755);
}

function result(spawned: SpawnSyncReturns<string>): Result {
  if (spawned.error) throw spawned.error;
  const status = spawned.status ?? 128 + (spawned.signal ? os.constants.signals[spawned.signal] : 0);
  return { status, output: spawned.stdout + spawned.stderr };
}

/** コマンドを動かし、失敗しても結果を返す。 */
function attempt(command: string, ...args: string[]): Result {
  return result(spawnSync(command, args, { encoding: 'utf8' }));
}

function run(command: string, ...args: string[]): string {
  const { status, output } = attempt(command, ...args);
  assert.equal(status, 0, `${command} ${args.join(' ')}\n${output}`);
  return output;
}

function attemptScript(work: Work, name: string, ...args: string[]): Result {
  return result(spawnSync('bash', [path.join(work, name), ...args], {
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${work}/:${process.env.PATH}`,
      MELTYPE_TEST_LOG: path.join(work, 'calls'),
      MELTYPE_START_LOG_DIR: `${work}/`,
    },
  }));
}

function runScript(work: Work, name: string, ...args: string[]): string {
  const { status, output } = attemptScript(work, name, ...args);
  assert.equal(status, 0, output);
  return output;
}

const installAppArgs = (work: Work): string[] => [path.join(work, 'source.app'), path.join(work, 'installed.app')];
const attemptInstallApp = (work: Work): Result => attemptScript(work, 'install-app.sh', ...installAppArgs(work));

function installedVersion(work: Work): string {
  return read(path.join(work, 'installed.app/Contents/version'));
}

async function waitForFile(file: string, attempts: number, interval: number): Promise<void> {
  for (let i = 0; i < attempts && !fs.existsSync(file); i++) await delay(interval);
  assert.ok(fs.existsSync(file), `${file} が作られなかった`);
}

/** /usr/bin/true は arm64e で、署名し直すと Apple シリコンでは起動時に kill されるので、空の実行ファイルをビルドする。 */
function compileEmpty(work: Work, output: string, architectures: string[] = []): void {
  const source = path.join(work, 'empty.c');
  writeFile(source, 'int main(void) { return 0; }\n');
  fs.mkdirSync(path.dirname(output), { recursive: true });
  run('xcrun', 'clang', ...architectures.flatMap(a => ['-arch', a]), '-o', output, source);
}

const buildDirectory = makeWork();
before(() => {
  installTool = path.join(buildDirectory, 'MeltypeInstallSupport');
  run('xcrun', 'swiftc', '-swift-version', '5',
    path.join(root, 'mac/Sources/InstallSupport/main.swift'),
    path.join(root, 'mac/Sources/InstallSupport/InputMethodLifecycle.swift'),
    '-o', installTool);
});
after(() => fs.rmSync(buildDirectory, { recursive: true, force: true }));

/** 自分用の一時フォルダーで動かすテスト。 */
function scenario(name: string, body: (work: Work) => void | Promise<void>): void {
  test(name, () => withWork(body));
}

// ---- build-cli.sh

function prepareBuildCli(work: Work): void {
  // リポジトリと同じ配置にし、ヘルパーも各テストの一時ディレクトリに閉じ込める。
  writeFile(path.join(work, 'mac/build-cli.sh'), read(path.join(root, 'mac/build-cli.sh')));
  fakeTool(work, 'mac/build.sh', 'echo build:$* >> "$MELTYPE_TEST_LOG"');
  for (const name of ['dotnet', 'swift']) fakeTool(work, name, `echo ${name} >> "$MELTYPE_TEST_LOG"`);
}

scenario('build-cli: --build はビルドだけをする', work => {
  prepareBuildCli(work);
  fakeTool(work, 'native/mozc/bin/mac/meltype_mozc_helper', 'exit 0');
  runScript(work, 'mac/build-cli.sh', '--build');
  assert.deepEqual(calls(work), ['build:--no-install']);
});

scenario('build-cli: Mozc ヘルパーが無い・実行できないときは、テストやビルドの前に止める', work => {
  prepareBuildCli(work);
  const helper = path.join(work, 'native/mozc/bin/mac/meltype_mozc_helper');
  // 既定・--all・update.sh が使う組み合わせも、テストやビルドの実行前に止める。
  for (const args of [[], ['--build'], ['--install'], ['--all'], ['--test', '--build'], ['--test', '--install'], ['--setup', '--all']]) {
    for (const notExecutable of [false, true]) {
      if (notExecutable) writeFile(helper, 'not executable');
      else fs.rmSync(helper, { force: true });
      const { status, output } = attemptScript(work, 'mac/build-cli.sh', ...args);
      assert.equal(status, 1, output);
      assert.ok(output.includes('mac/README.md'), output);
      assert.ok(!fs.existsSync(path.join(work, 'calls')));
    }
  }
});

scenario('build-cli: --test・--help に Mozc ヘルパーは要らない', work => {
  prepareBuildCli(work);
  runScript(work, 'mac/build-cli.sh', '--test');
  assert.deepEqual(calls(work), ['dotnet', 'swift']);
  clearCalls(work);
  runScript(work, 'mac/build-cli.sh', '--help');
  assert.deepEqual(calls(work), []);
});

scenario('build-cli: Mozc ヘルパーがあれば、既定・--all・--install の振り分けは変わらない', work => {
  prepareBuildCli(work);
  fakeTool(work, 'native/mozc/bin/mac/meltype_mozc_helper', 'exit 0');
  for (const args of [[], ['--all'], ['--test', '--install'], ['--install']]) {
    clearCalls(work);
    runScript(work, 'mac/build-cli.sh', ...args);
    assert.deepEqual(calls(work), args.length === 1 && args[0] === '--install' ? ['build:'] : ['dotnet', 'swift', 'build:']);
  }
});

// ---- update.sh・start-input-method.sh

function update(work: Work): void {
  const source = read(path.join(root, 'mac/update.sh'));
  const original = '$HOME/Library/Input Methods/Meltype.app';
  assert.ok(source.includes(original));
  writeFile(path.join(work, 'update.sh'), source.replace(original, () => path.join(work, 'Meltype.app')));
  fakeTool(work, 'Meltype.app/Contents/MacOS/Meltype', 'echo "app:$*" >> "$MELTYPE_TEST_LOG"');
  fakeTool(work, 'build-cli.sh', 'echo "build:$*" >> "$MELTYPE_TEST_LOG"');
  fakeTool(work, 'uname', 'echo Darwin');
  fakeTool(work, 'start-input-method.sh', 'echo UNEXPECTED_START >> "$MELTYPE_TEST_LOG"; exit 99');
  fakeTool(work, 'Meltype.app/Contents/MacOS/MeltypeInstallSupport', 'echo "select:$*" >> "$MELTYPE_TEST_LOG"');
  fakeTool(work, 'open', 'echo UNEXPECTED_OPEN >> "$MELTYPE_TEST_LOG"; exit 1');
  runScript(work, 'update.sh');
  const lines = calls(work);
  assert.equal(lines[0], 'build:--test --install');
  assert.ok(lines[1]?.startsWith('app:--check-inputs '));
  assert.equal(lines[2], `select:--select ${path.join(work, 'Meltype.app')}`);
  assert.ok(!lines.includes('UNEXPECTED_START'));
  assert.ok(!lines.includes('UNEXPECTED_OPEN'));
}

scenario('update: 入力チェックの後に入力ソースを選び、起動し直さない', update);

scenario('update: ビルド・入力チェックの失敗で止める', work => {
  update(work);
  for (const failure of ['build', 'check']) {
    clearCalls(work);
    fakeTool(work, 'build-cli.sh', failure === 'build' ? 'exit 23' : 'exit 0');
    fakeTool(work, 'Meltype.app/Contents/MacOS/Meltype',
      `echo "app:$*" >> "$MELTYPE_TEST_LOG"\nexit ${failure === 'check' ? 23 : 0}`);
    const { status, output } = attemptScript(work, 'update.sh');
    assert.equal(status, 23, output);
    const lines = calls(work);
    if (failure === 'build') assert.deepEqual(lines, []);
    else {
      assert.equal(lines.length, 1);
      assert.ok(lines[0]?.startsWith('app:--check-inputs '));
    }
  }
});

const startArgs = (work: Work): string[] => ['/tmp/Meltype.app', path.join(work, 'fixture-helper')];

function osStartup(work: Work): void {
  writeFile(path.join(work, 'start-input-method.sh'), read(path.join(root, 'mac/start-input-method.sh')));
  fakeTool(work, 'fixture-helper', 'echo "$*" >> "$MELTYPE_TEST_LOG"');
  fakeTool(work, 'launchctl', '[[ "$1" == list ]] && exit 1; echo UNEXPECTED_DAEMON_START >> "$MELTYPE_TEST_LOG"; exit 99');
  runScript(work, 'start-input-method.sh', ...startArgs(work));
  assert.deepEqual(calls(work), ['--start /tmp/Meltype.app']);
}

scenario('起動: macOS のアプリとして起動する', osStartup);

scenario('起動: 失敗を伝え、前の版の launchd の job を片付ける', work => {
  osStartup(work);
  clearCalls(work);
  fakeTool(work, 'fixture-helper', 'echo failed >> "$MELTYPE_TEST_LOG"; exit 23');
  const { status, output } = attemptScript(work, 'start-input-method.sh', ...startArgs(work));
  assert.equal(status, 23, output);
  assert.deepEqual(calls(work), ['failed']);
  // 停止と job の削除はヘルパーが続けて行う。間に job が起動し直すと、停止の途中で前の版が動き出す。
  clearCalls(work);
  fakeTool(work, 'fixture-helper', 'echo "$*" >> "$MELTYPE_TEST_LOG"');
  fakeTool(work, 'launchctl', 'if [[ "$1" == list ]]; then exit 0; fi; echo "$*" >> "$MELTYPE_TEST_LOG"');
  runScript(work, 'start-input-method.sh', ...startArgs(work));
  const lines = calls(work);
  assert.ok(lines[0]?.startsWith('--stop /tmp/Meltype.app /tmp/Meltype.app '));
  assert.ok(lines[0]?.endsWith(' io.github.yksr-melt.Meltype.manual'));
  assert.ok(lines[1]?.startsWith('--restore /tmp/Meltype.app '));
  assert.equal(lines[2], '--start /tmp/Meltype.app');
});

// ---- install.sh・install-app.sh

function copyInstallScript(work: Work): void {
  writeFile(path.join(work, 'install-app.sh'),
    read(path.join(root, 'mac/install-app.sh')).replaceAll(lsregister, 'lsregister').replaceAll('/usr/sbin/sysctl', 'sysctl'));
  fakeTool(work, 'lsregister', 'exit 0');
  fakeTool(work, 'pgrep', 'exit 1');
  fakeTool(work, 'pkill', 'echo UNEXPECTED_KILL >> "$MELTYPE_TEST_LOG"; exit 99');
  fakeTool(work, 'launchctl', 'exit 1');
  if (!fs.existsSync(path.join(work, 'start-input-method.sh')))
    fakeTool(work, 'start-input-method.sh', 'echo restarted >> "$MELTYPE_TEST_LOG"');
}

const fixtureSupport = (work: Work): string => JSON.stringify(path.join(work, 'fixture-support'));

function installSupportTool(work: Work, prefix = 'source.app'): void {
  // 登録は更新の一部なので、偽のアプリにも 2 つのコマンドを置く。
  for (const name of ['Meltype', 'MeltypeRegisterInputSource']) {
    const relative = `${prefix}/Contents/MacOS/${name}`;
    if (!fs.existsSync(path.join(work, relative))) fakeTool(work, relative, 'exit 0');
  }
  // AppKit の操作だけを身代わりにし、swap・lock は本物を使う。開発者の入力メソッドを止めたり選んだりしない。
  if (!fs.existsSync(path.join(work, 'fixture-support'))) {
    fakeTool(work, 'fixture-support', [
      'case "$1" in',
      '--validate-bundle) exit 0 ;;',
      '--stop) if [[ -f "$MELTYPE_START_LOG_DIR/running" ]]; then [[ "$4" == - ]] || echo 1 > "$4"; rm "$MELTYPE_START_LOG_DIR/running"; echo stopped >> "$MELTYPE_TEST_LOG"; else [[ "$4" == - ]] || echo 0 > "$4"; fi; exit 0 ;;',
      '--restore) if [[ $(cat "$3") == 1 ]]; then touch "$MELTYPE_START_LOG_DIR/running"; echo restarted >> "$MELTYPE_TEST_LOG"; fi; exit 0 ;;',
      '--start) echo started >> "$MELTYPE_TEST_LOG"; exit 0 ;;',
      '--select) [[ -x "$2/Contents/MacOS/Meltype" ]] || exit 23; echo selected >> "$MELTYPE_TEST_LOG"; exit 0 ;;',
      'esac',
      `exec ${JSON.stringify(installTool)} "$@"`,
    ].join('\n'));
  }
  const tool = path.join(work, prefix, 'Contents/MacOS/MeltypeInstallSupport');
  writeFile(tool, read(path.join(work, 'fixture-support')));
  fs.chmodSync(tool, 0o755);
}

/** install.sh が呼ぶ身代わりを置き、初回のインストールを 1 回通す。 */
function distributedInstall(work: Work): void {
  writeFile(path.join(work, 'install.sh'),
    read(path.join(root, 'mac/install.sh'))
      .replaceAll('$HOME/Library/Input Methods', () => path.join(work, 'installed'))
      .replaceAll(lsregister, 'lsregister'));
  fakeTool(work, 'Meltype.app/Contents/MacOS/Meltype', 'echo "app:$*" >> "$MELTYPE_TEST_LOG"');
  installSupportTool(work, 'Meltype.app');
  fakeTool(work, 'Meltype.app/Contents/MacOS/MeltypeRegisterInputSource', 'exit 0');
  fakeTool(work, 'start-input-method.sh', 'echo started >> "$MELTYPE_TEST_LOG"');
  copyInstallScript(work);
  fakeTool(work, 'ditto', 'cp -R "$1" "$2"');
  fakeTool(work, 'codesign', 'exit 0');
  fakeTool(work, 'install-settings-app.sh', 'echo settings >> "$MELTYPE_TEST_LOG"');
  // 配布先に Swift の開発ツールがなくても、初回は自動で入力ソースを選べる。
  fakeTool(work, 'swift', 'echo UNEXPECTED_SWIFT >> "$MELTYPE_TEST_LOG"; exit 99');
  for (const name of ['pkill', 'xattr', 'lsregister']) fakeTool(work, name, 'exit 0');
  fakeTool(work, 'killall', 'echo UNEXPECTED_MENU_REFRESH >> "$MELTYPE_TEST_LOG"');
  fakeTool(work, 'open', 'echo UNEXPECTED_OPEN >> "$MELTYPE_TEST_LOG"; exit 1');
  runScript(work, 'install.sh');
  assert.deepEqual(calls(work), ['app:--register-input-source', 'started', 'settings', 'selected']);
}

const removeInstalled = (work: Work): void => fs.rmSync(path.join(work, 'installed/Meltype.app'), { recursive: true });

scenario('起動に失敗したら、入力ソースを選ばない', work => {
  distributedInstall(work);
  clearCalls(work);
  fakeTool(work, 'start-input-method.sh', 'exit 1');
  assert.notEqual(attemptScript(work, 'install.sh').status, 0);
  assert.ok(!calls(work).includes('selected'));
});

scenario('配布用の install.sh', distributedInstall);

scenario('初回のインストールで入力ソースを選べなくても、アプリを残して入力メニューから選ぶよう案内する', work => {
  distributedInstall(work);
  removeInstalled(work);
  fakeTool(work, 'Meltype.app/Contents/MacOS/MeltypeInstallSupport',
    `[[ "$1" == --select ]] && exit 2\nexec ${fixtureSupport(work)} "$@"`);
  const message = runScript(work, 'install.sh');
  assert.ok(message.includes('入力メニューから Meltype を選んでください'), message);
  assert.ok(fs.existsSync(path.join(work, 'installed/Meltype.app/Contents/MacOS/Meltype')));
});

scenario('更新では入力メニューを起動し直さず、初回のインストールでは起動し直す', work => {
  distributedInstall(work);
  fakeTool(work, 'Meltype.app/Contents/MacOS/MeltypeRegisterInputSource', 'echo helper:${2:-initial} >> "$MELTYPE_TEST_LOG"');
  // 同じ入口でも、既存アプリの更新にだけ両コマンドへ指定する。
  for (const isUpdate of [true, false]) {
    if (!isUpdate) removeInstalled(work);
    clearCalls(work);
    runScript(work, 'install.sh');
    const lines = calls(work);
    assert.ok(lines.includes(isUpdate ? 'app:--register-input-source --keep-input-menu' : 'app:--register-input-source'));
    assert.ok(lines.includes(isUpdate ? 'helper:--keep-input-menu' : 'helper:initial'));
    assert.equal(lines.includes('selected'), !isUpdate);
  }
});

scenario('本体の登録コマンドは、入力メニューを起動し直さないときも同じ結果を返す', work => {
  // 本体の登録コマンドをそのままコンパイルし、登録APIとkillallだけをスタブ化する。
  const source = read(path.join(root, 'mac/Sources/MeltypeIME/main.swift'));
  const end = source.indexOf('func runIsolatedChecks(');
  assert.notEqual(end, -1);
  const binary = path.join(work, 'main-registrar');
  const called = path.join(work, 'menu-restart-called');
  writeFile(path.join(work, 'main-registrar.swift'),
    source.slice(0, end).replaceAll('/usr/bin/killall', () => path.join(work, 'killall'))
    + 'enum InputSourceRegistration { enum Outcome { case ready, pending, failed; var exitCode: Int32 { self == .failed ? 1 : 0 } }; static func register() -> Outcome { if CommandLine.arguments.contains("failed") { return .failed }; return CommandLine.arguments.contains("pending") ? .pending : .ready } }');
  fakeTool(work, 'killall', `touch ${JSON.stringify(called)}; exit 0`);
  run('xcrun', 'swiftc', path.join(work, 'main-registrar.swift'), '-o', binary);
  for (const keep of [false, true]) {
    for (const outcome of ['ready', 'pending', 'failed']) {
      fs.rmSync(called, { force: true });
      const { status, output } = attempt(binary, '--register-input-source', outcome, ...(keep ? ['--keep-input-menu'] : []));
      assert.equal(status, outcome === 'failed' ? 1 : 0, output);
      assert.equal(fs.existsSync(called), !keep && outcome === 'ready');
    }
  }
});

scenario('ファイルの足りない配布物は、何もせずに止める', work => {
  distributedInstall(work);
  clearCalls(work);
  fs.rmSync(path.join(work, 'start-input-method.sh'));
  assert.notEqual(attemptScript(work, 'install.sh').status, 0);
  assert.deepEqual(calls(work), []);
});

function installRollback(work: Work): void {
  copyInstallScript(work);
  writeFile(path.join(work, 'source.app/Contents/version'), 'new');
  writeFile(path.join(work, 'installed.app/Contents/version'), 'previous');
  installSupportTool(work);
  fakeTool(work, 'pkill', 'echo stopped >> "$MELTYPE_TEST_LOG"');
  for (const failure of ['copy', 'signature', 'replace']) {
    clearCalls(work);
    fakeTool(work, 'ditto', failure === 'copy' ? 'exit 23' : 'cp -R "$1" "$2"');
    fakeTool(work, 'codesign', failure === 'signature' ? 'exit 23' : 'exit 0');
    if (failure === 'replace')
      fakeTool(work, 'source.app/Contents/MacOS/MeltypeInstallSupport',
        `if [[ "$1" == --* ]]; then exec ${fixtureSupport(work)} "$@"; fi; exit 23`);
    else installSupportTool(work);
    const { status, output } = attemptInstallApp(work);
    assert.equal(status, 23, output);
    assert.equal(installedVersion(work), 'previous');
    if (failure !== 'replace') assert.deepEqual(calls(work), []);
  }
}

scenario('コピー・署名・入れ替えの失敗で、前の版を残す', installRollback);

scenario('入れ替えた後の失敗で前の版に戻し、戻せなければ控えを残す', work => {
  installRollback(work);
  installSupportTool(work);
  fakeTool(work, 'codesign', 'if [[ "${@: -1}" == */installed.app ]]; then exit 23; fi');
  fakeTool(work, 'mktemp', 'mkdir -p "$MELTYPE_START_LOG_DIR/staging"; echo "$MELTYPE_START_LOG_DIR/staging"');
  for (const restoreFails of [false, true]) {
    fakeTool(work, 'source.app/Contents/MacOS/MeltypeInstallSupport', [
      `if [[ "$1" == --* ]]; then exec ${fixtureSupport(work)} "$@"; fi`,
      restoreFails ? 'if [[ -f "$MELTYPE_START_LOG_DIR/swapped" ]]; then exit 27; fi' : '',
      'touch "$MELTYPE_START_LOG_DIR/swapped"',
      `exec ${fixtureSupport(work)} "$@"`,
    ].join('\n'));
    fs.rmSync(path.join(work, 'swapped'), { force: true });
    const { status, output } = attemptInstallApp(work);
    assert.equal(status, 23, output);
    if (restoreFails) {
      assert.equal(read(path.join(work, 'staging/Meltype.app/Contents/version')), 'previous');
      assert.equal(installedVersion(work), 'new');
    } else {
      assert.equal(installedVersion(work), 'previous');
      assert.ok(!fs.existsSync(path.join(work, 'staging')));
    }
  }
});

scenario('別のボリュームには、入力メソッドを止める前に断る', work => {
  installRollback(work);
  clearCalls(work);
  fakeTool(work, 'stat', 'if [[ "$3" == */.meltype-install.* ]]; then echo 1; else echo 2; fi');
  assert.notEqual(attemptInstallApp(work).status, 0);
  assert.equal(installedVersion(work), 'previous');
  assert.deepEqual(calls(work), []);
});

scenario('初回のインストールと、ヘルパーの無い版からの入れ替え', work => {
  copyInstallScript(work);
  writeFile(path.join(work, 'source.app/Contents/version'), 'new');
  installSupportTool(work);
  for (const name of ['pkill', 'codesign']) fakeTool(work, name, 'exit 0');
  fakeTool(work, 'ditto', 'cp -R "$1" "$2"');
  for (const replaceOld of [false, true]) {
    if (replaceOld) {
      fs.rmSync(path.join(work, 'installed.app/Contents'), { recursive: true });
      writeFile(path.join(work, 'installed.app/Contents/version'), 'previous');
    }
    runScript(work, 'install-app.sh', ...installAppArgs(work));
    assert.equal(installedVersion(work), 'new');
    assert.equal(read(path.join(work, 'source.app/Contents/version')), 'new');
  }
});

scenario('初回のインストールが途中で失敗したら、途中のアプリを消す', work => {
  copyInstallScript(work);
  writeFile(path.join(work, 'source.app/Contents/version'), 'new');
  installSupportTool(work);
  for (const name of ['pkill', 'codesign']) fakeTool(work, name, 'exit 0');
  fakeTool(work, 'ditto', 'cp -R "$1" "$2"');
  fakeTool(work, 'mv', 'if [[ "$1" == */Meltype.app ]]; then mkdir -p "$2/Contents"; echo incomplete > "$2/Contents/version"; exit 23; fi; exec /bin/mv "$@"');
  const { status, output } = attemptInstallApp(work);
  assert.equal(status, 23, output);
  assert.ok(!fs.existsSync(path.join(work, 'installed.app')));
});

scenario('入れ替えの途中でアプリが無くなる瞬間が無く、正しくない引数は何も変えない', async work => {
  const left = path.join(work, 'left');
  const right = path.join(work, 'right');
  writeFile(path.join(left, 'version'), 'old');
  writeFile(path.join(right, 'version'), 'new');
  const observer = spawn('bash', ['-c',
    'touch "$1/ready"; while [[ ! -e "$1/stop" ]]; do [[ -d "$1/left" ]] || touch "$1/missing"; done', 'observer', work], { stdio: 'ignore' });
  const exited = once(observer, 'exit');
  try {
    await waitForFile(path.join(work, 'ready'), 100, 10);
    for (let i = 0; i < 100; i++) {
      run(installTool, left, right);
      assert.equal(read(path.join(left, 'version')), i % 2 === 0 ? 'new' : 'old');
    }
  } finally {
    writeFile(path.join(work, 'stop'), '');
    await exited;
  }
  assert.ok(!fs.existsSync(path.join(work, 'missing')));
  const link = path.join(work, 'link');
  fs.symlinkSync(left, link);
  for (const other of [left, path.join(work, 'absent'), link, path.join(right, 'version')]) {
    assert.notEqual(attempt(installTool, left, other).status, 0);
    assert.equal(read(path.join(left, 'version')), 'old');
    assert.equal(read(path.join(right, 'version')), 'new');
  }
});

function bundleHash(app: string): string {
  const hash = /CDHash=.*/.exec(run('/usr/bin/codesign', '-dvvv', app))?.[0];
  assert.ok(hash);
  return hash;
}

const sign = (app: string): string => run('/usr/bin/codesign', '--force', '--deep', '--sign', '-', app);
const setXattr = (file: string, name: string, value: string): string => run('/usr/bin/xattr', '-w', name, value, file);
const inode = (file: string): string => run('/usr/bin/stat', '-f', '%i', file);

scenario('署名されたアプリの入れ替えと、改変された更新の拒否', work => {
  copyInstallScript(work);
  installSupportTool(work);
  const source = `${work}/source.app/`;
  const target = `${work}/installed.app/`;
  const args = [source, target];
  const plist = path.join(source, 'Contents/Info.plist');
  const sourceVersion = path.join(source, 'Contents/Resources/version');
  writeFile(plist, '<?xml version="1.0"?><plist version="1.0"><dict><key>CFBundleIdentifier</key><string>local.meltype.SwapTests</string><key>CFBundleExecutable</key><string>Meltype</string><key>CFBundlePackageType</key><string>APPL</string><key>CFBundleVersion</key><string>1</string></dict></plist>');
  writeFile(sourceVersion, 'A');
  compileEmpty(work, path.join(source, 'Contents/MacOS/Meltype'));
  sign(source);
  runScript(work, 'install-app.sh', ...args);
  const firstHash = bundleHash(target);
  const outerInode = inode(target);
  setXattr(target, 'com.apple.quarantine', '0083;12345678;Fixture;');
  setXattr(target, 'com.example.meltype-test', 'keep');
  writeFile(path.join(target, '.DS_Store'), 'Finder metadata');
  writeFile(path.join(source, '.DS_Store'), 'New Finder metadata');
  writeFile(sourceVersion, 'B');
  writeFile(plist, read(plist).replaceAll('<string>1</string>', '<string>2</string>'));
  sign(source);
  runScript(work, 'install-app.sh', ...args);
  const secondHash = bundleHash(target);
  assert.equal(read(path.join(target, '.DS_Store')), 'Finder metadata');
  assert.notEqual(secondHash, firstHash);
  assert.equal(bundleHash(source), secondHash);
  assert.equal(inode(target), outerInode);
  assert.equal(run('/usr/bin/xattr', '-p', 'com.example.meltype-test', target), 'keep\n');
  assert.ok(!run('/usr/bin/xattr', target).includes('com.apple.quarantine'));
  // Contents を入れ替えた後の検証に失敗したら、B の署名に戻す。
  setXattr(target, 'com.apple.quarantine', '0083;12345678;Previous;');
  setXattr(path.join(target, 'Contents'), 'com.apple.quarantine', '0083;12345678;PreviousContents;');
  writeFile(sourceVersion, 'C');
  sign(source);
  fakeTool(work, 'codesign',
    'app="${@: -1}"; if [[ "${app%/}" == */installed.app && $(cat "$app/Contents/Resources/version") == C ]]; then exit 23; fi; exec /usr/bin/codesign "$@"');
  const failed = attemptScript(work, 'install-app.sh', ...args);
  assert.equal(failed.status, 23, failed.output);
  assert.equal(bundleHash(target), secondHash);
  assert.ok(!run('/usr/bin/xattr', target).includes('com.apple.quarantine'));
  assert.equal(run('/usr/bin/xattr', '-p', 'com.apple.quarantine', path.join(target, 'Contents')), '0083;12345678;PreviousContents;\n');
  assert.equal(inode(target), outerInode);
  run('/usr/bin/codesign', '--verify', '--deep', '--strict', target);
  // 改変された更新は、インストール済みの入力メソッドを止める前に失敗する。
  writeFile(sourceVersion, 'tampered');
  clearCalls(work);
  assert.notEqual(attemptScript(work, 'install-app.sh', ...args).status, 0);
  assert.deepEqual(calls(work), []);
  assert.equal(read(path.join(target, 'Contents/Resources/version')), 'B');
});

scenario('失敗して前の版に戻したら、動いていた入力メソッドを起動し直す', work => {
  installRollback(work);
  installSupportTool(work);
  writeFile(path.join(work, 'running'), '');
  fakeTool(work, 'codesign', 'if [[ "${@: -1}" == */installed.app ]]; then touch "$MELTYPE_START_LOG_DIR/running"; exit 23; fi');
  clearCalls(work);
  const { status, output } = attemptInstallApp(work);
  assert.equal(status, 23, output);
  assert.equal(installedVersion(work), 'previous');
  assert.deepEqual(calls(work), ['stopped', 'stopped', 'restarted']);
});

scenario('同時のインストールは断り、終わった後のやり直しは成功する', async work => {
  copyInstallScript(work);
  for (const name of ['source-a.app', 'source-b.app']) installSupportTool(work, name);
  writeFile(path.join(work, 'source-a.app/Contents/version'), 'A');
  writeFile(path.join(work, 'source-b.app/Contents/version'), 'B');
  fakeTool(work, 'pkill', 'exit 0');
  fakeTool(work, 'ditto', 'cp -R "$1" "$2"');
  fakeTool(work, 'codesign',
    'target="${@: -1}"; if [[ "$target" == */installed.app && $(cat "$target/Contents/version") == A ]]; then touch "$MELTYPE_START_LOG_DIR/ready"; for _ in {1..200}; do [[ -f "$MELTYPE_START_LOG_DIR/release" ]] && exit 23; sleep 0.05; done; exit 24; fi');
  const installed = path.join(work, 'installed.app');
  // 初回のインストールと更新の両方。1 つ目は A を置いた後の検証でわざと失敗し、その間に 2 つ目が B を置こうとする。
  for (const previous of [false, true]) {
    if (previous) writeFile(path.join(installed, 'Contents/version'), 'previous');
    for (const name of ['ready', 'release']) fs.rmSync(path.join(work, name), { force: true });
    const first = spawn('bash', [path.join(work, 'install-app.sh'), path.join(work, 'source-a.app'), installed], {
      stdio: 'ignore',
      env: { ...process.env, PATH: `${work}/:${process.env.PATH}`, MELTYPE_START_LOG_DIR: `${work}/` },
    });
    const exited = once(first, 'exit');
    try {
      await waitForFile(path.join(work, 'ready'), 500, 10);
      const { status, output } = attemptScript(work, 'install-app.sh', path.join(work, 'source-b.app'), installed);
      assert.equal(status, 75, output);
      assert.equal(installedVersion(work), 'A');
    } finally {
      writeFile(path.join(work, 'release'), '');
      const [code] = await exited;
      assert.equal(code, 23);
    }
    if (previous) assert.equal(installedVersion(work), 'previous');
    else assert.ok(!fs.existsSync(installed));
    // 失敗の後はロックを放すので、B のやり直しは成功する。
    runScript(work, 'install-app.sh', path.join(work, 'source-b.app'), installed);
    assert.equal(installedVersion(work), 'B');
  }
});

// 手動の更新は、登録・起動に失敗したら (止まっていたときも) 前の版に戻す。
// Homebrew は前の版を消した後に呼ぶので、新しい版を残して警告する。
for (const homebrew of [false, true]) {
  for (const failure of ['registration', 'helper', 'launchservices', 'startup', 'startup-pending']) {
    scenario(`登録・起動の失敗 (${homebrew ? 'Homebrew' : '手動'}、${failure})`, work => {
      distributedInstall(work);
      writeFile(path.join(work, 'installed/Meltype.app/Contents/version'), 'old');
      writeFile(path.join(work, 'Meltype.app/Contents/version'), 'new');
      if (homebrew) removeInstalled(work);
      clearCalls(work);
      if (failure === 'startup-pending') fakeTool(work, 'Meltype.app/Contents/MacOS/MeltypeRegisterInputSource', 'exit 2');
      const broken = failure === 'registration' ? 'Meltype.app/Contents/MacOS/Meltype'
        : failure === 'helper' ? 'Meltype.app/Contents/MacOS/MeltypeRegisterInputSource'
        : failure === 'launchservices' ? 'lsregister'
        : 'start-input-method.sh';
      fakeTool(work, broken, '[[ "$1" == -u ]] && exit 0; touch "$MELTYPE_START_LOG_DIR/running"; echo failed >> "$MELTYPE_TEST_LOG"; exit 23');
      const { status, output } = attemptScript(work, 'install.sh', ...(homebrew ? ['--homebrew'] : []));
      assert.equal(status, homebrew ? 0 : 23, output);
      if (homebrew)
        assert.ok(output.includes('Meltype を入れましたが、登録か起動が終わりませんでした。ログアウトしてログインし直してから、入力メニューで Meltype を選んでください。'), output);
      assert.equal(read(path.join(work, 'installed/Meltype.app/Contents/version')), homebrew ? 'new' : 'old');
      assert.equal(fs.existsSync(path.join(work, 'running')), homebrew);
      const lines = calls(work);
      assert.ok(lines.includes('failed'));
      assert.equal(lines.includes('stopped'), !homebrew);
      if (homebrew && !failure.startsWith('startup')) assert.ok(lines.includes('started'));
      assert.ok(!lines.includes('restarted'));
      assert.ok(!lines.includes('selected'));
    });
  }
}

scenario('Homebrew でも、署名の失敗は止める', work => {
  distributedInstall(work);
  removeInstalled(work);
  clearCalls(work);
  fakeTool(work, 'codesign', 'exit 23');
  const { status, output } = attemptScript(work, 'install.sh', '--homebrew');
  assert.equal(status, 23, output);
  assert.ok(!fs.existsSync(path.join(work, 'installed/Meltype.app')));
  assert.deepEqual(calls(work), []);
});

scenario('止まっていた入力メソッドの更新では、起動が終わるまで控えとロックを持つ', work => {
  distributedInstall(work);
  writeFile(path.join(work, 'installed/Meltype.app/Contents/version'), 'old');
  writeFile(path.join(work, 'Meltype.app/Contents/version'), 'new');
  clearCalls(work);
  fakeTool(work, 'start-input-method.sh', [
    'stage="${2%/*}"',
    '[[ $(cat "$stage/Meltype.app/Contents/version") == old ]] || exit 91',
    '[[ $(cat "$stage/input-state.json") == 0 ]] || exit 92',
    'status=0',
    `${JSON.stringify(installTool)} --lock "$(dirname "$1")/.Meltype.app.install.lock" /usr/bin/true || status=$?`,
    '[[ $status == 75 ]] || exit 93',
    'echo "$stage" > "$MELTYPE_START_LOG_DIR/observed-stage"',
    'echo started >> "$MELTYPE_TEST_LOG"',
  ].join('\n'));
  runScript(work, 'install.sh');
  assert.equal(read(path.join(work, 'installed/Meltype.app/Contents/version')), 'new');
  assert.ok(!fs.existsSync(read(path.join(work, 'observed-stage')).trim()));
  const lines = calls(work);
  assert.ok(lines.includes('started'));
  assert.ok(!lines.includes('selected'));
});

scenario('本物の AppKit で、通常の終了・起動と、入力ソースの選択の維持', async work => {
  // 入力メソッドではない裏のアプリを使い、選択中の入力メソッドを変えずに終了と起動を確かめる。
  const id = path.basename(work);
  const app = path.join(work, 'LifecycleFixture.app');
  const executable = path.join(app, 'Contents/MacOS/Fixture');
  const state = path.join(work, 'input-state.json');
  const beforeState = path.join(work, 'selected-before.json');
  const ready = path.join(work, 'ready');
  const job = `local.meltype.manual-job.${id}`;
  writeFile(path.join(work, 'fixture.swift'),
    `import AppKit\nNSApplication.shared.setActivationPolicy(.prohibited)\nDispatchQueue.main.async { FileManager.default.createFile(atPath: ${JSON.stringify(ready)}, contents: nil) }\nNSApplication.shared.run()\n`);
  writeFile(path.join(app, 'Contents/Info.plist'),
    `<?xml version="1.0"?><plist version="1.0"><dict><key>CFBundleIdentifier</key><string>local.meltype.Lifecycle.${id}</string><key>CFBundleExecutable</key><string>Fixture</string><key>CFBundlePackageType</key><string>APPL</string><key>LSBackgroundOnly</key><true/></dict></plist>`);
  fs.mkdirSync(path.dirname(executable), { recursive: true });
  run('xcrun', 'swiftc', path.join(work, 'fixture.swift'), '-o', executable);
  sign(app);
  const validate = (): Result => attempt(installTool, '--validate-bundle', app);
  const finder = path.join(app, '.DS_Store');
  assert.equal(validate().status, 0);
  writeFile(finder, 'Finder metadata');
  assert.equal(validate().status, 0);
  run('/usr/bin/codesign', '--verify', '--deep', '--strict', app);
  fs.rmSync(finder);
  const outside = path.join(work, 'outside');
  writeFile(outside, 'do not modify');
  fs.symlinkSync(outside, finder);
  const linked = validate();
  assert.equal(linked.status, 1, linked.output);
  assert.ok(linked.output.includes('Move it outside the app'), linked.output);
  assert.equal(read(outside), 'do not modify');
  fs.rmSync(finder);
  const unexpected = path.join(app, 'unexpected-root-payload');
  writeFile(unexpected, 'preserve me');
  assert.notEqual(validate().status, 0);
  fs.rmSync(unexpected);
  const stop = (...jobs: string[]): Result => attempt(installTool, '--stop', app, app, state, ...jobs);
  try {
    // 実行ファイルを起動し直す手動の job の下で、裏のアプリを起動する。
    run('/bin/launchctl', 'submit', '-l', job, '--', executable);
    await waitForFile(ready, 100, 100);
    const stopped = stop(job);
    assert.equal(stopped.status, 0, stopped.output);
    assert.ok(read(state).includes('"wasRunning":true'));
    assert.notEqual(attempt('/bin/launchctl', 'list', job).status, 0);
    run(installTool, '--start', app);
    const started = stop();
    assert.equal(started.status, 0, started.output);
    assert.ok(read(state).includes('"wasRunning":true'));
    assert.ok(!read(state).includes('temporarySourceID'));
    fs.copyFileSync(state, beforeState);
    run(installTool, '--restore', app, state);
    const restored = stop();
    assert.equal(restored.status, 0, restored.output);
    assert.equal(read(state), read(beforeState));
  } finally {
    attempt('/bin/launchctl', 'remove', job);
    stop();
    attempt(lsregister, '-u', app);
  }
});

scenario('停止の拒否・起動の失敗で、元の状態に戻す', work => {
  copyInstallScript(work);
  writeFile(path.join(work, 'source.app/Contents/version'), 'new');
  writeFile(path.join(work, 'installed.app/Contents/version'), 'old');
  fakeTool(work, 'ditto', 'cp -R "$1" "$2"');
  fakeTool(work, 'codesign', 'exit 0');
  for (const failure of ['stop', 'restore']) {
    installSupportTool(work);
    writeFile(path.join(work, 'running'), '');
    clearCalls(work);
    fakeTool(work, 'source.app/Contents/MacOS/MeltypeInstallSupport', [
      failure === 'stop'
        ? 'if [[ "$1" == --stop && "${4##*/}" == input-state.json ]]; then echo 1 > "$4"; echo retreated >> "$MELTYPE_TEST_LOG"; exit 23; fi'
        : 'if [[ "$1" == --restore && $(cat "$2/Contents/version") == new ]]; then touch "$MELTYPE_START_LOG_DIR/running"; echo startup-failed >> "$MELTYPE_TEST_LOG"; exit 23; fi',
      `exec ${fixtureSupport(work)} "$@"`,
    ].join('\n'));
    const { status, output } = attemptInstallApp(work);
    assert.equal(status, 23, output);
    assert.equal(installedVersion(work), 'old');
    assert.ok(fs.existsSync(path.join(work, 'running')));
    assert.equal(calls(work).at(-1), 'restarted');
  }
});

scenario('初回か更新か・Homebrew かは、ロックを取ってから決める', work => {
  distributedInstall(work);
  // このインストールが元のアプリを準備している間に、別のインストールが終わる。
  fakeTool(work, 'ditto',
    'cp -R "$1" "$2"; mkdir -p "$MELTYPE_START_LOG_DIR/installed/Meltype.app/Contents"; echo other > "$MELTYPE_START_LOG_DIR/installed/Meltype.app/Contents/version"');
  for (const homebrew of [false, true]) {
    removeInstalled(work);
    clearCalls(work);
    const { status, output } = attemptScript(work, 'install.sh', ...(homebrew ? ['--homebrew'] : []));
    assert.equal(status, homebrew ? 1 : 0, output);
    const lines = calls(work);
    assert.ok(!lines.includes('selected'));
    if (homebrew) {
      assert.deepEqual(lines, []);
      assert.equal(read(path.join(work, 'installed/Meltype.app/Contents/version')), 'other\n');
    }
  }
});

scenario('子プロセスが突然終わっても、控えと状態を残す', work => {
  copyInstallScript(work);
  writeFile(path.join(work, 'source.app/Contents/version'), 'new');
  writeFile(path.join(work, 'installed.app/Contents/version'), 'old');
  installSupportTool(work);
  fakeTool(work, 'ditto', 'cp -R "$1" "$2"');
  fakeTool(work, 'mktemp', 'mkdir -p "$MELTYPE_START_LOG_DIR/staging"; echo "$MELTYPE_START_LOG_DIR/staging"');
  fakeTool(work, 'codesign', 'if [[ "${@: -1}" == */installed.app ]]; then kill -KILL "$PPID"; fi');
  const { status, output } = attemptInstallApp(work);
  assert.equal(status, 137, output);
  assert.equal(read(path.join(work, 'staging/Meltype.app/Contents/version')), 'old');
  assert.ok(fs.existsSync(path.join(work, 'staging/input-state.json')));
  assert.equal(installedVersion(work), 'new');
});

scenario('読み取り専用のファイル・quarantine のあるアプリは、元を変えずに整えてからヘルパーを動かす', work => {
  copyInstallScript(work);
  const source = path.join(work, 'source.app');
  writeFile(path.join(source, 'Contents/version'), 'new');
  installSupportTool(work);
  fakeTool(work, 'codesign', 'exit 0');
  fakeTool(work, 'ditto', 'cp -R "$1" "$2"');
  fakeTool(work, 'source.app/Contents/MacOS/MeltypeInstallSupport', [
    'if [[ "$1" == --validate-bundle ]]; then',
    'if /usr/bin/xattr "$2" | /usr/bin/grep -q \'^com.apple.quarantine$\'; then exit 86; fi',
    'fi',
    `exec ${fixtureSupport(work)} "$@"`,
  ].join('\n'));
  setXattr(source, 'com.apple.quarantine', '0083;12345678;Fixture;');
  setXattr(source, 'com.example.meltype-test', 'keep');
  // 配布物のファイルは、quarantine の有無によらず読み取り専用のことがある。ヘルパーは整えた後も実行できなければならない。
  for (const name of ['plain', 'quarantined']) {
    const resource = path.join(source, 'Contents/Resources', name);
    writeFile(resource, name);
    if (name === 'quarantined') setXattr(resource, 'com.apple.quarantine', '0083;12345678;Fixture;');
    fs.chmodSync(resource, 0o444);
  }
  fs.chmodSync(path.join(source, 'Contents/MacOS/MeltypeInstallSupport'), 0o555);
  runScript(work, 'install-app.sh', ...installAppArgs(work));
  for (const name of ['plain', 'quarantined']) {
    const installed = path.join(work, 'installed.app/Contents/Resources', name);
    assert.equal(read(installed), name);
    assert.equal(fs.statSync(path.join(source, 'Contents/Resources', name)).mode & 0o777, 0o444);
    assert.ok(!run('/usr/bin/xattr', installed).includes('com.apple.quarantine'));
  }
  assert.ok(run('/usr/bin/xattr', path.join(source, 'Contents/Resources/quarantined')).includes('com.apple.quarantine'));
  assert.equal(run('/usr/bin/xattr', '-p', 'com.example.meltype-test', path.join(work, 'installed.app')).trim(), 'keep');
});

scenario('ヘルパーの実行を拒まれたら、アプリを入れ替えずにヘルパーと診断を残す', work => {
  copyInstallScript(work);
  writeFile(path.join(work, 'source.app/Contents/version'), 'new');
  fakeTool(work, 'source.app/Contents/MacOS/MeltypeInstallSupport', 'exit 126');
  fakeTool(work, 'codesign', 'exit 0');
  fakeTool(work, 'ditto', 'cp -R "$1" "$2"');
  fakeTool(work, 'mktemp', 'mkdir -p "$MELTYPE_START_LOG_DIR/staging"; echo "$MELTYPE_START_LOG_DIR/staging"');
  const { status, output } = attemptInstallApp(work);
  assert.equal(status, 126, output);
  assert.ok(!fs.existsSync(path.join(work, 'installed.app')));
  assert.ok(fs.existsSync(path.join(work, 'staging/install-support')));
  assert.ok(fs.existsSync(path.join(work, 'staging/helper-diagnostics.txt')));
});

scenario('Intel の Mac に Apple シリコン用のアプリを入れない', work => {
  copyInstallScript(work);
  fakeTool(work, 'ditto', 'cp -R "$1" "$2"');
  // 署名の検証 (23) まで進めば、CPU は受け入れられている。
  fakeTool(work, 'codesign', 'exit 23');
  const cases: [appleSilicon: string, architectures: string[], expected: number][] = [
    ['', ['arm64'], 1],
    ['', ['x86_64'], 23],
    ['', ['arm64', 'x86_64'], 23],
    ['1', ['arm64'], 23],
    ['1', ['x86_64'], 23],
  ];
  for (const [appleSilicon, architectures, expected] of cases) {
    compileEmpty(work, path.join(work, 'source.app/Contents/MacOS/MeltypeInstallSupport'), architectures);
    fakeTool(work, 'sysctl', `echo ${appleSilicon}`);
    const { status, output } = attemptScript(work, 'install-app.sh', path.join(work, 'source.app'), path.join(work, 'installed/Meltype.app'));
    assert.equal(status, expected, output);
    assert.equal(output.includes('Apple シリコン用'), expected === 1, output);
    // 動かないアプリのために、何も置かず、何も残さない。
    if (expected === 1) assert.ok(!fs.existsSync(path.join(work, 'installed')));
    assert.deepEqual(fs.readdirSync(work).filter(name => name.startsWith('.meltype-install.')), []);
  }
});

scenario('登録コマンドは、登録の失敗・有効化の保留・メニューの更新の警告を区別する', work => {
  // TIS の登録・一覧の境界と killall のプロセスだけを身代わりにする。メタデータの検証・終了コード・文言は本物をコンパイルする。
  const original = read(path.join(root, 'mac/Sources/RegisterInputSource/main.swift'));
  const begin = original.indexOf('func isEnabledInputSource(');
  const end = original.indexOf('// 入力メニューの再起動を要求する。', begin);
  assert.ok(begin >= 0 && end >= 0);
  const app = `${work}/Fixture.app/`;
  const binary = path.join(work, 'registrar');
  writeFile(path.join(work, 'registrar.swift'),
    (original.slice(0, begin)
      + 'func isEnabledInputSource(bundleID: String) -> Bool { !CommandLine.arguments.contains("pending") }\n'
      + original.slice(end))
      .replaceAll('let status = TISRegisterInputSource(URL(fileURLWithPath: appPath) as CFURL)',
        'let status: OSStatus = CommandLine.arguments.count > 2 ? Int32(CommandLine.arguments[2])! : noErr')
      .replaceAll('/usr/bin/killall', () => path.join(work, 'killall')));
  writeFile(path.join(app, 'Contents/Info.plist'),
    '<?xml version="1.0"?><plist version="1.0"><dict><key>CFBundleIdentifier</key><string>local.meltype.RegistrarFixture</string><key>CFBundlePackageType</key><string>APPL</string></dict></plist>');
  run('xcrun', 'swiftc', path.join(work, 'registrar.swift'), '-o', binary);
  for (const code of [0, 1, 23]) {
    fakeTool(work, 'killall', `exit ${code}`);
    const { status, output } = attempt(binary, app);
    assert.equal(status, 0, output);
    if (code === 1) assert.ok(output.includes('プロセスはありません'), output);
  }
  // 更新指定でも登録失敗・有効化保留は維持し、メニュー再起動は行わない。
  const called = path.join(work, 'menu-restart-called');
  fakeTool(work, 'killall', `touch ${JSON.stringify(called)}; exit 23`);
  for (const [args, expected] of [[['0'], 0], [['0', 'pending'], 2], [['-50'], 1]] as const) {
    const { status, output } = attempt(binary, app, ...args, '--keep-input-menu');
    assert.equal(status, expected, output);
    assert.ok(!fs.existsSync(called));
  }
  for (const [args, expected, message] of [[['0', 'pending'], 2, 'システム設定'], [['-50'], 1, '登録できませんでした']] as const) {
    const { status, output } = attempt(binary, app, ...args);
    assert.equal(status, expected, output);
    assert.ok(output.includes(message), output);
  }
});

for (const mode of ['initial', 'update', 'homebrew', 'source'] as const) {
  scenario(`有効化が保留でも、アプリは残す (${mode})`, work => {
    distributedInstall(work);
    if (mode !== 'update') removeInstalled(work);
    writeFile(path.join(work, 'Meltype.app/Contents/version'), 'new');
    clearCalls(work);
    fakeTool(work, 'Meltype.app/Contents/MacOS/MeltypeRegisterInputSource', 'exit 2');
    const message = mode === 'source'
      ? runScript(work, 'install-app.sh', path.join(work, 'Meltype.app'), path.join(work, 'installed/Meltype.app'))
      : runScript(work, 'install.sh', ...(mode === 'homebrew' ? ['--homebrew'] : []));
    assert.ok(message.includes('有効化は保留中'), message);
    assert.ok(message.includes('システム設定'), message);
    // 有効化の保留が、続く起動の失敗を隠してはならない。
    if (mode === 'update') {
      writeFile(path.join(work, 'Meltype.app/Contents/version'), 'broken');
      fakeTool(work, 'start-input-method.sh', 'exit 23');
      const { status, output } = attemptScript(work, 'install.sh');
      assert.equal(status, 23, output);
    }
    assert.equal(read(path.join(work, 'installed/Meltype.app/Contents/version')), 'new');
    const lines = calls(work);
    assert.ok(lines.includes('started'));
    assert.ok(!lines.includes('stopped'));
  });
}

scenario('入力ソースを選べなかっただけなら、更新を残す', async work => {
  copyInstallScript(work);
  writeFile(path.join(work, 'source.app/Contents/version'), 'new');
  writeFile(path.join(work, 'installed.app/Contents/version'), 'old');
  installSupportTool(work);
  writeFile(path.join(work, 'running'), '');
  fakeTool(work, 'ditto', 'cp -R "$1" "$2"');
  fakeTool(work, 'codesign', 'exit 0');
  fakeTool(work, 'source.app/Contents/MacOS/MeltypeInstallSupport',
    `if [[ "$1" == --restore ]]; then touch "$MELTYPE_START_LOG_DIR/running"; exit 2; fi\nexec ${fixtureSupport(work)} "$@"`);
  // 外側の install.sh は、選べなかったという警告と矛盾することを言わない。
  await withWork(installer => {
    distributedInstall(installer);
    fakeTool(installer, 'Meltype.app/Contents/MacOS/MeltypeInstallSupport',
      `if [[ "$1" == --restore ]]; then exit 2; fi\nexec ${fixtureSupport(installer)} "$@"`);
    const message = runScript(installer, 'install.sh');
    assert.ok(message.includes('入力ソースを戻せませんでした'), message);
    assert.ok(message.includes('Meltype を更新しました。'), message);
    assert.ok(!message.includes('選択を維持します'), message);
  });
  const receipt = path.join(work, 'receipt');
  const message = runScript(work, 'install-app.sh', ...installAppArgs(work), 'manual', receipt);
  assert.ok(message.includes('入力メニューから選び直してください'), message);
  assert.equal(readLines(receipt)[0], 'update');
  assert.equal(installedVersion(work), 'new');
  assert.ok(fs.existsSync(path.join(work, 'running')));
});
