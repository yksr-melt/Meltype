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

    struct Failure: LocalizedError {
        let errorDescription: String?
        init(_ message: String) { errorDescription = message }
    }

    static func isMeltype(_ source: [String: Any]) -> Bool {
        (source["Bundle ID"] as? String) == identifier ||
        (source["Input Mode"] as? String) == japaneseIdentifier
    }

    static func normalized(_ sources: [[String: Any]]) -> [[String: Any]] {
        var result = sources.filter { !isMeltype($0) }
        result.append(["Bundle ID": identifier, "Input Mode": japaneseIdentifier, "InputSourceKind": "Input Mode"])
        result.append(["Bundle ID": identifier, "InputSourceKind": "Keyboard Input Method"])
        return result
    }

    static func repaired(legacy: [[String: Any]], thirdParty: [[String: Any]]?, preferThirdParty: Bool) -> Sources {
        if thirdParty != nil || preferThirdParty {
            return Sources(legacy: legacy.filter { !isMeltype($0) }, thirdParty: normalized(thirdParty ?? []))
        }
        return Sources(legacy: normalized(legacy), thirdParty: thirdParty)
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

    static func register() -> Int32 {
        var preferencesChanged = false
        defer {
            if preferencesChanged {
                CFNotificationCenterPostNotification(CFNotificationCenterGetDistributedCenter(),
                    CFNotificationName(kTISNotifyEnabledKeyboardInputSourcesChanged), nil, nil, true)
            }
        }
        do {
            let legacy = try read(legacyDomain, key: legacyKey)
            let thirdParty = try read(thirdPartyDomain, key: thirdPartyKey)
            let preferThirdParty = ProcessInfo.processInfo.operatingSystemVersion.majorVersion >= 26
            let sources = repaired(legacy: legacy ?? [], thirdParty: thirdParty, preferThirdParty: preferThirdParty)
            if let repairedThirdParty = sources.thirdParty {
                preferencesChanged = try write(repairedThirdParty, replacing: thirdParty, domain: thirdPartyDomain, key: thirdPartyKey)
            }
            let legacyChanged = try write(sources.legacy, replacing: legacy, domain: legacyDomain, key: legacyKey)
            preferencesChanged = preferencesChanged || legacyChanged
            let status = TISRegisterInputSource(Bundle.main.bundleURL as CFURL)
            guard status == noErr else { throw Failure("TISRegisterInputSource failed (OSStatus \(status))") }
            // 親を有効にしてからモードを確かめる。同じIDの重複オブジェクトは有効化しない。
            for id in [identifier, japaneseIdentifier] {
                let matches = inputSources().filter { (property($0, kTISPropertyInputSourceID) as? String) == id }
                if !matches.contains(where: { (property($0, kTISPropertyInputSourceIsEnabled) as? Bool) == true }),
                   let source = matches.first {
                    let status = TISEnableInputSource(source)
                    guard status == noErr else { throw Failure("TISEnableInputSource failed for \(id) (OSStatus \(status))") }
                }
            }
            let deadline = Date().addingTimeInterval(2)
            repeat {
                if enabledJapaneseCount() == 1 { return 0 }
                Thread.sleep(forTimeInterval: 0.1)
            } while Date() < deadline
            throw Failure("Expected one enabled selectable Meltype Japanese mode, found \(enabledJapaneseCount())")
        } catch {
            FileHandle.standardError.write(Data("Input-source repair failed: \(error.localizedDescription)\n".utf8))
            return 1
        }
    }
}
