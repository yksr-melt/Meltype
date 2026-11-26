// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ieyoukan

import AppKit
import Darwin

@MainActor
private final class Installer: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private let window = NSWindow(
        // 待っている画面では、許可と手動での追加の案内を折り返して表示する。
        contentRect: NSRect(x: 0, y: 0, width: 480, height: 240),
        styleMask: [.titled, .closable], backing: .buffered, defer: false
    )
    private let status = NSTextField(wrappingLabelWithString: """
        この Mac のアカウントに入力ソースと設定アプリを追加します。
        途中で macOS が Meltype の有効化を確認したら、「許可」を押してください。
        """)
    private let progress = NSProgressIndicator()
    private let keyboard = NSButton(title: "キーボード設定…", target: nil, action: nil)
    private let primary = NSButton(title: "インストール", target: nil, action: nil)
    private let close = NSButton(title: "閉じる", target: nil, action: nil)
    private var isWorking = false
    // 取り出す DMG のマウント先。インストールが完了し、自分が入っているディスクイメージを確かに特定できたときだけ入る。
    private var volumeToEject: String?
    private let installedApp = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Input Methods/Meltype.app", isDirectory: true)

    func applicationDidFinishLaunching(_ notification: Notification) {
        window.title = "Meltype インストーラー"
        window.delegate = self
        window.isReleasedWhenClosed = false

        let heading = NSTextField(labelWithString: "Meltype をインストール")
        heading.font = .systemFont(ofSize: 22, weight: .semibold)
        status.font = .systemFont(ofSize: 14)
        progress.style = .spinning
        progress.controlSize = .small
        progress.isDisplayedWhenStopped = false
        progress.setAccessibilityLabel("インストール処理の進行状況")
        keyboard.target = self
        keyboard.action = #selector(openKeyboardSettings)
        keyboard.isHidden = true
        primary.target = self
        primary.action = #selector(install)
        primary.keyEquivalent = "\r"
        close.target = self
        close.action = #selector(finish)

        let footer = NSStackView(views: [progress, keyboard, close, primary])
        footer.alignment = .centerY
        footer.spacing = 10
        let content = NSView()
        for view in [heading, status, footer] {
            view.translatesAutoresizingMaskIntoConstraints = false
            content.addSubview(view)
        }
        window.contentView = content
        NSLayoutConstraint.activate([
            heading.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 24),
            heading.topAnchor.constraint(equalTo: content.topAnchor, constant: 24),
            status.leadingAnchor.constraint(equalTo: heading.leadingAnchor),
            status.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -24),
            status.topAnchor.constraint(equalTo: heading.bottomAnchor, constant: 12),
            // 案内が長いときはボタンに重ねず、ウィンドウを縦に伸ばす。
            status.bottomAnchor.constraint(lessThanOrEqualTo: footer.topAnchor, constant: -16),
            footer.trailingAnchor.constraint(equalTo: status.trailingAnchor),
            footer.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -20),
        ])
        window.center()
        window.makeKeyAndOrderFront(nil)
        activate()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func windowShouldClose(_ sender: NSWindow) -> Bool { !isWorking }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        isWorking ? .terminateCancel : .terminateNow
    }

    @objc private func finish() {
        if let volumeToEject { ejectAfterExit(volumeToEject) }
        window.performClose(nil)
    }

    @objc private func openKeyboardSettings() {
        let settings = URL(string: "x-apple.systempreferences:com.apple.Keyboard-Settings.extension")!
        NSWorkspace.shared.open(settings)
    }

    @objc private func install() {
        // アプリのバンドルには必ず Resources があり、無ければ壊れた DMG なので止める。
        let script = Bundle.main.resourceURL!.appendingPathComponent("Meltype-mac/install.sh")
        status.stringValue = "Meltype を配置して入力ソースに登録しています…"
        isWorking = true
        primary.isEnabled = false
        close.isEnabled = false
        progress.startAnimation(nil)
        launch(URL(fileURLWithPath: "/bin/bash"), [script.path]) { exitCode, details in
            self.isWorking = false
            self.progress.stopAnimation(nil)
            self.primary.isEnabled = true
            self.close.isEnabled = true
            guard exitCode == 0 else {
                self.status.stringValue = "インストールを完了できませんでした。"
                self.showDetails(details)
                return
            }
            self.primary.isHidden = true
            self.close.keyEquivalent = "\r"
            self.checkEnabled(waited: false)
        }
    }

    // 有効化の確認はシステム設定がこの画面より手前に出し、許可されるまで install.sh は待たない。
    // この画面のプロセスからは有効な入力ソースの一覧が古いままなので、確かめるたびに補助ツールを新しく起動する。
    private func checkEnabled(waited: Bool) {
        launch(installedApp.appendingPathComponent("Contents/MacOS/MeltypeInstallSupport"), ["--enabled", installedApp.path]) { exitCode, details in
            switch exitCode {
            case 0:
                self.complete(waited: waited)
            case 2:
                if !waited { self.showWaiting() }
                // 補助ツールは毎回すぐ終わるので、1 秒ごとに確かめても負担にならない。閉じればアプリごと終わる。
                DispatchQueue.main.asyncAfter(deadline: .now() + 1) { self.checkEnabled(waited: true) }
            default:
                self.status.stringValue = "Meltype が有効になったか確かめられませんでした。入力メニューから Meltype を選んでください。"
                self.showDetails(details)
            }
        }
    }

    private func showWaiting() {
        status.stringValue = """
            システム設定に出た確認で「許可」を押してください。許可すると、この画面でお知らせします。
            確認が出ないときや「許可しない」を押したときは、「キーボード設定…」の入力ソース「編集…」→「+」→ 日本語 → Meltype で追加してください。
            """
        keyboard.isHidden = false
        progress.startAnimation(nil)
    }

    private func complete(waited: Bool) {
        progress.stopAnimation(nil)
        keyboard.isHidden = true
        status.stringValue = """
            Meltype を使えるようになりました。メニューバーの入力メニューが Meltype になっていれば、そのまま使えます。
            なっていないときは、入力メニューから Meltype を選んでください。
            """
        volumeToEject = diskImageVolume()
        if volumeToEject != nil { close.title = "取り出して閉じる" }
        guard waited else { return }
        // 許可のあとで macOS が Meltype に切り替えるとは限らないので、待っていた初回だけ一度選ぶ。選べなくても案内で対処できる。
        launch(installedApp.appendingPathComponent("Contents/MacOS/MeltypeInstallSupport"), ["--select", installedApp.path]) { _, _ in }
        // 許可を押した直後はシステム設定が前面にあり、macOS 14 以降の activate() だけでは前に出ないことがある。
        window.orderFrontRegardless()
        NSApp.requestUserAttention(.informationalRequest)
        activate()
    }

    private func activate() {
        if #available(macOS 14, *) {
            NSApp.activate()
        } else {
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    private func launch(_ executable: URL, _ arguments: [String], completion: @escaping (Int32?, String) -> Void) {
        DispatchQueue.global(qos: .userInitiated).async {
            let process = Process()
            let output = Pipe()
            process.executableURL = executable
            process.arguments = arguments
            process.standardOutput = output
            process.standardError = output
            let result: (Int32?, String)
            do {
                try process.run()
                let data = output.fileHandleForReading.readDataToEndOfFile()
                process.waitUntilExit()
                result = (process.terminationStatus, String(decoding: data, as: UTF8.self))
            } catch {
                result = (nil, error.localizedDescription)
            }
            DispatchQueue.main.async { completion(result.0, result.1) }
        }
    }

    private func showDetails(_ details: String) {
        let alert = NSAlert()
        alert.messageText = "Meltype のインストール"
        alert.informativeText = details.split(whereSeparator: \.isNewline).suffix(5).joined(separator: "\n")
        alert.addButton(withTitle: "OK")
        alert.beginSheetModal(for: window)
    }
}

