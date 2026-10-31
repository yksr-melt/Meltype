// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 joyamazaki

import AppKit

/// The source picker discovers registered app copies, independently of enabled-source preferences.
enum BundleCleanup {
    static let identifier = "io.github.yksr-melt.inputmethod.Meltype"

    static func candidates(registered: [URL], installed: URL, installedDirectories: [URL],
                           bundleIdentifier: (URL) -> String? = { Bundle(url: $0)?.bundleIdentifier }) -> [URL] {
        let target = installed.standardizedFileURL.resolvingSymlinksInPath()
        let roots = installedDirectories.map { $0.standardizedFileURL.resolvingSymlinksInPath() }
        var seen = Set<URL>()
        return registered.compactMap { url in
            guard url.isFileURL else { return nil }
            let path = url.standardizedFileURL.resolvingSymlinksInPath()
            guard path != target, seen.insert(path).inserted,
                  !roots.contains(path.deletingLastPathComponent()) else { return nil }
            // The lookup was by our exact bundle ID. Do not unregister a path reused by another app.
            if let actual = bundleIdentifier(path), actual != identifier { return nil }
            return path
        }
    }

    static func unregisterCopies(of installed: URL) {
        let roots = [FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Input Methods"),
                     URL(fileURLWithPath: "/Library/Input Methods"), URL(fileURLWithPath: "/System/Library/Input Methods")]
        let registered = NSWorkspace.shared.urlsForApplications(withBundleIdentifier: identifier)
        for url in candidates(registered: registered, installed: installed, installedDirectories: roots) {
            let process = Process()
            process.executableURL = URL(fileURLWithPath:
                "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister")
            process.arguments = ["-u", url.path]
            // 他ユーザーのコピーなど、整理できないパスがあっても残りと本体の登録を続ける。
            do {
                try process.run()
                process.waitUntilExit()
                guard process.terminationStatus == 0 else {
                    throw NSError(domain: "MeltypeBundleCleanup", code: Int(process.terminationStatus),
                                  userInfo: [NSLocalizedDescriptionKey: "登録解除に失敗しました (終了コード \(process.terminationStatus))"])
                }
                print("入力ソースの候補からコピーの登録を外しました（ファイルは保持）: \(url.path)")
            } catch {
                FileHandle.standardError.write(Data(
                    "古いコピーの登録を解除できませんでした（本体の登録は続けます）: \(url.path): \(error.localizedDescription)\n".utf8))
            }
        }
    }
}
