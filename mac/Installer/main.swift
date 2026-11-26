// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ieyoukan

import AppKit

@MainActor
private final class Installer: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private let window = NSWindow(
        // 完了画面では、切り替わったかの見方と手動での追加の案内を折り返して表示する。
        contentRect: NSRect(x: 0, y: 0, width: 480, height: 240),
        styleMask: [.titled, .closable], backing: .buffered, defer: false
    )
    private let status = NSTextField(wrappingLabelWithString: "この Mac のアカウントに入力ソースと設定アプリを追加します。")
    private let progress = NSProgressIndicator()
    private let keyboard = NSButton(title: "キーボード設定…", target: nil, action: nil)
    private let primary = NSButton(title: "インストール", target: nil, action: nil)
    private let close = NSButton(title: "閉じる", target: nil, action: nil)
    private var isWorking = false

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
        if #available(macOS 14, *) {
            NSApp.activate()
        } else {
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func windowShouldClose(_ sender: NSWindow) -> Bool { !isWorking }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        isWorking ? .terminateCancel : .terminateNow
    }

    @objc private func finish() { window.performClose(nil) }

    @objc private func openKeyboardSettings() {
        let settings = URL(string: "x-apple.systempreferences:com.apple.Keyboard-Settings.extension")!
        NSWorkspace.shared.open(settings)
    }

    @objc private func install() {
        // アプリのバンドルには必ず Resources があり、無ければ壊れた DMG なので止める。
        let script = Bundle.main.resourceURL!.appendingPathComponent("Meltype-mac/install.sh")
        // 有効化の確認はシステム設定がこの画面より手前に出すので、何の確認かをここで伝えておく。
        status.stringValue = "Meltype を配置して入力ソースに登録しています…\n途中でシステム設定に Meltype の有効化の確認が出たら、「許可」を押してください。"
        run(URL(fileURLWithPath: "/bin/bash"), arguments: [script.path]) { success, details in
            if success {
                // 利用者はこの画面を確認に答えたあとで見る。許可すれば install.sh か macOS が Meltype に切り替えているので、
                // ここでは選び直さず、切り替わったかの見方と、切り替わっていないときの手順だけを示す。
                self.status.stringValue = """
                    インストールしました。メニューバーの入力メニューが Meltype になっていれば、そのまま使えます。
                    なっていないときは、入力メニューから Meltype を選んでください。入力メニューに無いときは、「キーボード設定…」の入力ソース「編集…」→「+」→ 日本語 → Meltype で追加してください。
                    """
                self.keyboard.isHidden = false
                self.primary.isHidden = true
                self.close.keyEquivalent = "\r"
            } else {
                self.status.stringValue = "インストールを完了できませんでした。"
                self.showDetails(details)
            }
        }
    }

    private func run(_ executable: URL, arguments: [String], completion: @escaping (Bool, String) -> Void) {
        isWorking = true
        primary.isEnabled = false
        close.isEnabled = false
        keyboard.isEnabled = false
        progress.startAnimation(nil)
        DispatchQueue.global(qos: .userInitiated).async {
            let process = Process()
            let output = Pipe()
            process.executableURL = executable
            process.arguments = arguments
            process.standardOutput = output
            process.standardError = output
            let result: (Bool, String)
            do {
                try process.run()
                let data = output.fileHandleForReading.readDataToEndOfFile()
                process.waitUntilExit()
                result = (process.terminationStatus == 0, String(decoding: data, as: UTF8.self))
            } catch {
                result = (false, error.localizedDescription)
            }
            DispatchQueue.main.async {
                self.isWorking = false
                self.progress.stopAnimation(nil)
                self.primary.isEnabled = true
                self.close.isEnabled = true
                self.keyboard.isEnabled = true
                completion(result.0, result.1)
            }
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
