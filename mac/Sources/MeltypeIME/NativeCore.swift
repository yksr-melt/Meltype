// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import Foundation

// libMeltypeNative.dylib (C# の Meltype.Core を NativeAOT にしたもの) の関数。src/Meltype.Mac.Native/Exports.cs と合わせる。
typealias IsWordCallback = @convention(c) (UnsafePointer<CChar>?) -> Int32

private typealias InitFunction = @convention(c) (UnsafePointer<CChar>?, IsWordCallback) -> Int32
private typealias CreateFunction = @convention(c) () -> UnsafeMutableRawPointer?
private typealias DestroyFunction = @convention(c) (UnsafeMutableRawPointer?) -> Void
private typealias HandleFunction = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias DataDirectoryFunction = @convention(c) () -> UnsafeMutablePointer<CChar>?
private typealias ReportUrlFunction = @convention(c) (UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias FreeFunction = @convention(c) (UnsafeMutableRawPointer?) -> Void
private typealias ConversionReadyCallback = @convention(c) () -> Void
private typealias SetConversionReadyFunction = @convention(c) (ConversionReadyCallback) -> Void
private typealias ShutdownFunction = @convention(c) () -> Void

// ---- 本体から呼ばれる関数 ----

/// 背景の変換の結果が届いた (ワーカーのスレッドで呼ばれる)。
private let conversionReadyCallback: ConversionReadyCallback = {
    DispatchQueue.main.async { MeltypeInputController.active?.refreshConversion() }
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

/// 本体の応答 (ImeOutput) のうち、Mac で使う項目。
struct ImeOutput: Decodable {
    let state: ImeState
    let result: SessionResult
}

/// 打ったら効く言語モード (表示する「あ / A」)
enum LanguageMode: String, Decodable {
    case japanese = "Japanese"
    case alphanumeric = "Alphanumeric"
}

struct ImeState: Decodable {
    let languageMode: LanguageMode
    /// 入力モードを表示する時間 (ミリ秒。0 なら出さない)
    let modeIndicatorMs: Int
}

struct SessionResult: Decodable {
    let consumed: Bool
    let commits: [TextEdit]
    let view: CompositionView?
}

/// キャレットの前の removeBefore (本体が照合済み) を消してから text を入れる
struct TextEdit: Decodable {
    let text: String
    let removeBefore: String?
}

struct CompositionView: Decodable {
    let text: String
    let converting: Bool
    let clauses: [String]
    let selectedClause: Int
    let hint: String
    let spellingSuggestion: String?
    /// 打ったキー (見えている文字と違うときだけ)
    let typedKeys: String?
    /// 一覧に出す候補 (出さないなら nil)
    let candidates: CandidateList?
}

struct CandidateList: Decodable {
    let items: [String]
    /// 候補ごとの注記 (無ければ nil)
    let notes: [String?]
    let selected: Int
    /// 選んでいる候補の意味。候補で meaningDelayMs 止まったら注釈に出す
    let selectedMeaning: String?
    let meaningDelayMs: Int
}

/// libMeltypeNative.dylib を読み込んで呼ぶ。Meltype.app/Contents/Frameworks に置く (build.sh)。
final class NativeCore {
    static let shared = NativeCore()
    var spellCheckCount: Int { spellCacheLock.withLock { spellChecks } }

    private let library: UnsafeMutableRawPointer?
    private let initFunction: InitFunction?
    private let createFunction: CreateFunction?
    private let destroyFunction: DestroyFunction?
    private let handleFunction: HandleFunction?
    private let dataDirectoryFunction: DataDirectoryFunction?
    private let reportUrlFunction: ReportUrlFunction?
    private let freeFunction: FreeFunction?
    private let setConversionReadyFunction: SetConversionReadyFunction?
    private let shutdownFunction: ShutdownFunction?

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
        createFunction = symbol("meltype_ime_create", as: CreateFunction.self)
        destroyFunction = symbol("meltype_ime_destroy", as: DestroyFunction.self)
        handleFunction = symbol("meltype_ime_handle", as: HandleFunction.self)
        dataDirectoryFunction = symbol("meltype_data_directory", as: DataDirectoryFunction.self)
        reportUrlFunction = symbol("meltype_report_url", as: ReportUrlFunction.self)
        freeFunction = symbol("meltype_free", as: FreeFunction.self)
        setConversionReadyFunction = symbol("meltype_set_conversion_ready", as: SetConversionReadyFunction.self)
        shutdownFunction = symbol("meltype_shutdown", as: ShutdownFunction.self)
    }

    /// 本体に英単語の判定を登録し、変換エンジンを用意する。Mozc のヘルパー (mozc/) と Zenzai (zenzai/) は Contents/Resources に置く (build.sh)。
    func initialize() {
        guard let initFunction else { return }
        let resources = Bundle.main.resourcePath ?? ""
        if resources.withCString({ initFunction($0, isWordCallback) }) == 0 {
            NSLog("Meltype: Mozc を使えません (漢字変換はひらがなのままになります)")
        }
    }

    /// 終了する前に呼ぶ。変換エンジンを止める (Zenzai の llama-server を終わらせ、Mozc の学習を保存する)。
    func shutdown() {
        shutdownFunction?()
    }

    /// 変換エンジンを入力のスレッドで待たないようにする (入力欄を作る前に呼ぶ)。呼ばなければ変換は同期のまま (--self-test)。
    func startBackgroundConversion() {
        setConversionReadyFunction?(conversionReadyCallback)
    }

    /// 入力欄 (Input Method Kit のクライアント) ごとの本体 (ImeHost)。
    func createHost() -> UnsafeMutableRawPointer? { createFunction?() }

    func destroyHost(_ host: UnsafeMutableRawPointer?) { destroyFunction?(host) }

    /// 出来事 (ImeEvent) を送って応答を受け取る。本体が処理できなければ nil。
    func send(_ host: UnsafeMutableRawPointer?, _ event: [String: Any]) -> ImeOutput? {
        guard let handleFunction, let host,
              let data = try? JSONSerialization.data(withJSONObject: event),
              let json = String(data: data, encoding: .utf8) else { return nil }
        guard let pointer = json.withCString({ handleFunction(host, $0) }) else { return nil }
        defer { freeFunction?(pointer) }
        do {
            return try JSONDecoder().decode(ImeOutput.self, from: Data(bytes: pointer, count: strlen(pointer)))
        } catch {
            NSLog("Meltype: 応答を読めませんでした: %@", String(describing: error))
            return nil
        }
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
}
