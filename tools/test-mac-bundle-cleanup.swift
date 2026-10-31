// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 joyamazaki
// Compile with mac/Sources/RegisterInputSource/BundleCleanup.swift; never changes registration.
import Foundation

@main
struct BundleCleanupTests {
    static func main() throws {
        let userRoot = URL(fileURLWithPath: "/Users/test/Library/Input Methods")
        let systemRoot = URL(fileURLWithPath: "/Library/Input Methods")
        let installed = userRoot.appendingPathComponent("Meltype.app")
        let copies = (0..<15).map { URL(fileURLWithPath: "/Users/test/Downloads/Version \($0)/Meltype.app") }
        func plan(_ urls: [URL]) -> [URL] {
            BundleCleanup.candidates(registered: urls, installed: installed,
                installedDirectories: [userRoot, systemRoot], bundleIdentifier: { _ in BundleCleanup.identifier })
        }
        precondition(plan([installed] + copies) == copies)
        precondition(plan([installed, installed] + copies + copies) == copies)
        precondition(plan([installed]).isEmpty)
        precondition(plan([systemRoot.appendingPathComponent("Meltype.app")]).isEmpty)
        precondition(plan([URL(string: "https://example.com/Meltype.app")!]).isEmpty)
        let foreign = URL(fileURLWithPath: "/Users/test/Downloads/Reused.app")
        precondition(BundleCleanup.candidates(registered: [foreign], installed: installed,
            installedDirectories: [], bundleIdentifier: { _ in "another.app" }).isEmpty)
        let missing = URL(fileURLWithPath: "/nonexistent/old/Meltype.app")
        precondition(BundleCleanup.candidates(registered: [missing], installed: installed,
            installedDirectories: [], bundleIdentifier: { _ in nil }) == [missing])
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let app = dir.appendingPathComponent("installed.app")
        try FileManager.default.createDirectory(at: app, withIntermediateDirectories: true)
        let link = dir.appendingPathComponent("alias.app")
        try FileManager.default.createSymbolicLink(at: link, withDestinationURL: app)
        precondition(BundleCleanup.candidates(registered: [link], installed: app,
            installedDirectories: [], bundleIdentifier: { _ in BundleCleanup.identifier }).isEmpty)
        print("PASS: 8 bundle cleanup cases (15 copies, canonical target, duplicates, system install, URL type, foreign bundle, stale path, symlink)")
    }
}
