// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hrmcngs

import Carbon
import Foundation

enum InputSourceRegistration {
    static let identifier = "io.github.yksr-melt.inputmethod.Meltype"
    static let japaneseIdentifier = identifier + ".Japanese"
    static let legacyDomain = "com.apple.HIToolbox"
    static let thirdPartyDomain = "com.apple.inputsources"
    static let legacyKey = "AppleEnabledInputSources"
    static let thirdPartyKey = "AppleEnabledThirdPartyInputSources"

    struct Sources {
        let legacy: [[String: Any]]
        let thirdParty: [[String: Any]]?
    }

    enum Outcome {
        case ready
        case pending
        case failed

        var exitCode: Int32 { self == .failed ? 1 : 0 }
    }

    struct Failure: LocalizedError {
        let errorDescription: String?
        init(_ message: String) { errorDescription = message }
    }

    static func isMeltype(_ source: [String: Any]) -> Bool {
        (source["Bundle ID"] as? String) == identifier ||
        (source["Input Mode"] as? String) == japaneseIdentifier
    }

    static func normalized(_ sources: [[String: Any]]) -> [[String: Any]] {
        var seen = Set<String>()
        var result: [[String: Any]] = []
        for source in sources {
            guard isMeltype(source) else {
                result.append(source)
                continue
            }
            let kind = source["InputSourceKind"] as? String
            if kind == "Input Mode", (source["Input Mode"] as? String) == japaneseIdentifier {
                if seen.insert("Input Mode").inserted { result.append(source) }
            } else if kind == "Keyboard Input Method", (source["Bundle ID"] as? String) == identifier {
                if seen.insert("Keyboard Input Method").inserted { result.append(source) }
            } else {
                result.append(source)
            }
        }
        if !seen.contains("Input Mode") {
            result.append(["Bundle ID": identifier, "Input Mode": japaneseIdentifier, "InputSourceKind": "Input Mode"])
        }
        if !seen.contains("Keyboard Input Method") {
            result.append(["Bundle ID": identifier, "InputSourceKind": "Keyboard Input Method"])
        }
        return result
    }

    static func repaired(legacy: [[String: Any]], thirdParty: [[String: Any]]?) -> Sources {
        Sources(legacy: legacy.contains(where: isMeltype) ? normalized(legacy) : legacy,
                thirdParty: thirdParty.map { $0.contains(where: isMeltype) ? normalized($0) : $0 })
    }

    static func validatedSources(_ value: Any?, name: String) throws -> [[String: Any]]? {
        guard let value else { return nil }
        guard let sources = value as? [[String: Any]] else {
            throw Failure("\(name) is not an array of input-source dictionaries")
        }
        return sources
    }

    private static func synchronize(_ domain: String) throws {
        guard CFPreferencesSynchronize(domain as CFString, kCFPreferencesCurrentUser, kCFPreferencesAnyHost) else {
            throw Failure("Could not synchronize \(domain)")
        }
    }

    private static func read(_ domain: String, key: String) throws -> [[String: Any]]? {
        try synchronize(domain)
        let value = CFPreferencesCopyValue(key as CFString, domain as CFString, kCFPreferencesCurrentUser, kCFPreferencesAnyHost)
        return try validatedSources(value, name: "\(domain)/\(key)")
    }

    private static func write(_ sources: [[String: Any]], replacing previous: [[String: Any]]?, domain: String, key: String) throws -> Bool {
        if let previous, NSArray(array: previous).isEqual(to: sources) { return false }
        if previous == nil && sources.isEmpty { return false }
        let current = try read(domain, key: key)
        guard (current == nil && previous == nil) ||
                (current != nil && previous != nil && NSArray(array: current!).isEqual(to: previous!)) else {
            throw Failure("\(domain)/\(key) changed during repair; run the command again")
        }
        CFPreferencesSetValue("MeltypeInputSourcesBeforeRepair" as CFString, (previous ?? []) as CFArray,
                              domain as CFString, kCFPreferencesCurrentUser, kCFPreferencesAnyHost)
        CFPreferencesSetValue(key as CFString, sources as CFArray,
                              domain as CFString, kCFPreferencesCurrentUser, kCFPreferencesAnyHost)
        try synchronize(domain)
        guard let saved = try read(domain, key: key), NSArray(array: saved).isEqual(to: sources) else {
            throw Failure("Could not save \(domain)/\(key)")
        }
        return true
    }

