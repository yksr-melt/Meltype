// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota
// Copyright (C) 2026 aster-void

import Cocoa

/// 表示する入力モード。どちらにするかは本体が決める (state.japanese)。
enum MacInputMode: String, CaseIterable {
    case japanese, direct

    var title: String {
        switch self {
        case .japanese: return "日本語・英語の自動判定"
        case .direct: return "英数の直接入力"
        }
    }

    var shortTitle: String {
        switch self {
        case .japanese: return "あ"
        case .direct: return "A"
        }
    }
}

/// macOS の「サービス」から選ぶ操作 (キーの割り当ては OS が管理する)。
enum ModeShortcutAction: String, CaseIterable {
    case japanese, direct, toggle

    var title: String {
        switch self {
        case .japanese: return MacInputMode.japanese.title
        case .direct: return MacInputMode.direct.title
        case .toggle: return "日本語 ⇔ 英数"
        }
    }

    var serviceIdentifier: String { rawValue }

    var serviceTitle: String { "Meltype：" + title }

    init?(serviceIdentifier: String) {
        self.init(rawValue: serviceIdentifier)
    }
}
