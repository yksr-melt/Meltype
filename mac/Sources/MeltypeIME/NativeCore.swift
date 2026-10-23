// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import Foundation

// libMeltypeNative.dylib (C# の Meltype.Core を NativeAOT にしたもの) の関数。src/Meltype.Mac.Native/Exports.cs と合わせる。
typealias ClausesCallback = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
typealias CandidatesCallback = @convention(c) (UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
typealias IsWordCallback = @convention(c) (UnsafePointer<CChar>?) -> Int32

private typealias InitFunction = @convention(c) (ClausesCallback, CandidatesCallback, IsWordCallback) -> Int32
private typealias CreateFunction = @convention(c) () -> UnsafeMutableRawPointer?
private typealias DestroyFunction = @convention(c) (UnsafeMutableRawPointer?) -> Void
private typealias HandleKeyFunction = @convention(c) (UnsafeMutableRawPointer?, Int32, Int32, Int32, UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias CommitFunction = @convention(c) (UnsafeMutableRawPointer?) -> UnsafeMutablePointer<CChar>?
private typealias SelectFunction = @convention(c) (UnsafeMutableRawPointer?, Int32) -> UnsafeMutablePointer<CChar>?
private typealias SetDirectFunction = @convention(c) (UnsafeMutableRawPointer?, Int32) -> Void
private typealias DataDirectoryFunction = @convention(c) () -> UnsafeMutablePointer<CChar>?
private typealias ReportUrlFunction = @convention(c) (UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias FreeFunction = @convention(c) (UnsafeMutableRawPointer?) -> Void

// ---- 本体から呼ばれる関数 (文字列は strdup したものを返し、本体が free する) ----

/// (ひらがな, 文脈) → 「読み\t変換結果」を改行でつないだもの (文節ごと)。
private let clausesCallback: ClausesCallback = { hiragana, context in
    guard let hiragana else { return nil }
    let clauses = MeltypeConverter.shared.clauses(for: String(cString: hiragana), context: context.map { String(cString: $0) })
    guard !clauses.isEmpty else { return nil }
    return strdup(clauses.map { "\($0.reading)\t\($0.text)" }.joined(separator: "\n"))
}

/// 読み → 候補を改行でつないだもの。
private let candidatesCallback: CandidatesCallback = { reading in
    guard let reading else { return nil }
    let candidates = MeltypeConverter.shared.candidates(for: String(cString: reading))
    return strdup(candidates.joined(separator: "\n"))
}

/// 英単語として正しい綴りか (macOS のスペルチェッカー、英語で調べる)。
private let spellCacheLock = NSLock()
nonisolated(unsafe) private var spellCache: [String: Bool] = [:]
nonisolated(unsafe) private var spellCacheExpiration = Date.distantPast
nonisolated(unsafe) private var spellChecks = 0
private let isWordCallback: IsWordCallback = { word in
    guard let word else { return 0 }
    let text = String(cString: word)
    let cached: Bool? = spellCacheLock.withLock {
        if Date() >= spellCacheExpiration {
            spellCache.removeAll(keepingCapacity: true)
            spellCacheExpiration = Date().addingTimeInterval(300)
        }
        return spellCache[text]
    }
    if let cached { return cached ? 1 : 0 }
    let misspelled = NSSpellChecker.shared.checkSpelling(of: text, startingAt: 0, language: "en", wrap: false, inSpellDocumentWithTag: 0, wordCount: nil)
    let valid = misspelled.location == NSNotFound
    spellCacheLock.withLock {
        spellChecks += 1
        if spellCache.count >= 4096 { spellCache.removeAll(keepingCapacity: true) }
        spellCache[text] = valid
    }
    return valid ? 1 : 0
}

/// Swift 側で扱う結果 (本体の SessionResult.ToJson と同じ形)。
struct SessionResult: Decodable {
    let consumed: Bool
    let commits: [TextEdit]
    let view: CompositionView?
}

struct TextEdit: Decodable {
    let deleteBefore: Int
    let text: String
    let expect: String?
    /// 確定し直すときに text の後ろに続けて入る、今確定した語。前の文字を消せないときは、これだけを入れる
    let current: String?
}

// DeleteBefore uses UTF-16 units, matching .NET string lengths and AppKit ranges.
func matchesExpectedText(_ actual: String?, expected: String?, deleteBefore: Int) -> Bool {
    guard deleteBefore > 0,
          let expected,
          (expected as NSString).length == deleteBefore,
          actual == expected else { return false }
    return true
}

struct CompositionView: Decodable {
    let text: String
    let converting: Bool
    let selectedIndex: Int
    let selectedClause: Int
    let hint: String
    let candidates: [String]
    let clauses: [String]
    /// 選んでいる候補の意味 (無ければ nil)。候補で少し止まったら注釈に出す。
    let meaning: String?
    let suggestion: String?
}

/// libMeltypeNative.dylib を読み込んで呼ぶ。Meltype.app/Contents/Frameworks に置く (build.sh)。
final class NativeCore {
    static let shared = NativeCore()
    var spellCheckCount: Int { spellCacheLock.withLock { spellChecks } }

    private let library: UnsafeMutableRawPointer?
    private let initFunction: InitFunction?
    private let createFunction: CreateFunction?
    private let destroyFunction: DestroyFunction?
    private let handleKeyFunction: HandleKeyFunction?
    private let commitFunction: CommitFunction?
    private let selectFunction: SelectFunction?
    private let setDirectFunction: SetDirectFunction?
    private let setCodeInputFunction: SetDirectFunction?
    private let dataDirectoryFunction: DataDirectoryFunction?
    private let reportUrlFunction: ReportUrlFunction?
    private let freeFunction: FreeFunction?

    private init() {
        let path = (Bundle.main.privateFrameworksPath ?? "") + "/libMeltypeNative.dylib"
        // 初期化が終わるまで self のプロパティは使えないので、ローカルの handle から関数を探す。
        let handle = dlopen(path, RTLD_NOW)
        library = handle
        if handle == nil, let error = dlerror() {
            NSLog("Meltype: %@ を読み込めませんでした: %@", path, String(cString: error))
        }
        func symbol<T>(_ name: String, as type: T.Type) -> T? {
            guard let handle, let pointer = dlsym(handle, name) else { return nil }
            return unsafeBitCast(pointer, to: type)
        }
        initFunction = symbol("meltype_init", as: InitFunction.self)
        createFunction = symbol("meltype_create", as: CreateFunction.self)
        destroyFunction = symbol("meltype_destroy", as: DestroyFunction.self)
        handleKeyFunction = symbol("meltype_handle_key", as: HandleKeyFunction.self)
        commitFunction = symbol("meltype_commit", as: CommitFunction.self)
        selectFunction = symbol("meltype_select_candidate", as: SelectFunction.self)
        setDirectFunction = symbol("meltype_set_direct", as: SetDirectFunction.self)
        setCodeInputFunction = symbol("meltype_set_code_input", as: SetDirectFunction.self)
        dataDirectoryFunction = symbol("meltype_data_directory", as: DataDirectoryFunction.self)
        reportUrlFunction = symbol("meltype_report_url", as: ReportUrlFunction.self)
        freeFunction = symbol("meltype_free", as: FreeFunction.self)
    }

    func initialize() {
        _ = initFunction?(clausesCallback, candidatesCallback, isWordCallback)
    }

    func createSession() -> UnsafeMutableRawPointer? { createFunction?() }

    func destroySession(_ session: UnsafeMutableRawPointer?) { destroyFunction?(session) }

    func handleKey(_ session: UnsafeMutableRawPointer?, vk: Int32, character: Int32, modifiers: Int32, before: String?, after: String?) -> SessionResult? {
        guard let handleKeyFunction else { return nil }
        return withOptionalCString(before) { beforePointer in
            withOptionalCString(after) { afterPointer in
                decode(handleKeyFunction(session, vk, character, modifiers, beforePointer, afterPointer))
            }
        }
    }

    func commit(_ session: UnsafeMutableRawPointer?) -> SessionResult? {
        guard let commitFunction else { return nil }
        return decode(commitFunction(session))
    }

    /// 元の複数スカラーのイベントをアプリに渡す前に、未確定の内容だけ確定する。
    /// 結合文字から始まるイベントは、1 スカラーの場合と同じく直前の文字との境界を保つ。
    func commitBeforeExternalText(_ session: UnsafeMutableRawPointer?, text: String) -> SessionResult? {
        if let first = text.unicodeScalars.first {
            switch first.properties.generalCategory {
            case .nonspacingMark, .spacingMark, .enclosingMark:
                return handleKey(session, vk: 0x07, character: Int32(first.value), modifiers: 0, before: nil, after: nil)
            default:
                break
            }
        }
        return commit(session)
    }

    func selectCandidate(_ session: UnsafeMutableRawPointer?, index: Int) -> SessionResult? {
        guard let selectFunction else { return nil }
        return decode(selectFunction(session, Int32(index)))
    }

    func setDirect(_ session: UnsafeMutableRawPointer?, _ direct: Bool) {
        setDirectFunction?(session, direct ? 1 : 0)
    }

    func setCodeInput(_ session: UnsafeMutableRawPointer?, _ enabled: Bool) {
        setCodeInputFunction?(session, enabled ? 1 : 0)
    }

    /// 設定・学習データ・ユーザー辞書の保存場所。
    var dataDirectory: String? {
        guard let pointer = dataDirectoryFunction?() else { return nil }
        defer { freeFunction?(pointer) }
        return String(cString: pointer)
    }

    /// 不具合報告を開く URL (OS・版・実行環境を入れたもの)。
    var reportUrl: URL? {
        guard let pointer = "Mac".withCString({ reportUrlFunction?($0) }) else { return nil }
        defer { freeFunction?(pointer) }
        return URL(string: String(cString: pointer))
    }

    private func decode(_ pointer: UnsafeMutablePointer<CChar>?) -> SessionResult? {
        guard let pointer else { return nil }
        defer { freeFunction?(pointer) }
        let json = Data(bytes: pointer, count: strlen(pointer))
        do {
            return try JSONDecoder().decode(SessionResult.self, from: json)
        } catch {
            NSLog("Meltype: 結果を読めませんでした: %@", String(describing: error))
            return nil
        }
    }

    private func withOptionalCString<R>(_ text: String?, _ body: (UnsafePointer<CChar>?) -> R) -> R {
        guard let text else { return body(nil) }
        return text.withCString { body($0) }
    }
}
