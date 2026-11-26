// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ieyoukan

import AppKit

@MainActor
private final class Installer: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private let window = NSWindow(
        // 完了画面では許可・切り替え・手動追加の案内を折り返して表示する。
        contentRect: NSRect(x: 0, y: 0, width: 480, height: 204),
        styleMask: [.titled, .closable], backing: .buffered, defer: false
    )
    private let status = NSTextField(wrappingLabelWithString: "この Mac のアカウントに入力ソースと設定アプリを追加します。")
    private let progress = NSProgressIndicator()
    private let keyboard = NSButton(title: "キーボード設定…", target: nil, action: nil)
    private let primary = NSButton(title: "インストール", target: nil, action: nil)
    private let close = NSButton(title: "閉じる", target: nil, action: nil)
    private let selectionInstructions = """
        有効化の確認が表示されたら、先に「許可」を押してください。
        「Meltype を使う」を押すと入力ソースを切り替えます。
        入力メニューに無い場合は、「キーボード設定…」の入力ソース「編集…」→「+」→ 日本語 → Meltype から追加してください。
        """
    private var isWorking = false
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
        status.stringValue = "Meltype を配置して入力ソースに登録しています…"
        run(URL(fileURLWithPath: "/bin/bash"), arguments: [script.path]) { exitCode, details in
            if exitCode == 0 {
                // 初回は install.sh が選択を試み、許可のあとでもボタンから選び直せる。
                self.status.stringValue = "インストールが完了しました。\n\(self.selectionInstructions)"
                self.keyboard.isHidden = false
                self.primary.title = "Meltype を使う"
                self.primary.action = #selector(self.selectSource)
            } else {
                self.status.stringValue = "インストールを完了できませんでした。"
                self.showDetails(details)
            }
        }
    }

    @objc private func selectSource() {
        let helper = installedApp.appendingPathComponent("Contents/MacOS/MeltypeInstallSupport")
        status.stringValue = "入力ソースを切り替えています…"
        run(helper, arguments: ["--select", installedApp.path]) { exitCode, details in
            if exitCode == 0 {
                self.status.stringValue = "入力ソースに Meltype を選びました。入力メニューに出ない場合は、キーボード設定で Meltype を追加し直してください。"
                self.primary.isHidden = true
                self.close.keyEquivalent = "\r"
            } else {
                self.status.stringValue = "Meltype に切り替えられませんでした。\n\(self.selectionInstructions)"
                // 終了コード 2 は選択できないときの案内で対処できるため、再試行ボタンをすぐに使えるようにする。
                if exitCode != 2 { self.showDetails(details) }
            }
        }
    }

    private func run(_ executable: URL, arguments: [String], completion: @escaping (Int32?, String) -> Void) {
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
            let result: (Int32?, String)
            do {
                try process.run()
                let data = output.fileHandleForReading.readDataToEndOfFile()
                process.waitUntilExit()
                result = (process.terminationStatus, String(decoding: data, as: UTF8.self))
            } catch {
                result = (nil, error.localizedDescription)
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
