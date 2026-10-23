// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Keep only the installed copy registered, then use the IME's single registration/repair path.
import Foundation

let appPath = CommandLine.arguments.count >= 2 ? CommandLine.arguments[1] : Bundle.main.bundlePath
let appURL = URL(fileURLWithPath: appPath).standardizedFileURL.resolvingSymlinksInPath()
guard Bundle(url: appURL)?.bundleIdentifier == BundleCleanup.identifier else {
    FileHandle.standardError.write(Data("Meltype.app が見つかりません: \(appPath)\n".utf8))
    exit(1)
}
do {
    try BundleCleanup.unregisterCopies(of: appURL)
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
