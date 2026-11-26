// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Libraries.Lexicon;

namespace Meltype.Detection;

/// <summary>
/// 同梱のよく使う英単語の一覧 (dictionaries/english-words.txt と english-minerals.txt) で英単語かを調べる。
/// 全 OS で同じ判定にするため、OS のスペルチェッカーは使わない (#1508)。
/// </summary>
internal sealed class BuiltInWordChecker
{
    // 鉱物名は完全一致だけで読む。EnglishDetector の接頭辞に加えると、masuyite の先頭 masu などの日本語まで英語になる。
    // 予測 (DictionarySource.EnglishWords) には入れないので、判定用に別に読む
    private static readonly Lazy<BuiltInWordChecker> SharedInstance = new(() => new BuiltInWordChecker(
        PackedWordSet.FromLines(DictionarySource.ReadEmbedded("english-words.txt") + "\n" + DictionarySource.ReadEmbedded("english-minerals.txt"))));

    /// <summary>同梱の一覧を読んだもの (最初に使うときに読む)。</summary>
    public static BuiltInWordChecker Shared => SharedInstance.Value;

    private readonly PackedWordSet _words;

    private BuiltInWordChecker(PackedWordSet words) => _words = words;

    /// <summary>小文字の英単語 (a-z だけ) が、一覧にあるか。</summary>
    public bool IsWord(string lower) => _words.Contains(lower);
    internal bool IsWord(ReadOnlySpan<char> lower) => _words.Contains(lower);
}