    private static func property(_ source: TISInputSource, _ key: CFString) -> AnyObject? {
        guard let pointer = TISGetInputSourceProperty(source, key) else { return nil }
        return Unmanaged<AnyObject>.fromOpaque(pointer).takeUnretainedValue()
    }

    private static func inputSources(includeAllInstalled: Bool = true) -> [TISInputSource] {
        TISCreateInputSourceList(nil, includeAllInstalled)?.takeRetainedValue() as? [TISInputSource] ?? []
    }

    private static func enabledJapaneseCount() -> Int {
        inputSources(includeAllInstalled: false).filter {
            (property($0, kTISPropertyInputSourceID) as? String) == japaneseIdentifier &&
            (property($0, kTISPropertyInputSourceIsEnabled) as? Bool) == true &&
            (property($0, kTISPropertyInputSourceIsSelectCapable) as? Bool) == true
        }.count
    }

    private static func prepare() throws {
        _ = try read(legacyDomain, key: legacyKey)
        _ = try read(thirdPartyDomain, key: thirdPartyKey)
        let status = TISRegisterInputSource(Bundle.main.bundleURL as CFURL)
        guard status == noErr else { throw Failure("TISRegisterInputSource failed (OSStatus \(status))") }
        for id in [identifier, japaneseIdentifier] {
            let matches = inputSources().filter { (property($0, kTISPropertyInputSourceID) as? String) == id }
            if !matches.contains(where: { (property($0, kTISPropertyInputSourceIsEnabled) as? Bool) == true }),
               let source = matches.first {
                let status = TISEnableInputSource(source)
                guard status == noErr else { throw Failure("TISEnableInputSource failed for \(id) (OSStatus \(status))") }
            }
        }

        var preferencesChanged = false
        defer {
            if preferencesChanged {
                CFNotificationCenterPostNotification(CFNotificationCenterGetDistributedCenter(),
                    CFNotificationName(kTISNotifyEnabledKeyboardInputSourcesChanged), nil, nil, true)
            }
        }
        let legacy = try read(legacyDomain, key: legacyKey)
        let thirdParty = try read(thirdPartyDomain, key: thirdPartyKey)
        let sources = repaired(legacy: legacy ?? [], thirdParty: thirdParty)
        if let repairedThirdParty = sources.thirdParty {
            preferencesChanged = try write(repairedThirdParty, replacing: thirdParty, domain: thirdPartyDomain, key: thirdPartyKey)
        }
        let legacyChanged = try write(sources.legacy, replacing: legacy, domain: legacyDomain, key: legacyKey)
        preferencesChanged = preferencesChanged || legacyChanged
    }

    static func register(
        prepare: () throws -> Void = prepare,
        enabledCount: () -> Int = enabledJapaneseCount,
        wait: () -> Void = { Thread.sleep(forTimeInterval: 0.1) }
    ) -> Outcome {
        do {
            try prepare()
            for attempt in 0...20 {
                let count = enabledCount()
                if count == 1 { return .ready }
                if attempt < 20 {
                    wait()
                } else {
                    FileHandle.standardError.write(Data(
                        "まだ入力ソースとして有効になっていません（有効で選択可能な日本語モード: \(count) 件）。システム設定 → キーボード →（テキスト入力の）入力ソース →「編集…」→「+」→ 日本語 → Meltype から追加してください。それでも出てこなければ、ログアウトしてログインし直してください。\n".utf8))
                }
            }
            return .pending
        } catch {
            FileHandle.standardError.write(Data("Input-source repair failed: \(error.localizedDescription)\n".utf8))
            return .failed
        }
    }
}
