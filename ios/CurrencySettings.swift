// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
import Foundation

// 通貨の換算のレート。iOS はネットからレートを取らないので、固定 (同梱) と手入力の 2 つ。
struct CurrencySelection: Codable, Equatable {
    var mode = "Fixed"
    var manual = ""
    var decimals = 1

    static let modes = ["Fixed", "Manual"]
    var modeLabel: String { self.mode == "Manual" ? "手入力" : "固定（同梱のレート）" }
}

// アプリで選んだ設定を App Group に置き、キーボード拡張が読む。
final class CurrencySettingsStore {
    let directory: URL?
    init(directory: URL? = CurrencySettingsStore.sharedDirectory) { self.directory = directory }
    static var sharedDirectory: URL? {
        guard let group = Bundle.main.object(forInfoDictionaryKey: "MeltypeAppearanceAppGroup") as? String else { return nil }
        return FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: group)?.appendingPathComponent("Currency", isDirectory: true)
    }
    var file: URL? { self.directory?.appendingPathComponent("settings.json") }
    func load() -> CurrencySelection {
        guard let file = file, let bytes = try? Data(contentsOf: file), bytes.count < 8192,
              let value = try? JSONDecoder().decode(CurrencySelection.self, from: bytes) else {
            return CurrencySelection()
        }
        var selection = value
        // iOS は固定と手入力だけ。他の値 (自動など) が入っていたら固定に戻す
        if !CurrencySelection.modes.contains(selection.mode) { selection.mode = "Fixed" }
        selection.decimals = min(max(value.decimals, 0), 4)
        return selection
    }
    func save(_ value: CurrencySelection) throws {
        guard let directory = directory, let file = file else { throw CocoaError(.fileWriteNoPermission) }
        var selection = value
        selection.decimals = min(max(value.decimals, 0), 4)
        if !CurrencySelection.modes.contains(selection.mode) { selection.mode = "Fixed" }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try JSONEncoder().encode(selection).write(to: file, options: .atomic)
    }
}
