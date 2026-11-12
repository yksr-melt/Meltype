// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using System.Xml.Linq;

namespace Meltype.Tests;

/// <summary>
/// Unicode CLDR の絵文字の日本語の名前・キーワード (common/annotations/ja.xml, annotationsDerived/ja.xml) から、
/// 絵文字の辞書 dictionaries/emoji-cldr.txt を作る道具。読みは Microsoft IME の変換エンジンの逆変換で求める。
///
///   dotnet run --project src/Meltype.Windows.Tests -- --gen-emoji ja.xml ja-derived.xml dictionaries/emoji-cldr.txt
/// </summary>
internal static class EmojiGenerator
{
    /// <summary>1 つの読みに並べる絵文字の上限 (「かお」のような広い語で候補が長くなりすぎないように)。</summary>
    private const int MaxPerReading = 16;

    public static void Run(string annotations, string derived, string output)
    {
        using var converter = new Composition.MsImeKanjiConverter();
        var readingCache = new Dictionary<string, string?>();
        string? ReadingOf(string term)
        {
            if (readingCache.TryGetValue(term, out var cached)) return cached;
            var text = term.Trim();
            string? reading = IsKana(text) ? ToHiragana(text) : converter.Reading(text) is { } r ? ToHiragana(r) : null;
            // 読みはひらがなと長音だけ (英字や記号が残るものは使わない)。
            if (reading is not null && (reading.Length < 2 || !reading.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー'))) reading = null;
            readingCache[term] = reading;
            return reading;
        }

        var entries = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string reading, string emoji)
        {
            if (!entries.TryGetValue(reading, out var list)) entries[reading] = list = [];
            if (!list.Contains(emoji)) list.Add(emoji);
        }

        var count = 0;
        foreach (var file in new[] { annotations, derived })
        {
            foreach (var annotation in XDocument.Load(file).Descendants("annotation"))
            {
                var emoji = (string?)annotation.Attribute("cp");
                if (emoji is null || HasSkinTone(emoji)) continue;
                var isName = (string?)annotation.Attribute("type") == "tts";
                foreach (var raw in annotation.Value.Split('|'))
                {
                    var term = raw.Trim();
                    // 国旗など「旗: 日本」の形の名前は、後ろの部分 (日本) と「日本の旗」の両方を読みにする。
                    var colon = term.IndexOf(": ", StringComparison.Ordinal);
                    if (colon >= 0)
                    {
                        var subject = term[(colon + 2)..];
                        if (ReadingOf(subject) is { } subjectReading) Add(subjectReading, emoji);
                        if (ReadingOf(subject + "の" + term[..colon]) is { } fullReading) Add(fullReading, emoji);
                        continue;
                    }
                    if (ReadingOf(term) is { } reading) Add(reading, emoji);
                }
                if (isName) count++;
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Meltype 絵文字の辞書 (Unicode CLDR の日本語の名前・キーワードから自動生成)");
        builder.AppendLine("# 形式: 読み<Tab>絵文字<Tab>絵文字 …  (手で書いた emoji.txt の候補の後に並ぶ)");
        builder.AppendLine("# 元データ: Unicode CLDR common/annotations/ja.xml, common/annotationsDerived/ja.xml");
        builder.AppendLine("# Copyright © 1991-2026 Unicode, Inc. Unicode License v3 (SPDX: Unicode-3.0)。全文は THIRD-PARTY-NOTICES.md。");
        builder.AppendLine("# 読みは Microsoft IME の変換エンジンの逆変換で求めた。作り直し: dotnet run --project src/Meltype.Windows.Tests -- --gen-emoji …");
        foreach (var (reading, list) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            builder.Append(reading);
            foreach (var emoji in list.Take(MaxPerReading)) builder.Append('\t').Append(emoji);
            builder.Append('\n');
        }
        File.WriteAllText(output, builder.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"絵文字 {count} 個、読み {entries.Count} 個を {output} に書きました。");
    }

    private static bool HasSkinTone(string emoji)
    {
        for (var i = 0; i < emoji.Length; i++)
        {
            if (char.IsHighSurrogate(emoji[i]) && i + 1 < emoji.Length && char.ConvertToUtf32(emoji[i], emoji[i + 1]) is >= 0x1F3FB and <= 0x1F3FF) return true;
        }
        return false;
    }

    private static bool IsKana(string text) => text.Length > 0 && text.All(c => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヺ' or 'ー');

    private static string ToHiragana(string text) =>
        new(text.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());
}
