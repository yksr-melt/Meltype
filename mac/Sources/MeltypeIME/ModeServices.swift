// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import Cocoa
import Carbon.HIToolbox

protocol ModeServiceTarget: AnyObject {
    /// 入力欄を取得できない場合は操作せず false を返す。
    func selectModeFromService(_ action: ModeShortcutAction) -> Bool
}

/// キーの割り当て・配信は macOS の「サービス」が管理する。
/// 入力欄ごとの controller を弱参照し、非アクティブな入力欄への配送を防ぐ。
final class ModeServices: NSObject {
    static let shared = ModeServices()
    static let portName = "MeltypeModeServices"
    var updateMenu: ((NSMenu) -> Void)?
    private weak var activeTarget: ModeServiceTarget?
    private let isMeltypeSelected: () -> Bool
    private let selectWithoutInput: (ModeShortcutAction) -> Void

    init(isMeltypeSelected: @escaping () -> Bool = ModeServices.currentInputSourceIsMeltype,
         selectWithoutInput: @escaping (ModeShortcutAction) -> Void = { action in
             let mode = action.mode(after: ModeSelection.shared.current)
             ModeSelection.shared.select(mode)
             ModeIndicator.shared.show(mode)
         }) {
        self.isMeltypeSelected = isMeltypeSelected
        self.selectWithoutInput = selectWithoutInput
        super.init()
    }

    func activate(_ target: ModeServiceTarget) { activeTarget = target }

    func deactivate(_ target: ModeServiceTarget) {
        if activeTarget === target { activeTarget = nil }
    }

    @objc func changeInputMode(_ pasteboard: NSPasteboard, userData: String?,
                               error: AutoreleasingUnsafeMutablePointer<NSString?>) {
        guard Thread.isMainThread else {
            error.pointee = "Meltypeの入力モードはメインスレッドで変更してください。"
            return
        }
        guard let userData, let action = ModeShortcutAction(serviceIdentifier: userData) else {
            error.pointee = "Meltypeの入力モードの指定が不正です。"
            return
        }
        guard isMeltypeSelected() else {
            error.pointee = "入力ソースでMeltypeを選択してください。"
            return
        }
        // 入力カーソルがない状態にも、独立したモード選択操作を用意する。
        guard let target = activeTarget else {
            selectWithoutInput(action)
            return
        }
        guard target.selectModeFromService(action) else {
            error.pointee = "Meltypeの入力欄を取得できませんでした。"
            return
        }
    }

    private static func currentInputSourceIsMeltype() -> Bool {
        guard let source = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue(),
              let pointer = TISGetInputSourceProperty(source, kTISPropertyInputSourceID),
              let identifier = unsafeBitCast(pointer, to: CFTypeRef.self) as? String else { return false }
        return identifier == InputSourceRegistration.identifier + ".Japanese"
    }
}
