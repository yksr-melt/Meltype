// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Libraries.Lexicon;

namespace Meltype.Detection;

/// <summary>
/// 同梱のよく使う英単語の一覧 (dictionaries/english-words.txt、SCOWL から作成) で英単語かを調べる。
/// 全 OS で同じ判定にするため、OS のスペルチェッカーは使わない (#1508)。
/// </summary>
internal sealed class BuiltInWordChecker
{
    private static readonly Lazy<BuiltInWordChecker> SharedInstance = new(() => new BuiltInWordChecker(DictionarySource.EnglishWords));

    /// <summary>同梱の一覧を読んだもの (最初に使うときに読む)。</summary>
    public static BuiltInWordChecker Shared => SharedInstance.Value;

    private readonly PackedWordSet _words;

    private BuiltInWordChecker(PackedWordSet words) => _words = words;

    /// <summary>小文字の英単語 (a-z だけ) が、一覧にあるか。</summary>
    public bool IsWord(string lower) => _words.Contains(lower);
    internal bool IsWord(ReadOnlySpan<char> lower) => _words.Contains(lower);
}
