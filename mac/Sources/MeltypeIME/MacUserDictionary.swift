// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hrmcngs

import Foundation

enum MacUserDictionary {
    enum ImportError: LocalizedError {
        case unsupported
        var errorDescription: String? { "読みと単語を含むmacOSユーザー辞書のplistを選んでください。" }
    }

    static func entries(from data: Data) throws -> [(String, String)] {
        let plist = try PropertyListSerialization.propertyList(from: data, options: [], format: nil)
        let rows = (plist as? [[String: Any]]) ?? (plist as? [String: Any])?["UserDictionary"] as? [[String: Any]] ?? []
        let controls = CharacterSet.controlCharacters
        let entries = rows.compactMap { row -> (String, String)? in
            guard let raw = (row["reading"] ?? row["shortcut"]) as? String,
                  let phrase = (row["phrase"] ?? row["word"]) as? String else { return nil }
            let reading = (raw.applyingTransform(.hiraganaToKatakana, reverse: true) ?? raw).trimmingCharacters(in: .whitespacesAndNewlines)
            let word = phrase.trimmingCharacters(in: .whitespacesAndNewlines)
            // Core の UserDictionary.Parse は制御文字を含む行を読まない。
            // 取り込み件数に「使えない語」を含めないよう、保存前にも同じ条件で除外する。
            guard reading.utf16.count >= 2, !word.isEmpty,
                  !reading.unicodeScalars.contains(where: { controls.contains($0) }),
                  !word.unicodeScalars.contains(where: { controls.contains($0) }) else { return nil }
            return (reading, word)
        }
        guard !entries.isEmpty else { throw ImportError.unsupported }
        return entries
    }

    static func merge(_ entries: [(String, String)], into existing: String) -> (String, Int) {
        var seen = Set(existing.split(separator: "\n").map { String($0).trimmingCharacters(in: .newlines) })
        var lines: [String] = []
        for (reading, word) in entries {
            let line = reading + "\t" + word
            if seen.insert(line).inserted { lines.append(line) }
        }
        guard !lines.isEmpty else { return (existing, 0) }
        let separator = existing.isEmpty || existing.hasSuffix("\n") ? "" : "\n"
        return (existing + separator + lines.joined(separator: "\n") + "\n", lines.count)
    }

    static func importFile(_ source: URL, directory: String) throws -> Int {
        let entries = try entries(from: Data(contentsOf: source))
        let folder = URL(fileURLWithPath: directory, isDirectory: true)
        let destination = folder.appendingPathComponent("userdict.txt")
        let exists = FileManager.default.fileExists(atPath: destination.path)
        let existing = exists ? try String(contentsOf: destination, encoding: .utf8) : ""
        let (merged, count) = merge(entries, into: existing)
        if count > 0 {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try merged.write(to: destination, atomically: true, encoding: .utf8)
        }
        return count
    }
}
