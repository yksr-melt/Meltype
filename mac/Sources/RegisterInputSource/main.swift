// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// install.sh / build.sh がアプリを入れ替えたあとに呼ぶ登録補助ツール (#134)。
// 入れ替え中にバンドルが無い一瞬の走査で、macOS が入力ソースの登録を消すことがある。
// 古い配布コピーを整理したあと、本体の --register-input-source に登録・設定修復を一度だけ任せる。
// 本体と別の実行ファイルにして、引数を知らない旧版が IME として起動したままになるのを防ぐ。
// 本体側は有効かつ選択可能な日本語モードが1件になるまで待ち、.ready の場合だけ表示を更新する。
// 見えないまま TextInputMenuAgent を再起動すると、メニューが3項目だけになるため待機が必要。
import Foundation

let appPath = CommandLine.arguments.count >= 2 ? CommandLine.arguments[1] : Bundle.main.bundlePath
let appURL = URL(fileURLWithPath: appPath).standardizedFileURL.resolvingSymlinksInPath()
guard Bundle(url: appURL)?.bundleIdentifier == BundleCleanup.identifier else {
    FileHandle.standardError.write(Data("Meltype.app が見つかりません: \(appPath)\n".utf8))
    exit(1)
}
do {
    BundleCleanup.unregisterCopies(of: appURL)
    let registration = Process()
    registration.executableURL = appURL.appendingPathComponent("Contents/MacOS/Meltype")
    registration.arguments = ["--register-input-source"]
    try registration.run()
    registration.waitUntilExit()
    exit(registration.terminationStatus)
} catch {
    FileHandle.standardError.write(Data("入力ソースを登録できませんでした: \(error.localizedDescription)\n".utf8))
    exit(1)
}
