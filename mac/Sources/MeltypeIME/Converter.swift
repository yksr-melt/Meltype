// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation
import KanaKanjiConverterModuleWithDefaultDictionary

/// azooKey の変換エンジン (AzooKeyKanaKanjiConverter) で、ひらがなを漢字かな混じりに変換する。
/// azooKey の API が変わったときは、このファイルだけ直せばよいようにしてある。
final class MeltypeConverter {
    static let shared = MeltypeConverter()

    // 同梱の辞書 (KanaKanjiConverterModuleWithDefaultDictionary) を使う変換エンジン。
    // azooKey の withDefaultDictionary() は辞書を Bundle.module で探すが、Bundle.module は Meltype.app の直下か
    // ビルドしたマシンのフォルダーしか見ない。辞書のバンドルは Contents/Resources に入れているので、見つからずに落ちていた (#26)。
    // (Meltype.app の直下には置けない: 署名が通らない)。なので、辞書の場所をこちらで渡す。
    private let converter = KanaKanjiConverter(dicdataStore: DicdataStore(dictionaryURL: MeltypeConverter.resource("Dictionary")))
    private var options: ConvertRequestOptions
    /// Zenzai (ニューラルかな漢字変換) のモデル。無ければ Zenzai を使わない。
    private let zenzModel: URL?
    /// Zenzai の変換を待つ上限 (Windows の Zenzai と同じ 1500 ms)。超えたら Zenzai なしの変換を使う。ワーカーで待つので入力は止めない。
    private static let zenzaiTimeout: TimeInterval = 1.5
    /// これより長い読みは Zenzai に渡さない (Windows の Zenzai と同じ。長いほど推論が長い)。
    private static let zenzaiMaxReading = 80
    /// 打ち切ったときに Zenzai を使わない長さ (Windows の Zenzai と同じ 5 秒)。打ち切った変換が動いている間は .busy で断る。
    private static let zenzaiPause: TimeInterval = 5
    /// Zenzai はこの時刻まで使わない。
    private var zenzaiPausedUntil = Date.distantPast
    /// Zenzai の変換は、別の変換エンジンで別のスレッドで動かす (待つ時間に上限を付けるため。azooKey の変換は途中で止められない)。
    private let zenzaiWorker: ZenzaiWorker?
    private var resultCache: [String: [Candidate]] = [:]
    private var cacheOrder: [String] = []

    /// 同梱の辞書のバンドル (Contents/Resources に入れている) の中のフォルダー。
    private static func resource(_ name: String) -> URL {
        let bundleName = "AzooKeyKanaKanjiConverter_KanaKanjiConverterModuleWithDefaultDictionary.bundle"
        let url = (Bundle.main.resourceURL ?? Bundle.main.bundleURL).appendingPathComponent(bundleName, isDirectory: true)
        return (Bundle(url: url)?.resourceURL ?? url).appendingPathComponent(name, isDirectory: true)
    }

    /// 絵文字の辞書 (azooKey の withDefaultEmojiDictionary() と同じ選び方。こちらも Bundle.module を使わない)。
    private static func emojiDictionary() -> URL {
        let directory = resource("EmojiDictionary")
        if #available(macOS 15.3, *) { return directory.appendingPathComponent("emoji_all_E16.0.txt", isDirectory: false) }
        if #available(macOS 14.4, *) { return directory.appendingPathComponent("emoji_all_E15.1.txt", isDirectory: false) }
        return directory.appendingPathComponent("emoji_all_E15.0.txt", isDirectory: false)
    }

    /// zenz のモデル (gguf)。設定「Zenzai を使う」が OFF なら nil (issue #1130)。
    /// 設定「Zenzai: モデル (GGUF) の場所」があればそれを、空なら Meltype.app に同梱の Resources/zenz/ の .gguf を使う
    /// (名前は見ない: 版とファイル名は native/zenzai/zenzai.env だけに書く)。
    private static func findZenzModel() -> URL? {
        guard let configured = NativeCore.shared.zenzaiModelSetting else { return nil }
        let path = configured.trimmingCharacters(in: .whitespacesAndNewlines)
        if !path.isEmpty {
            let url = URL(fileURLWithPath: (path as NSString).expandingTildeInPath, isDirectory: false)
            if FileManager.default.fileExists(atPath: url.path) { return url }
            NSLog("Meltype: Zenzai のモデルが見つかりません: %@", url.path)
            return nil
        }
        guard let directory = Bundle.main.resourceURL?.appendingPathComponent("zenz", isDirectory: true),
              let names = try? FileManager.default.contentsOfDirectory(atPath: directory.path) else { return nil }
        return names.filter { $0.hasSuffix(".gguf") }.sorted().first.map { directory.appendingPathComponent($0, isDirectory: false) }
    }

