// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import Cocoa

protocol ImportDialogApplication {
    func activationPolicy() -> NSApplication.ActivationPolicy
    func setActivationPolicy(_ activationPolicy: NSApplication.ActivationPolicy) -> Bool
    func activate(ignoringOtherApps flag: Bool)
}

extension NSApplication: ImportDialogApplication {}

/// バックグラウンド専用の IME でも、辞書を選ぶ間は通常のパネルを操作できるようにする。
enum ImportDialogPresentation {
    enum PresentationError: LocalizedError {
        case cannotActivate
        case cannotRestore

        var errorDescription: String? {
            switch self {
            case .cannotActivate: return "ユーザー辞書の選択画面を表示できませんでした。"
            case .cannotRestore: return "辞書の取り込み後にアプリの前面化設定を元に戻せませんでした。"
            }
        }
    }

    private static var isPresenting = false

    static func run(application: ImportDialogApplication = NSApplication.shared, body: () -> Void) throws {
        // body から同期的に再入しても、内側の表示処理やポリシー変更を行わない。
        guard !isPresenting else { return }
        isPresenting = true
        defer { isPresenting = false }

        let previousPolicy = application.activationPolicy()
        // LSBackgroundOnly のまま activate しても前面化できない (#954)。
        guard application.setActivationPolicy(.accessory) else { throw PresentationError.cannotActivate }
        application.activate(ignoringOtherApps: true)
        // ファイル選択の取消・取り込み失敗の通知も、この範囲で完了させる。
        body()
        guard application.setActivationPolicy(previousPolicy) else { throw PresentationError.cannotRestore }
    }
}
