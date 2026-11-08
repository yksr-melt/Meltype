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
    // 更新時はメニューを再起動せず、前面アプリの入力接続を維持する。
    if CommandLine.arguments.contains("--keep-input-menu") { exit(0) }
    let refresh = Process()
    refresh.executableURL = URL(fileURLWithPath: "/usr/bin/killall")
    refresh.arguments = ["TextInputMenuAgent"]
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

/// 変換の候補の一覧 (すべての入力欄で共有する)。
var candidatesWindow: IMKCandidates?

func runIsolatedChecks(inputPath: String?) -> Int32 {
    let files = FileManager.default
    let data = files.temporaryDirectory.appendingPathComponent("meltype-input-check.\(UUID().uuidString)", isDirectory: true)
    var status: Int32 = 1
    do {
        try files.createDirectory(at: data, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        defer {
            do { try files.removeItem(at: data) }
            catch {
                fputs("検査用の一時ディレクトリを削除できませんでした: \(error.localizedDescription)\n", stderr)
                status = 1
            }
        }
        // Core は最初に参照したデータの保存先を保持するので、読み込む前に設定する。
        guard setenv("MELTYPE_DATA_DIR", data.path, 1) == 0 else {
            perror("MELTYPE_DATA_DIR")
            return 1
        }
        NativeCore.shared.initialize()
        status = runChecks(inputPath: inputPath)
    } catch {
        fputs("検査用の一時ディレクトリを作成できませんでした: \(error.localizedDescription)\n", stderr)
        return 1
    }
    return status
}

func runChecks(inputPath: String?) -> Int32 {
    var failures = 0
    func check(_ name: String, _ condition: Bool) {
        print("\(condition ? "PASS" : "FAIL") \(name)")
        if !condition { failures += 1 }
    }
    /// 入力欄の代わり (確定した文字とアプリに通した文字)。キーごとにキャレットの前として本体に渡す
    final class Document {
        var text = ""
        let host = NativeCore.shared.createHost()
        deinit { NativeCore.shared.destroyHost(host) }

        @discardableResult
        func send(_ type: String, _ fields: [String: Any] = [:]) -> ImeOutput? {
            var event: [String: Any] = ["v": 1, "type": type, "sid": "self-test", "field": ["before": text]]
            event.merge(fields) { _, new in new }
            let output = NativeCore.shared.send(host, event)
            for edit in output?.result.commits ?? [] {
                // 確定し直しは本体が照合済み: 前の deleteBefore 文字 (UTF-16) を消してから入れる
                let current = text as NSString
                text = current.substring(to: current.length - min(edit.deleteBefore, current.length)) + edit.text
            }
            return output
        }
    }
    func type(_ text: String, into document: Document) -> ImeOutput? {
        var last: ImeOutput?
        for character in text {
            var key: [String: Any] = ["shift": character.isUppercase]
            switch character {
            case " ": key["vk"] = 0x20; key["ch"] = 0x20
            case "\n": key["vk"] = 0x0D
            case "\t": key["vk"] = 0x09
            default: key["ch"] = character.unicodeScalars.first!.value
            }
            last = document.send("key", ["key": key])
            if last?.result.consumed != true && character != "\n" && character != "\t" { document.text += String(character) }
        }
        return last
    }
    if let inputPath {
        do {
            let content = try String(contentsOfFile: inputPath, encoding: .utf8)
            var count = 0
            for line in content.split(separator: "\n") where !line.hasPrefix("#") {
                let parts = line.split(separator: "\t", maxSplits: 1, omittingEmptySubsequences: false)
                guard parts.count == 2 else { continue }
                let document = Document()
                _ = type(String(parts[0]) + "\n", into: document)
                let output = document.text
                check(String(parts[0]), output == String(parts[1]))
                if output != String(parts[1]) { print("expected: \(parts[1]); actual: \(output)") }
                count += 1
            }
            print("\(count - failures)/\(count) input checks passed")
            return count > 0 && failures == 0 ? 0 : 1
        } catch {
            print("Input check file could not be read: \(error.localizedDescription)")
            return 1
        }
    }
    for (input, expected) in [("kyouhagoogledekensaku \n", "今日はgoogleで検索"),
                               ("I want to go to the park\n", "I want to go to the park"),
                               ("google github hello\n", "google github hello"),
                               ("seeyouagain\n", "see you again")] {
        let document = Document()
        _ = type(input, into: document)
        print("result: \(document.text)")
        check(input.trimmingCharacters(in: .whitespacesAndNewlines), document.text == expected)
    }
    let english = Document()
    _ = type("reflectsareta\n", into: english)
    check("issue 77: reflect + Japanese", english.text == "reflectされた")
    let sentence = "kyouhagoogledekensakusitekekkawomiru\n"
    let started = Date()
    let checksBefore = NativeCore.shared.spellCheckCount
    let first = Document()
    _ = type(sentence, into: first)
    let checksAfterFirst = NativeCore.shared.spellCheckCount
    let second = Document()
    _ = type(sentence, into: second)
    let repeatedChecks = NativeCore.shared.spellCheckCount - checksAfterFirst
    print("issue 114: first checks=\(checksAfterFirst - checksBefore), repeat checks=\(repeatedChecks), elapsed=\(Date().timeIntervalSince(started))s")
    check("issue 114: repeated spelling uses cache", repeatedChecks == 0 && first.text == second.text)
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
    let document = Document()
    let emoji = type("egao ", into: document)
    check("emoji candidate", emoji?.result.view?.list?.items.contains("😊") == true)
    document.send("commit")
    let typo = type("buresureddo", into: document)
    check("misspelling suggestion", typo?.result.view?.suggestion?.contains("ブレスレット") == true)
    let corrected = type("\t", into: document)
    print("corrected: \(corrected?.result.view?.text ?? "nil")")
    check("misspelling correction", ["ぶれすれっと", "ブレスレット"].contains(corrected?.result.view?.text ?? ""))
    document.send("commit")
    let stress = Document()
    let stressStart = Date()
    for iteration in 0..<1000 {
        _ = type("kyouhagoogledekensaku ", into: stress)
        stress.send("select", ["index": iteration % 2])
        check("stress commit \(iteration)", stress.send("commit")?.result.view == nil)
        stress.send("setMode", ["japanese": false])
        _ = type("hello", into: stress)
        stress.send("setMode", ["japanese": true])
        stress.text = ""
        _ = type("wsldeshell\n", into: stress)
        check("stress mode switch \(iteration)", stress.text == "wslでshell")
    }
    print("1000 native input/candidate/commit/mode cycles in \(Date().timeIntervalSince(stressStart))s")
    return failures == 0 ? 0 : 1
}

if let index = CommandLine.arguments.firstIndex(of: "--check-inputs") {
    guard index + 1 < CommandLine.arguments.count else {
        fputs("Usage: Meltype --check-inputs <file>\n", stderr)
        exit(1)
    }
    exit(runIsolatedChecks(inputPath: CommandLine.arguments[index + 1]))
}
if CommandLine.arguments.contains("--self-test") {
    exit(runIsolatedChecks(inputPath: nil))
}

// 本体 (libMeltypeNative.dylib) を読み込み、漢字変換・英単語の判定の関数を登録しておく。
NativeCore.shared.initialize()

// Input Method Kit のサーバーを起動する。入力欄 (クライアント) ごとに MeltypeInputController が作られる。
let connectionName = Bundle.main.object(forInfoDictionaryKey: "InputMethodConnectionName") as? String ?? "Meltype_Connection"
guard let bundleIdentifier = Bundle.main.bundleIdentifier,
      let server = IMKServer(name: connectionName, bundleIdentifier: bundleIdentifier) else {
    NSLog("Meltype: IMKServer を起動できませんでした (Meltype.app から起動してください)")
    exit(1)
}
candidatesWindow = IMKCandidates(server: server, panelType: kIMKSingleColumnScrollingCandidatePanel)
NSRegisterServicesProvider(ModeServices.shared, ModeServices.portName)
NativeCore.shared.startBackgroundConversion()
NSApplication.shared.run()