    var usesZenzai: Bool { zenzModel != nil }

    private init() {
        // azooKey の学習データ・ユーザー辞書の置き場所 (Meltype では学習しない設定にしているので、ほぼ使わない)。
        let directory = (NativeCore.shared.dataDirectory.map { URL(fileURLWithPath: $0, isDirectory: true) }
            ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
                .appendingPathComponent("Meltype", isDirectory: true))
            .appendingPathComponent("azooKey", isDirectory: true)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        options = ConvertRequestOptions(
            requireJapanesePrediction: .disabled,
            requireEnglishPrediction: .disabled,
            keyboardLanguage: .ja_JP,
            learningType: .nothing,
            memoryDirectoryURL: directory,
            sharedContainerURL: directory,
            textReplacer: TextReplacer(emojiDataProvider: { MeltypeConverter.emojiDictionary() }),
            specialCandidateProviders: KanaKanjiConverter.defaultSpecialCandidateProviders,
            metadata: .init(versionString: "Meltype \(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "unknown")")
        )
        // init の中では self を閉包に入れられないので、ローカルの値で作る
        let model = Self.findZenzModel()
        let requestOptions = options
        zenzModel = model
        if let model {
            zenzaiWorker = ZenzaiWorker(dictionaryURL: MeltypeConverter.resource("Dictionary"), model: model, options: requestOptions)
        } else {
            zenzaiWorker = nil
        }
    }

    /// キャレットの前の文 (Zenzai の文脈に使う) は最後の 20 文字まで。
    private static func contextTail(_ context: String?) -> String? {
        guard let context, !context.isEmpty else { return nil }
        return String(context.suffix(20))
    }

    /// ひらがな全体に対する変換結果 (入力をすべて使ったものだけ、よい順)。
    /// Zenzai を使うときは、キャレットの前の文 (context) も文脈としてモデルに渡す (同じ読みでも前の文で変換が変わる)。
    private func results(for hiragana: String, context: String? = nil) -> [Candidate] {
        // Zenzai を使えるとき (止めていない・読みが長すぎない・前の変換が終わっている) だけ、上限付きで Zenzai に頼む
        let canUseZenzai = zenzaiWorker != nil && hiragana.count <= Self.zenzaiMaxReading
        let useZenzai = canUseZenzai && Date() >= zenzaiPausedUntil
        let tail = canUseZenzai ? Self.contextTail(context) : nil
        let key = canUseZenzai ? (tail ?? "") + "\u{1}" + hiragana : hiragana
        // 止めている間も、Zenzai の答えを控えていればそれを返す (同じ読みを聞き直して azooKey の答えに戻さない)
        if let cached = resultCache[key] { return cached }
        var composing = ComposingText()
        composing.insertAtCursorPosition(hiragana, inputStyle: .direct)
        var results: ConversionResult?
        if useZenzai, let zenzaiWorker {
            switch zenzaiWorker.convert(composing, leftContext: tail, timeout: Self.zenzaiTimeout) {
            case .done(let zenzai):
                results = zenzai
            case .timedOut:
                zenzaiPausedUntil = Date().addingTimeInterval(Self.zenzaiPause)
                NSLog("Meltype: Zenzai の変換が %.0f ms で終わらなかったので、5 秒間 Zenzai を使いません", Self.zenzaiTimeout * 1000)
            case .busy, .notReady:
                break
            }
        }
        let key2 = results == nil ? hiragana : key
        if results == nil, let cached = resultCache[key2] { return cached }
        let converted = results ?? converter.requestCandidates(composing, options: options)
        let count = hiragana.count
        // 入力 (ひらがな) をすべて使った候補だけ (.direct で入れたので、入力の文字数 = ひらがなの文字数)
        let candidates = converted.mainResults.filter { $0.composingCount == .inputCount(count) || $0.composingCount == .surfaceCount(count) }
        // Keep the engine's previous lattice for incremental conversion; bound retained results.
        if count <= 256 {
            if cacheOrder.count >= 64 {
                resultCache.removeValue(forKey: cacheOrder.removeFirst())
            }
            resultCache[key2] = candidates
            cacheOrder.append(key2)
        }
        return candidates
    }

