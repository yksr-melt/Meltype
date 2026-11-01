// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Cocoa
import InputMethodKit

if CommandLine.arguments.contains("--version") {
    let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "?"
    print("Meltype \(version)")
    exit(0)
}

if CommandLine.arguments.contains("--register-input-source") {
    let outcome = InputSourceRegistration.register()
    guard outcome == .ready else { exit(outcome.exitCode) }
    // メニューと切り替えUIは一覧を別々にキャッシュするため、両方の表示を更新する。
    // 有効なモードの確認後だけ実行し、登録待ちのときは一覧を消さない。
    let refresh = Process()
    refresh.executableURL = URL(fileURLWithPath: "/usr/bin/killall")
    refresh.arguments = ["TextInputMenuAgent", "TextInputSwitcher"]
    refresh.standardOutput = FileHandle.nullDevice
    refresh.standardError = FileHandle.nullDevice
    do {
        try refresh.run()
        refresh.waitUntilExit()
    } catch {
        FileHandle.standardError.write(Data("入力メニューの表示を更新できませんでした: \(error.localizedDescription)\n".utf8))
    }
    exit(0)
}

// Input Method Kit のサーバーを起動する。入力欄 (クライアント) ごとに MeltypeInputController が作られる。

/// 変換の候補の一覧 (すべての入力欄で共有する)。
var candidatesWindow: IMKCandidates?

// 本体 (libMeltypeNative.dylib) を読み込み、漢字変換・英単語の判定の関数を登録しておく。
NativeCore.shared.initialize()

