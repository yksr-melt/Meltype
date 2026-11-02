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
        zenzModel = Self.findZenzModel()
    }

    /// キャレットの前の文 (Zenzai の文脈に使う) は最後の 20 文字まで。
    private static func contextTail(_ context: String?) -> String? {
        guard let context, !context.isEmpty else { return nil }
        return String(context.suffix(20))
    }

    /// ひらがな全体に対する変換結果 (入力をすべて使ったものだけ、よい順)。
    /// Zenzai を使うときは、キャレットの前の文 (context) も文脈としてモデルに渡す (同じ読みでも前の文で変換が変わる)。
    private func results(for hiragana: String, context: String? = nil) -> [Candidate] {
        let tail = zenzModel == nil ? nil : Self.contextTail(context)
        let key = tail.map { $0 + "\u{1}" + hiragana } ?? hiragana
        if let cached = resultCache[key] { return cached }
        var composing = ComposingText()
        composing.insertAtCursorPosition(hiragana, inputStyle: .direct)
        if let zenzModel {
            options.zenzaiMode = .on(weight: zenzModel, inferenceLimit: 10, personalizationMode: nil,
                                     versionDependentMode: .v3(.init(leftSideContext: tail, maxLeftSideContextLength: 20)))
        }
        let results = converter.requestCandidates(composing, options: options)
        let count = hiragana.count
        // 入力 (ひらがな) をすべて使った候補だけ (.direct で入れたので、入力の文字数 = ひらがなの文字数)
        let candidates = results.mainResults.filter { $0.composingCount == .inputCount(count) || $0.composingCount == .surfaceCount(count) }
        // Keep the engine's previous lattice for incremental conversion; bound retained results.
        if count <= 256 {
            if cacheOrder.count >= 64 {
                resultCache.removeValue(forKey: cacheOrder.removeFirst())
            }
            resultCache[key] = candidates
            cacheOrder.append(key)
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