    /// 文節に区切った変換結果 (読みをつなげると元のひらがなになる)。変換できなければ空。
    func clauses(for hiragana: String, context: String?) -> [(reading: String, text: String)] {
        guard let best = results(for: hiragana, context: context).first else { return [] }
        var clauses: [(reading: String, text: String)] = []
        for element in best.data {
            let reading = element.ruby.applyingTransform(.hiraganaToKatakana, reverse: true) ?? element.ruby
            // 助詞・助動詞 (ひらがなだけの短い語) は前の文節にくっつける (今日|は → 今日は)。
            if let last = clauses.last, element.word == reading, element.word.count <= 3 {
                clauses[clauses.count - 1] = (last.reading + reading, last.text + element.word)
            } else {
                clauses.append((reading, element.word))
            }
        }
        // 読みがずれた (長音などの扱いの違い) ときは、文節に分けずに全体で返す。
        if clauses.map(\.reading).joined() != hiragana {
            return [(hiragana, best.text)]
        }
        return clauses
    }

    /// 読みに対する候補の一覧 (重複なし、30 個まで)。
    func candidates(for reading: String) -> [String] {
        var seen = Set<String>()
        var list: [String] = []
        for candidate in results(for: reading) where seen.insert(candidate.text).inserted {
            list.append(candidate.text)
            if list.count >= 30 { break }
        }
        return list
    }
}

/// Zenzai の変換を、自分の変換エンジンで別のスレッド (直列のキュー) で動かす。
/// 呼ぶ側は上限の時間だけ待ち、終わらなければ Zenzai なしの変換を使う。終わらなかった変換は裏で最後まで動き、結果は捨てる。
/// 最初の変換 (モデルの読み込みを含む) は作ったときに裏で済ませ、済むまでは Zenzai を使わない。
private final class ZenzaiWorker: @unchecked Sendable {
    enum Outcome {
        case done(ConversionResult)
        case timedOut
        /// 前の変換がまだ動いている (打ち切った後など)
        case busy
        /// モデルを読み込み中
        case notReady
    }

    private let converter: KanaKanjiConverter
    private let model: URL
    private let options: ConvertRequestOptions
    private let queue = DispatchQueue(label: "Meltype.Zenzai")
    private let lock = NSLock()
    private var running = false
    private var ready = false

    init(dictionaryURL: URL, model: URL, options: ConvertRequestOptions) {
        converter = KanaKanjiConverter(dicdataStore: DicdataStore(dictionaryURL: dictionaryURL))
        self.model = model
        self.options = options
        queue.async { [self] in
            var warmup = ComposingText()
            warmup.insertAtCursorPosition("にほんご", inputStyle: .direct)
            _ = converter.requestCandidates(warmup, options: zenzaiOptions(leftContext: nil))
            locked { ready = true }
        }
    }

    // NSLock.withLock は macOS 14 から (Meltype は macOS 13 から動く) なので自分で書く
    private func locked<T>(_ body: () -> T) -> T {
        lock.lock()
        defer { lock.unlock() }
        return body()
    }

    private func zenzaiOptions(leftContext: String?) -> ConvertRequestOptions {
        var zenzai = options
        zenzai.zenzaiMode = .on(weight: model, inferenceLimit: 10, personalizationMode: nil,
                                versionDependentMode: .v3(.init(leftSideContext: leftContext, maxLeftSideContextLength: 20)))
        return zenzai
    }

    func convert(_ composing: ComposingText, leftContext: String?, timeout: TimeInterval) -> Outcome {
        let state: Outcome? = locked {
            if !ready { return .notReady }
            if running { return .busy }
            running = true
            return nil
        }
        if let state { return state }
        let done = DispatchSemaphore(value: 0)
        let box = ResultBox()
        let request = zenzaiOptions(leftContext: leftContext)
        queue.async { [self] in
            box.value = converter.requestCandidates(composing, options: request)
            locked { running = false }
            done.signal()
        }
        guard done.wait(timeout: .now() + timeout) == .success, let result = box.value else { return .timedOut }
        return .done(result)
    }

    private final class ResultBox: @unchecked Sendable {
        var value: ConversionResult?
    }
}
