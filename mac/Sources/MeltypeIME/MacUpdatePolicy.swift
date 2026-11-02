// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 joyamazaki

import Foundation

struct MacUpdatePolicy {
    static let feedURL = "https://github.com/yksr-melt/Meltype/releases/latest/download/appcast.xml"

    static func isConfigured(feed: String?, publicKey: String?) -> Bool {
        guard let feed, let url = URL(string: feed), url.scheme == "https", url.host != nil,
              url.user == nil, url.password == nil,
              let publicKey, Data(base64Encoded: publicKey)?.count == 32 else { return false }
        return true
    }

    static func isInstalled(_ app: URL, home: URL = FileManager.default.homeDirectoryForCurrentUser) -> Bool {
        let parent = app.standardizedFileURL.resolvingSymlinksInPath().deletingLastPathComponent()
        return [home.appendingPathComponent("Library/Input Methods"), URL(fileURLWithPath: "/Library/Input Methods")]
            .map { $0.standardizedFileURL.resolvingSymlinksInPath().path }.contains(parent.path)
    }
}

/// 未確定文字の確定を待つ。30秒で打ち切り、Sparkleに今回の更新を中止させる。
final class UpdateRelaunchGate {
    static let timeout: TimeInterval = 30
    private var continuation: (() -> Void)?
    private var deadline: TimeInterval?
    private(set) var didExpire = false
    var isPending: Bool { continuation != nil }

    func postpone(ready: Bool, now: TimeInterval = ProcessInfo.processInfo.systemUptime,
                  continuation: @escaping () -> Void) -> Bool {
        cancel()
        guard !ready else { return false }
        deadline = now + Self.timeout
        self.continuation = continuation
        return true
    }

    func resumeIfReady(_ ready: Bool, now: TimeInterval = ProcessInfo.processInfo.systemUptime) {
        guard let resume = continuation, let deadline else { return }
        didExpire = now >= deadline
        guard ready || didExpire else { return }
        continuation = nil
        self.deadline = nil
        // 期限切れでも継続を一度返す。Sparkleは再開時に再起動の可否を再確認するため、
        // updaterShouldRelaunchApplicationで拒否すればインストール自体が中止される。
        resume()
    }

    func cancel() {
        continuation = nil
        deadline = nil
        didExpire = false
    }
}
