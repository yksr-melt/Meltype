// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// 公開版のリリースから、パッケージマネージャーに出すマニフェストを作る (winget・Scoop・Homebrew)。
//   node tools/make-manifests.mjs <版> [Windows の zip] [Mac の zip] [Windows のインストーラー (setup.exe)]
//   例: node tools/make-manifests.mjs 1.2.0 dist/Meltype-1.2.0-windows.zip dist/Meltype-1.2.0-mac.zip dist/Meltype-1.2.0-setup.exe
//   Homebrew だけ: node tools/make-manifests.mjs 1.2.0 dist/Meltype-1.2.0-mac.zip (mac.yml がタグのときに作る)
// ファイルは名前で見分ける (-mac.zip は Mac、ほかの .zip は Windows、-setup.exe はインストーラー)。
// zip・setup.exe は GitHub のリリース (v<版>) に同じ名前で上げたもの。SHA-256 をここで計算する。
// winget はインストーラー (setup.exe) があればそれを使う (Meltype IME・起動時の起動・Meltype の自動更新と同じ場所に入る: issue #367)。
// できたものは dist/manifests/ に置く。出し方は docs/RELEASE.md。
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const [version, ...files] = process.argv.slice(2);
const windowsSetup = files.find(file => file.endsWith('-setup.exe'));
const macZip = files.find(file => file.endsWith('-mac.zip'));
const windowsZip = files.find(file => file.endsWith('.zip') && file !== macZip);
if (!version || (!windowsZip && !macZip)) {
  console.error('使い方: node tools/make-manifests.mjs <版> [Windows の zip] [Mac の zip] [Windows の setup.exe]');
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

if (windowsZip) {
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
}

// --- Homebrew Cask (このリポジトリを URL 指定の tap として使う) ---
// input_method で ~/Library/Input Methods/Meltype.app に入れ、postflight で mac/install.sh と同じこと
// (隔離属性を外す・入力ソースに登録・LaunchServices に登録・GUI のセッションで起動) をする。
// start-input-method.sh は zip の中 (Meltype-mac/) のものを使う (Homebrew は Meltype.app だけを動かし、ほかは staged_path に残す)。
if (macZip) {
  const macName = path.basename(macZip);
  write('homebrew/Casks/meltype.rb', `cask "meltype" do
  version "${version}"
  sha256 "${sha256(macZip)}"

  url "${release}/${macName.replace(version, '#{version}')}"
  name "Meltype"
  desc "${description}"
  homepage "${repo}"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on macos: ">= :ventura"
  depends_on arch: :arm64

  preflight do
    payload = "#{staged_path}/Meltype-mac/Meltype.app.zip"
    if File.exist?(payload)
      system_command "/usr/bin/ditto", args: ["-x", "-k", payload, "#{staged_path}/Meltype-mac"], must_succeed: true
    end
  end

  input_method "Meltype-mac/Meltype.app"

  postflight do
    app = "#{Dir.home}/Library/Input Methods/Meltype.app"
    # 署名が自分用 (ad hoc) の版もあるので、隔離属性を外さないと macOS が起動させない (install.sh と同じ)
    system_command "/usr/bin/xattr", args: ["-dr", "com.apple.quarantine", app], must_succeed: false
    # 入力ソースに登録する (#21、#134)
    register = "#{app}/Contents/MacOS/MeltypeRegisterInputSource"
    if File.executable?(register)
      system_command register, args: [app], must_succeed: false
    else
      system_command "#{app}/Contents/MacOS/Meltype", args: ["--register-input-source"], must_succeed: false
    end
    system_command "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister",
                   args: ["-f", app], must_succeed: false
    system_command "/bin/bash", args: ["#{staged_path}/Meltype-mac/start-input-method.sh", app], must_succeed: false
  end

  uninstall launchctl: "io.github.yksr-melt.Meltype.manual",
            quit:      "io.github.yksr-melt.inputmethod.Meltype"

  zap trash: [
    "~/Library/Application Support/Meltype",
    "~/Library/Logs/Meltype.stderr.log",
    "~/Library/Logs/Meltype.stdout.log",
  ]

  caveats <<~EOS
    初めてのときは、システム設定 → キーボード → 入力ソース →「編集…」→「+」→ 日本語 → Meltype を追加してください
    (出てこないときは、ログアウトしてログインし直してください)。
    設定と学習データも消すときは brew uninstall --zap --cask meltype です。
    Meltype for Mac はプレビュー版です。
  EOS
end
`);
}