if CommandLine.arguments.contains("--self-test") || CommandLine.arguments.contains("--check-inputs") {
    var failures = 0
    func check(_ name: String, _ condition: Bool) {
        print("\(condition ? "PASS" : "FAIL") \(name)")
        if !condition { failures += 1 }
    }
    check("reconversion: replaces matching previous text",
          matchesExpectedText("って", expected: "って", deleteBefore: 2))
    check("reconversion: preserves changed pasted text",
          !matchesExpectedText("paste", expected: "って", deleteBefore: 2))
    check("reconversion: counts UTF-16 units",
          matchesExpectedText("😊", expected: "😊", deleteBefore: 2))
    check("reconversion: rejects missing expected text",
          !matchesExpectedText("って", expected: nil, deleteBefore: 2))
    func type(_ text: String, session: UnsafeMutableRawPointer?, before: String? = nil) -> (String, SessionResult?) {
        var output = ""
        var last: SessionResult?
        for character in text {
            let vk: Int32
            switch character {
            case " ": vk = 0x20
            case "\n": vk = 0x0D
            case "\t": vk = 0x09
            default: vk = Int32(character.uppercased().utf16.first ?? 0)
            }
            let code = Int32(String(character).utf16.first ?? 0)
            last = NativeCore.shared.handleKey(session, vk: vk, character: code,
                modifiers: character.isUppercase ? 1 : 0, before: before, after: nil)
            for edit in last?.commits ?? [] {
                if edit.deleteBefore > 0 {
                    let current = output as NSString
                    // 消す文字を確認できないときは、前の文字は消さずに edit.text だけを入れる (本体の状態とそろえる)。
                    if edit.deleteBefore <= current.length,
                       matchesExpectedText(current.substring(from: current.length - edit.deleteBefore),
                                           expected: edit.expect, deleteBefore: edit.deleteBefore) {
                        output = current.substring(to: current.length - edit.deleteBefore)
                    }
                }
                output += edit.text
            }
            if last?.consumed != true { output += String(character) }
        }
        return (output, last)
    }
    if let index = CommandLine.arguments.firstIndex(of: "--check-inputs"), index + 1 < CommandLine.arguments.count {
        do {
            let content = try String(contentsOfFile: CommandLine.arguments[index + 1], encoding: .utf8)
            var count = 0
            for line in content.split(separator: "\n") where !line.hasPrefix("#") {
                let parts = line.split(separator: "\t", maxSplits: 1, omittingEmptySubsequences: false)
                guard parts.count == 2 else { continue }
                let session = NativeCore.shared.createSession()
                let (output, _) = type(String(parts[0]) + "\n", session: session)
                NativeCore.shared.destroySession(session)
                check(String(parts[0]), output == String(parts[1]))
                if output != String(parts[1]) { print("expected: \(parts[1]); actual: \(output)") }
                count += 1
            }
            print("\(count - failures)/\(count) input checks passed")
            exit(count > 0 && failures == 0 ? 0 : 1)
        } catch {
            print("Input check file could not be read: \(error.localizedDescription)")
            exit(1)
        }
    }
    for (input, expected) in [("kyouhagoogledekensaku \n", "今日はgoogleで検索"),
                               ("I want to go to the park\n", "I want to go to the park"),
                               ("google github hello\n", "google github hello"),
                               ("seeyouagain\n", "see you again")] {
        let session = NativeCore.shared.createSession()
        let (output, _) = type(input, session: session)
        print("result: \(output)")
        check(input.trimmingCharacters(in: .whitespacesAndNewlines), output == expected)
        NativeCore.shared.destroySession(session)
    }
    let englishSession = NativeCore.shared.createSession()
    let (reflected, _) = type("reflectsareta\n", session: englishSession)
    check("issue 77: reflect + Japanese", reflected == "reflectされた")
    NativeCore.shared.destroySession(englishSession)
    let sentence = "kyouhagoogledekensakusitekekkawomiru\n"
    let started = Date()
    let checksBefore = NativeCore.shared.spellCheckCount
    let firstSession = NativeCore.shared.createSession()
    let (firstOutput, _) = type(sentence, session: firstSession)
    NativeCore.shared.destroySession(firstSession)
    let checksAfterFirst = NativeCore.shared.spellCheckCount
    let secondSession = NativeCore.shared.createSession()
    let (secondOutput, _) = type(sentence, session: secondSession)
    NativeCore.shared.destroySession(secondSession)
    let repeatedChecks = NativeCore.shared.spellCheckCount - checksAfterFirst
    print("issue 114: first checks=\(checksAfterFirst - checksBefore), repeat checks=\(repeatedChecks), elapsed=\(Date().timeIntervalSince(started))s")
    check("issue 114: repeated spelling uses cache", repeatedChecks == 0 && firstOutput == secondOutput)
    let other: [String: Any] = ["InputSourceKind": "Keyboard Layout", "KeyboardLayout Name": "ABC"]
    let duplicate: [String: Any] = ["Bundle ID": InputSourceRegistration.identifier, "InputSourceKind": "Keyboard Input Method", "metadata": ["keep": true]]
    let duplicateMode: [String: Any] = ["Bundle ID": InputSourceRegistration.identifier,
                                      "Input Mode": InputSourceRegistration.japaneseIdentifier, "InputSourceKind": "Input Mode"]
    let duplicatePairs = (0..<15).flatMap { _ in [duplicate, duplicateMode] }
    let repaired = InputSourceRegistration.repaired(legacy: [other] + duplicatePairs, thirdParty: duplicatePairs)
    check("issue 101: deduplicate each existing store without moving entries",
          NSArray(array: repaired.legacy).isEqual(to: [other, duplicate, duplicateMode]) &&
          NSArray(array: repaired.thirdParty ?? []).isEqual(to: [duplicate, duplicateMode]))
    let repeatedRepair = InputSourceRegistration.repaired(legacy: repaired.legacy, thirdParty: repaired.thirdParty)
    check("issue 101: repair is idempotent", NSArray(array: repeatedRepair.legacy).isEqual(to: repaired.legacy) &&
          NSArray(array: repeatedRepair.thirdParty ?? []).isEqual(to: repaired.thirdParty ?? []))
    let fallback = InputSourceRegistration.repaired(legacy: [duplicateMode, other, duplicate], thirdParty: nil)
    check("issue 101: preserve order, metadata and absent store", fallback.thirdParty == nil &&
          NSArray(array: fallback.legacy).isEqual(to: [duplicateMode, other, duplicate]))
    let malformedPreferences: [Any] = ["not an array", [other, "not a dictionary"] as [Any]]
    for invalid in malformedPreferences {
        do {
            _ = try InputSourceRegistration.validatedSources(invalid, name: "self-test")
            check("issue 101: reject malformed preferences", false)
        } catch { check("issue 101: reject malformed preferences", true) }
    }
    do {
        for format in [PropertyListSerialization.PropertyListFormat.xml, .binary] {
            let data = try PropertyListSerialization.data(fromPropertyList: [["shortcut": "カイシャ", "phrase": "株式会社テスト"]], format: format, options: 0)
            let entries = try MacUserDictionary.entries(from: data)
            let (merged, added) = MacUserDictionary.merge(entries + entries, into: "# existing\nなまえ\t既存の名前\n")
            check("issue 40: plist import \(format)", added == 1 && merged.contains("かいしゃ\t株式会社テスト") && merged.contains("なまえ\t既存の名前"))
            check("issue 40: reimport is idempotent", MacUserDictionary.merge(entries, into: merged).1 == 0)
        }
    } catch { check("issue 40: plist parsing", false) }
    let session = NativeCore.shared.createSession()
    let (_, emoji) = type("egao ", session: session)
    check("emoji candidate", emoji?.view?.candidates.contains("😊") == true)
    _ = NativeCore.shared.commit(session)
    let (_, typo) = type("buresureddo", session: session)
    check("misspelling suggestion", typo?.view?.suggestion?.contains("ブレスレット") == true)
    let (_, corrected) = type("\t", session: session)
    print("corrected: \(corrected?.view?.text ?? "nil")")
    check("misspelling correction", ["ぶれすれっと", "ブレスレット"].contains(corrected?.view?.text ?? ""))
    _ = NativeCore.shared.commit(session)
    NativeCore.shared.setCodeInput(session, true)
    let (code, _) = type("kyouha", session: session, before: "let value = ")
    check("code stays ASCII", code == "kyouha")
    let (_, comment) = type("kyouha", session: session, before: "// ")
    check("comment composes Japanese", comment?.view?.text.contains("きょう") == true || comment?.view?.text.contains("今日") == true)
    NativeCore.shared.destroySession(session)
    let stressSession = NativeCore.shared.createSession()
    let stressStart = Date()
    for iteration in 0..<1000 {
        _ = type("kyouhagoogledekensaku ", session: stressSession)
        _ = NativeCore.shared.selectCandidate(stressSession, index: iteration % 2)
        let committed = NativeCore.shared.commit(stressSession)
        check("stress commit \(iteration)", committed?.view == nil)
        NativeCore.shared.setDirect(stressSession, true)
        _ = type("hello", session: stressSession)
        NativeCore.shared.setDirect(stressSession, false)
        let (output, _) = type("wsldeshell\n", session: stressSession)
        check("stress mode switch \(iteration)", output == "wslでshell")
    }
    NativeCore.shared.destroySession(stressSession)
    print("1000 native input/candidate/commit/mode cycles in \(Date().timeIntervalSince(stressStart))s")
    exit(failures == 0 ? 0 : 1)
}

let connectionName = Bundle.main.object(forInfoDictionaryKey: "InputMethodConnectionName") as? String ?? "Meltype_Connection"
guard let bundleIdentifier = Bundle.main.bundleIdentifier,
      let server = IMKServer(name: connectionName, bundleIdentifier: bundleIdentifier) else {
    NSLog("Meltype: IMKServer を起動できませんでした (Meltype.app から起動してください)")
    exit(1)
}
candidatesWindow = IMKCandidates(server: server, panelType: kIMKSingleColumnScrollingCandidatePanel)
NSRegisterServicesProvider(ModeServices.shared, ModeServices.portName)
NSApplication.shared.run()
