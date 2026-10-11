// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// 公開版のリリースから、パッケージマネージャーに出すマニフェストを作る (winget・Scoop・Homebrew)。
//   node tools/make-manifests.mjs <版> <Windows の zip> [Mac の zip] [Windows のインストーラー (setup.exe)]
//   例: node tools/make-manifests.mjs 1.2.0 dist/Meltype-1.2.0-windows.zip dist/Meltype-1.2.0-mac.zip dist/Meltype-1.2.0-setup.exe
// zip・setup.exe は GitHub のリリース (v<版>) に同じ名前で上げたもの。SHA-256 をここで計算する。
// winget はインストーラー (setup.exe) があればそれを使う (Meltype IME・起動時の起動・Meltype の自動更新と同じ場所に入る: issue #367)。
// できたものは dist/manifests/ に置く。出し方は docs/RELEASE.md。
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const [version, windowsZip, ...rest] = process.argv.slice(2);
const windowsSetup = rest.find(file => file.endsWith('-setup.exe'));
const macZip = rest.find(file => file.endsWith('.zip'));
if (!version || !windowsZip) {
  console.error('使い方: node tools/make-manifests.mjs <版> <Windows の zip> [Mac の zip] [Windows の setup.exe]');
  process.exit(1);
}
const repo = 'https://github.com/yksr-melt/Meltype';
const release = `${repo}/releases/download/v${version}`;
const sha256 = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const out = path.join('dist', 'manifests');
const description = '半角/全角を押さずに、日本語と英語をそのまま打てる日本語入力 (英単語は英字のまま、ローマ字は日本語に)';
const write = (file, text) => {
  fs.mkdirSync(path.dirname(path.join(out, file)), { recursive: true });
  fs.writeFileSync(path.join(out, file), text);
  console.log('作成しました:', path.join(out, file));
};

// --- Scoop (自分のバケット yksr-melt/scoop-bucket に置く想定) ---
const windowsName = path.basename(windowsZip);
const windowsHash = sha256(windowsZip);
write('scoop/meltype.json', JSON.stringify({
  version,
  description,
  homepage: repo,
  license: 'GPL-3.0-or-later',
  url: `${release}/${windowsName}`,
  hash: windowsHash,
  // zip の中の app フォルダーがアプリ本体 (Install.cmd を使わず、Scoop の場所で動かす)
  extract_dir: 'app',
  shortcuts: [['Meltype.exe', 'Meltype']],
  notes: 'Windows の起動時に起動するには、Meltype のトレイのメニューで「Windows の起動時に起動」を ON にしてください。',
  checkver: { github: repo },
  autoupdate: { url: `${repo}/releases/download/v$version/${windowsName.replace(version, '$version')}` },
}, null, 2) + '\n');

// --- winget (microsoft/winget-pkgs に Pull Request で出す) ---
const id = 'Yukishiro.Meltype';
const wingetDir = `winget/manifests/y/Yukishiro/Meltype/${version}`;
write(`${wingetDir}/${id}.yaml`, `PackageIdentifier: ${id}
PackageVersion: ${version}
DefaultLocale: ja-JP
ManifestType: version
ManifestVersion: 1.6.0
`);
// インストーラー (Inno Setup、このユーザーだけに入れる・管理者権限なし) があればそれを使う。
// 無ければ zip (portable)。portable は Meltype IME・起動時の起動が無く、Meltype の自動更新とも別の場所になる
write(`${wingetDir}/${id}.installer.yaml`, windowsSetup ? `PackageIdentifier: ${id}
PackageVersion: ${version}
InstallerType: inno
Scope: user
InstallModes:
  - interactive
  - silent
  - silentWithProgress
UpgradeBehavior: install
ProductCode: Meltype_is1
Installers:
  - Architecture: x64
    InstallerUrl: ${release}/${path.basename(windowsSetup)}
    InstallerSha256: ${sha256(windowsSetup).toUpperCase()}
ManifestType: installer
ManifestVersion: 1.6.0
` : `PackageIdentifier: ${id}
PackageVersion: ${version}
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: app\\Meltype.exe
    PortableCommandAlias: meltype
Installers:
  - Architecture: x64
    InstallerUrl: ${release}/${windowsName}
    InstallerSha256: ${windowsHash.toUpperCase()}
ManifestType: installer
ManifestVersion: 1.6.0
`);
write(`${wingetDir}/${id}.locale.ja-JP.yaml`, `PackageIdentifier: ${id}
PackageVersion: ${version}
PackageLocale: ja-JP
Publisher: Yukishiro
PublisherUrl: https://github.com/yksr-melt
PackageName: Meltype
PackageUrl: ${repo}
License: GPL-3.0-or-later
LicenseUrl: ${repo}/blob/main/LICENSE
ShortDescription: ${description}
Tags:
  - ime
  - japanese
  - input-method
  - 日本語入力
ManifestType: defaultLocale
ManifestVersion: 1.6.0
`);

// --- Homebrew (自分の tap yksr-melt/homebrew-tap に置く想定) ---
if (macZip) {
  const macName = path.basename(macZip);
  write('homebrew/Casks/meltype.rb', `cask "meltype" do
  version "${version}"
  sha256 "${sha256(macZip)}"

  url "${release}/${macName.replace(version, '#{version}')}"
  name "Meltype"
  desc "${description}"
  homepage "${repo}"

  depends_on macos: ">= :ventura"
  depends_on arch: :arm64

  input_method "Meltype-mac/Meltype.app"

  caveats <<~EOS
    ログアウトしてログインし直してから、システム設定 → キーボード → 入力ソース →「編集…」→「+」→ 日本語 → Meltype を追加してください。
    Meltype for Mac はプレビュー版です。
  EOS
end
`);
}