private func mountInfo(_ path: String) -> (on: String, from: String, type: String)? {
    var info = statfs()
    guard statfs(path, &info) == 0 else { return nil }
    func text<T>(_ field: T) -> String {
        withUnsafeBytes(of: field) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
    }
    return (text(info.f_mntonname), text(info.f_mntfromname), text(info.f_fstypename))
}

// このインストーラーが入っているディスクイメージのマウント先。特定できなければ取り出さない (nil)。
private func diskImageVolume() -> String? {
    guard var mount = mountInfo(Bundle.main.bundlePath) else { return nil }
    // App Translocation では、元のフォルダーを nullfs で別の場所に見せて動かすので、元のフォルダーからたどる。
    if mount.type == "nullfs", let original = mountInfo(mount.from) { mount = original }
    // ディスクイメージのマウント先と一致するときだけにし、USB などへコピーして開いたときは取り出さない。
    let hdiutil = Process()
    let output = Pipe()
    hdiutil.executableURL = URL(fileURLWithPath: "/usr/bin/hdiutil")
    hdiutil.arguments = ["info", "-plist"]
    hdiutil.standardOutput = output
    hdiutil.standardError = FileHandle.nullDevice
    guard (try? hdiutil.run()) != nil else { return nil }
    let data = output.fileHandleForReading.readDataToEndOfFile()
    hdiutil.waitUntilExit()
    guard let info = try? PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any],
          let images = info["images"] as? [[String: Any]] else { return nil }
    let mountPoints = images.flatMap { $0["system-entities"] as? [[String: Any]] ?? [] }.compactMap { $0["mount-point"] as? String }
    return mountPoints.contains(mount.on) ? mount.on : nil
}

// 動いている間は DMG を取り出せないので、終わってから取り出す。子プロセスはアプリが終わっても残る。
// 出力先を捨てておかないと、アプリが終わったあとに hdiutil が書き込んだ時点で止まる。
private func ejectAfterExit(_ volume: String) {
    let eject = Process()
    eject.executableURL = URL(fileURLWithPath: "/bin/sh")
    // Translocation のマウントが外れるまで使用中になることがあるので、1 秒ごとに 10 回まで試す。
    eject.arguments = ["-c", "for _ in 1 2 3 4 5 6 7 8 9 10; do sleep 1; /usr/bin/hdiutil detach -quiet \"$1\" && exit 0; done", "sh", volume]
    eject.standardOutput = FileHandle.nullDevice
    eject.standardError = FileHandle.nullDevice
    try? eject.run()
}

@main
private enum InstallerApp {
    @MainActor
    static func main() {
        let app = NSApplication.shared
        let installer = Installer()
        app.setActivationPolicy(.regular)
        app.delegate = installer
        withExtendedLifetime(installer) { app.run() }
    }
}
