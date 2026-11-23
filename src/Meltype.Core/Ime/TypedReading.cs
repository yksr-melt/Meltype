// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Detection;

namespace Meltype.Ime;

/// <summary>
/// 設定アプリのユーザー辞書の登録で打った読みを、ひらがなにする。読みはローマ字で打ってもよい (kigoutou → きごうとう)。
/// ユーザーのローマ字の表 (dataDirectory/dictionaries/romaji.txt) も使う。
/// </summary>
public sealed class TypedReading(string dataDirectory)
{
    private readonly RomajiDetector _romaji = RomajiDetector.CreateDefault(Path.Combine(dataDirectory, "dictionaries"));

    public string ToHiragana(string input)
    {
        var text = input.Trim();
        return text.Any(char.IsAsciiLetter) ? _romaji.ConvertLenient(text.ToLowerInvariant(), final: true) : text;
    }
}
