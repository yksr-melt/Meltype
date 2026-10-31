// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 joyamazaki
import Foundation
@main struct Tests {
    static func main() {
        let key = Data(repeating: 1, count: 32).base64EncodedString()
        assert(MacUpdatePolicy.isConfigured(feed: MacUpdatePolicy.feedURL, publicKey: key))
        for feed in [nil, "http://example.com/feed", "https://user:pass@example.com/feed", "file:///tmp/feed"] as [String?] {
            assert(!MacUpdatePolicy.isConfigured(feed: feed, publicKey: key))
        }
        for bad in [nil, "", "invalid", Data(repeating: 0, count: 31).base64EncodedString()] as [String?] {
            assert(!MacUpdatePolicy.isConfigured(feed: MacUpdatePolicy.feedURL, publicKey: bad))
        }
        let home = URL(fileURLWithPath: "/tmp/test-home")
        assert(MacUpdatePolicy.isInstalled(home.appendingPathComponent("Library/Input Methods/Meltype.app"), home: home))
        assert(MacUpdatePolicy.isInstalled(URL(fileURLWithPath: "/Library/Input Methods/Meltype.app"), home: home))
        assert(!MacUpdatePolicy.isInstalled(home.appendingPathComponent("Downloads/Meltype.app"), home: home))
        let gate = UpdateRelaunchGate()
        var count = 0
        assert(!gate.postpone(ready: true) { count += 1 })
        assert(count == 0 && !gate.isPending)
        assert(gate.postpone(ready: false) { count += 1; gate.resumeIfReady(true) })
        gate.resumeIfReady(false)
        assert(count == 0 && gate.isPending)
        gate.resumeIfReady(true)
        gate.resumeIfReady(true)
        assert(count == 1 && !gate.isPending)
        assert(gate.postpone(ready: false) { count += 1 })
        gate.cancel()
        gate.resumeIfReady(true)
        assert(count == 1 && !gate.isPending)
        // 確定できない場合でも期限で継続を一度返し、更新拒否状態を保持する。
        assert(gate.postpone(ready: false, now: 100) { count += 1 })
        gate.resumeIfReady(false, now: 129.9)
        assert(count == 1 && gate.isPending && !gate.didExpire)
        gate.resumeIfReady(false, now: 130)
        assert(count == 2 && !gate.isPending && gate.didExpire)
        gate.resumeIfReady(true, now: 131)
        assert(count == 2 && gate.didExpire)
        gate.cancel()
        assert(!gate.didExpire && !gate.isPending)
        // 次の更新は前回の期限切れを引きずらず、確定後に再開できる。
        assert(gate.postpone(ready: false, now: 200) { count += 1 })
        gate.resumeIfReady(true, now: 201)
        assert(count == 3 && !gate.didExpire && !gate.isPending)
        print("更新ポリシー・待機・期限切れ・再試行のテストに成功しました")
    }
}
