// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import AppKit
import Carbon
import Darwin

private struct Failure: LocalizedError {
    let message: String
    var errorDescription: String? { message }
    init(_ message: String) { self.message = message }
}

// 更新が失敗した場合にも、更新前の起動状態と入力ソースへ戻すための記録。
private struct InputState: Codable {
    let schema: Int
    let appPath: String
    let bundleID: String
    let originalSourceID: String
    let temporarySourceID: String?
    let wasRunning: Bool
}

private func property(_ source: TISInputSource, _ key: CFString) -> Any? {
    guard let pointer = TISGetInputSourceProperty(source, key) else { return nil }
    return Unmanaged<AnyObject>.fromOpaque(pointer).takeUnretainedValue()
}

private func sourceID(_ source: TISInputSource) throws -> String {
    guard let value = property(source, kTISPropertyInputSourceID) as? String else {
        throw Failure("Input source has no identifier")
    }
    return value
}

private func currentSourceID() throws -> String {
    guard let source = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue() else {
        throw Failure("Cannot read the current input source")
    }
    return try sourceID(source)
}

private func select(_ source: TISInputSource) throws {
    let status = TISSelectInputSource(source)
    guard status == noErr else { throw Failure("Cannot select input source (OSStatus \(status))") }
    guard try currentSourceID() == sourceID(source) else { throw Failure("Input source selection did not change") }
}

private func matchingApplications(_ url: URL, bundleID: String) -> [NSRunningApplication] {
    NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).filter {
        $0.processIdentifier != getpid() && $0.bundleURL?.resolvingSymlinksInPath() == url && !$0.isTerminated
    }
}

func stopInputMethod(at requestedURL: URL, reference: URL, stateFile: URL?, manualJob: String?) throws {
    let url = requestedURL.resolvingSymlinksInPath()
    guard let id = Bundle(url: reference)?.bundleIdentifier else { throw Failure("Reference bundle has no identifier") }
    if FileManager.default.fileExists(atPath: url.path), Bundle(url: url)?.bundleIdentifier != id {
        throw Failure("Installed bundle identifier differs from the replacement")
    }
    // 同じ識別子の別コピーが動いている場合は、更新対象を曖昧にせず中止する。
    let otherCopies = NSRunningApplication.runningApplications(withBundleIdentifier: id).filter {
        $0.processIdentifier != getpid() && !$0.isTerminated && $0.bundleURL?.resolvingSymlinksInPath() != url
    }
    guard otherCopies.isEmpty else { throw Failure("Another copy of the input method is running at a different location") }
    let applications = matchingApplications(url, bundleID: id)
    let original = try currentSourceID()
    var temporary: TISInputSource?
    if original == id || original.hasPrefix(id + ".") {
        guard let ascii = TISCopyCurrentASCIICapableKeyboardLayoutInputSource()?.takeRetainedValue(),
              property(ascii, kTISPropertyInputSourceIsSelectCapable) as? Bool == true,
              try sourceID(ascii) != original else { throw Failure("No selectable ASCII layout for input-source handoff") }
        temporary = ascii
    }
    let state = try InputState(schema: 1, appPath: url.path, bundleID: id, originalSourceID: original,
                               temporarySourceID: temporary.map { try sourceID($0) }, wasRunning: !applications.isEmpty)
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.sortedKeys]
    // 切り替えや終了に失敗しても復旧できるよう、操作前に状態を保存する。
    if let stateFile { try encoder.encode(state).write(to: stateFile, options: .atomic) }
    if let temporary { try select(temporary) }
    // launchctl submit で起動した手動ジョブは、プロセスだけを終了すると起動し直す。
    // 入力状態の保存と退避を済ませてから、再起動元を解除する。
    if let manualJob {
        let remove = Process()
        remove.executableURL = URL(fileURLWithPath: "/bin/launchctl")
        remove.arguments = ["remove", manualJob]
        try remove.run()
        remove.waitUntilExit()
        guard remove.terminationStatus == 0 else { throw Failure("Cannot remove manual launch job: \(manualJob)") }
    }
    let remaining = matchingApplications(url, bundleID: id)
    // 強制終了には切り替えず、通常終了できなければアプリの交換を中止する。
    // ジョブ解除で既に終了処理中の場合も、下の待機で実際の終了を確認する。
    for application in remaining where !application.isTerminated {
        guard application.terminate() || application.isTerminated || manualJob != nil else {
            throw Failure("Normal termination was refused by process \(application.processIdentifier)")
        }
    }
    let deadline = ProcessInfo.processInfo.systemUptime + 10
    func stopped() -> Bool {
        applications.allSatisfy(\.isTerminated) && remaining.allSatisfy(\.isTerminated)
            && matchingApplications(url, bundleID: id).isEmpty
    }
    while !stopped(), ProcessInfo.processInfo.systemUptime < deadline {
        RunLoop.current.run(until: Date().addingTimeInterval(0.05))
    }
    guard stopped() else {
        throw Failure("Input method is still running; the bundle was not replaced")
    }
}

// 起動完了のコールバックと待機側が共有する結果は、すべてロックを介して読み書きする。
private final class LaunchResult: @unchecked Sendable {
    private let lock = NSLock()
    private var result: (NSRunningApplication?, Error?)?
    func complete(_ app: NSRunningApplication?, _ error: Error?) {
        lock.lock()
        defer { lock.unlock() }
        result = (app, error)
    }
    func value() -> (NSRunningApplication?, Error?)? {
        lock.lock()
        defer { lock.unlock() }
        return result
    }
}

