// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 joyamazaki

import AppKit
import Carbon
import Sparkle

/// 更新確認・署名検証・インストール・再起動はSparkleに任せる。
/// 自動ダウンロード・自動インストールは使わず、利用者が更新を選ぶ。
final class MacUpdater: NSObject, SPUUpdaterDelegate, NSApplicationDelegate, NSMenuItemValidation {
    static let shared = MacUpdater()
    private var controller: SPUStandardUpdaterController?
    private let gate = UpdateRelaunchGate()
    private var retry: Timer?
    private let repairKey = "MacRegisterAfterSparkleUpdate"
    private let selectKey = "MacSelectAfterSparkleUpdate"

    func start() {
        ModeServices.shared.updateMenu = { [weak self] menu in self?.addMenuItems(to: menu) }
        repairAfterUpdateIfNeeded()
        let info = Bundle.main.infoDictionary ?? [:]
        guard MacUpdatePolicy.isInstalled(Bundle.main.bundleURL),
              MacUpdatePolicy.isConfigured(feed: info["SUFeedURL"] as? String,
                                           publicKey: info["SUPublicEDKey"] as? String) else { return }
        let controller = SPUStandardUpdaterController(startingUpdater: false, updaterDelegate: self, userDriverDelegate: nil)
        self.controller = controller
        controller.startUpdater()
        controller.updater.automaticallyDownloadsUpdates = false
    }

    func addMenuItems(to menu: NSMenu) {
        menu.addItem(.separator())
        let check = menu.addItem(withTitle: "更新を確認…", action: #selector(checkForUpdates(_:)), keyEquivalent: "")
        check.target = self
        let notify = menu.addItem(withTitle: "新しいバージョンを通知", action: #selector(toggleNotifications(_:)), keyEquivalent: "")
        notify.target = self
        notify.state = controller?.updater.automaticallyChecksForUpdates == true ? .on : .off
    }

    @objc func checkForUpdates(_ sender: Any?) {
        guard let controller else {
            let alert = NSAlert()
            alert.messageText = "この確認版では更新配信がまだ有効になっていません"
            alert.informativeText = "公式配布版で利用できるようになると、ここから新しいバージョンを確認できます。"
            NSApp.activate(ignoringOtherApps: true)
            alert.runModal()
            return
        }
        controller.checkForUpdates(sender)
    }

    @objc private func toggleNotifications(_ sender: Any?) {
        guard let updater = controller?.updater else { return }
        updater.automaticallyChecksForUpdates.toggle()
    }

    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        if menuItem.action == #selector(toggleNotifications(_:)) { return controller != nil }
        if menuItem.action == #selector(checkForUpdates(_:)) {
            return controller?.updater.canCheckForUpdates ?? true
        }
        return true
    }

    func updater(_ updater: SPUUpdater, shouldPostponeRelaunchForUpdate item: SUAppcastItem,
                 untilInvokingBlock installHandler: @escaping () -> Void) -> Bool {
        let postponed = gate.postpone(ready: MeltypeInputController.prepareForUpdate(), continuation: installHandler)
        if postponed {
            retry?.invalidate()
            retry = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] timer in
                guard let self else { timer.invalidate(); return }
                self.gate.resumeIfReady(MeltypeInputController.prepareForUpdate())
                if !self.gate.isPending { timer.invalidate(); self.retry = nil }
            }
        }
        return postponed
    }

    func updaterShouldRelaunchApplication(_ updater: SPUUpdater) -> Bool {
        // 待機期限が切れた場合は、Sparkleの公開APIで今回のインストールを中止する。
        // Sparkle 2.10は待機用の継続を呼んだあとも、この可否を確認する。
        !gate.didExpire
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        // 更新の待機は上の専用経路だけで行う。ログアウト・再起動などは妨げない。
        // 確定先のある入力欄だけ処理し、確定できない欄があっても終了を許可する。
        _ = MeltypeInputController.prepareForUpdate()
        return .terminateNow
    }

    func updaterWillRelaunchApplication(_ updater: SPUUpdater) {
        UserDefaults.standard.set(true, forKey: repairKey)
        UserDefaults.standard.set(Self.currentSourceIsMeltype(), forKey: selectKey)
    }

    func updater(_ updater: SPUUpdater, didAbortWithError error: Error) {
        gate.cancel()
        retry?.invalidate()
        retry = nil
    }

    private static func currentSourceIsMeltype() -> Bool {
        guard let source = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue(),
              let value = TISGetInputSourceProperty(source, kTISPropertyInputSourceID) else { return false }
        return (Unmanaged<AnyObject>.fromOpaque(value).takeUnretainedValue() as? String) == InputSourceRegistration.japaneseIdentifier
    }

    private func repairAfterUpdateIfNeeded() {
        guard UserDefaults.standard.bool(forKey: repairKey) else { return }
        let helper = Bundle.main.bundleURL.appendingPathComponent("Contents/MacOS/MeltypeRegisterInputSource")
        let select = UserDefaults.standard.bool(forKey: selectKey)
        DispatchQueue.global(qos: .utility).async {
            let process = Process()
            process.executableURL = helper
            process.arguments = [Bundle.main.bundlePath]
            do {
                try process.run()
                process.waitUntilExit()
                guard process.terminationStatus == 0 else { return }
                DispatchQueue.main.async {
                    if select {
                        let filter = [kTISPropertyInputSourceID as String: InputSourceRegistration.japaneseIdentifier] as CFDictionary
                        let sources = TISCreateInputSourceList(filter, false)?.takeRetainedValue() as? [TISInputSource] ?? []
                        guard let source = sources.first, TISSelectInputSource(source) == noErr else {
                            // 登録が反映待ち、または選択失敗なら次の起動時に再試行できるよう記録を残す。
                            return
                        }
                    }
                    UserDefaults.standard.removeObject(forKey: self.repairKey)
                    UserDefaults.standard.removeObject(forKey: self.selectKey)
                }
            } catch { NSLog("Meltype: 更新後の入力ソース登録を完了できませんでした: %@", error.localizedDescription) }
        }
    }
}