func startInputMethod(at requestedURL: URL) throws {
    let url = requestedURL.resolvingSymlinksInPath()
    guard let bundle = Bundle(url: url), let id = bundle.bundleIdentifier, let executable = bundle.executableURL else {
        throw Failure("Input method bundle is invalid: \(url.path)")
    }
    let existing = matchingApplications(url, bundleID: id)
    guard existing.count <= 1 else { throw Failure("Multiple input-method applications are running for the installed bundle") }
    let deadline = ProcessInfo.processInfo.systemUptime + 10
    let application: NSRunningApplication
    if let running = existing.first {
        application = running
    } else {
        let configuration = NSWorkspace.OpenConfiguration()
        // 入力先のフォーカスを維持し、候補 UI を持つアプリ自体は隠さない。
        configuration.activates = false
        configuration.hides = false
        configuration.addsToRecentItems = false
        configuration.createsNewApplicationInstance = false
        configuration.allowsRunningApplicationSubstitution = false
        let result = LaunchResult()
        NSWorkspace.shared.openApplication(at: url, configuration: configuration) { result.complete($0, $1) }
        while result.value() == nil, ProcessInfo.processInfo.systemUptime < deadline {
            RunLoop.current.run(until: Date().addingTimeInterval(0.05))
        }
        guard let (launched, error) = result.value() else { throw Failure("LaunchServices startup timed out") }
        if let error { throw error }
        guard let launched else { throw Failure("LaunchServices did not return a running application") }
        application = launched
    }
    guard application.bundleURL?.resolvingSymlinksInPath() == url,
          application.executableURL?.resolvingSymlinksInPath() == executable.resolvingSymlinksInPath() else {
        throw Failure("LaunchServices did not start the requested bundle")
    }
    while !application.isFinishedLaunching && !application.isTerminated, ProcessInfo.processInfo.systemUptime < deadline {
        RunLoop.current.run(until: Date().addingTimeInterval(0.05))
    }
    guard application.isFinishedLaunching && !application.isTerminated,
          matchingApplications(url, bundleID: id).contains(where: { $0.processIdentifier == application.processIdentifier }) else {
        throw Failure("Input method did not finish launching")
    }
    print("Input method application finished launching (pid \(application.processIdentifier)).")
}

func restoreInputMethod(at requestedURL: URL, stateFile: URL) throws {
    let state = try JSONDecoder().decode(InputState.self, from: Data(contentsOf: stateFile))
    let url = requestedURL.resolvingSymlinksInPath()
    guard state.schema == 1, state.appPath == url.path, Bundle(url: url)?.bundleIdentifier == state.bundleID else {
        throw Failure("Saved input state does not match the installed bundle")
    }
    if state.temporarySourceID != nil {
        let status = TISRegisterInputSource(url as CFURL)
        guard status == noErr else { throw Failure("Cannot register restored input method (OSStatus \(status))") }
    }
    if state.wasRunning || state.temporarySourceID != nil { try startInputMethod(at: url) }
    do {
        // 更新中に利用者が別の入力ソースへ切り替えていた場合は、その選択を優先する。
        if let temporary = state.temporarySourceID, try currentSourceID() == temporary {
            let sources = TISCreateInputSourceList(nil, false).takeRetainedValue() as! [TISInputSource]
            guard let source = sources.first(where: { (property($0, kTISPropertyInputSourceID) as? String) == state.originalSourceID }) else {
                throw Failure("Original input source is no longer available")
            }
            try select(source)
        }
    } catch {
        throw InputSelectionFailure(message: error.localizedDescription)
    }
}

// 登録・起動の失敗と区別し、選択だけの失敗は入力メニューからの選び直しを案内する。
struct InputSelectionFailure: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

func validateBundleLayout(at root: URL) throws {
    let names = try FileManager.default.contentsOfDirectory(atPath: root.path)
    var contents = stat()
    // 交換対象は Contents だけなので、それ以外の内容を取り残さないよう検査する。
    // Finder が作る通常ファイルの .DS_Store だけは許可する。
    let unexpected = names.filter { $0 != "Contents" && $0 != ".DS_Store" }
    guard unexpected.isEmpty else {
        throw Failure("Unsupported application root entries (\(unexpected.joined(separator: ", "))): \(root.path). Move these entries outside the app, then retry with the complete distribution ZIP.")
    }
    if names.contains(".DS_Store") {
        var metadata = stat()
        guard lstat(root.appendingPathComponent(".DS_Store").path, &metadata) == 0,
              metadata.st_mode & S_IFMT == S_IFREG else {
            throw Failure(".DS_Store must be a regular file. Move it outside the app and retry: \(root.path)")
        }
    }
    guard lstat(root.appendingPathComponent("Contents").path, &contents) == 0,
          contents.st_mode & S_IFMT == S_IFDIR else {
        throw Failure("Contents must be a real directory: \(root.path)")
    }
    guard let bundle = Bundle(url: root), let identifier = bundle.bundleIdentifier, !identifier.isEmpty,
          bundle.object(forInfoDictionaryKey: "CFBundlePackageType") as? String == "APPL" else {
        throw Failure("Application bundle metadata is invalid: \(root.path)")
    }
}
