// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>
/// 同梱のよく使う英単語の一覧 (dictionaries/english-words.txt、SCOWL から作成) で英単語かを調べる。
/// 全 OS で同じ判定にするため、OS のスペルチェッカーは使わない (#1508)。
/// </summary>
public sealed class BuiltInWordChecker
{
    private static readonly Lazy<BuiltInWordChecker> SharedInstance = new(() => new BuiltInWordChecker(DictionarySource.ReadEmbedded("english-words.txt")));

    /// <summary>同梱の一覧を読んだもの (最初に使うときに読む)。</summary>
    public static BuiltInWordChecker Shared => SharedInstance.Value;

    private readonly PackedWordSet _words;

    private BuiltInWordChecker(string text) => _words = PackedWordSet.FromLines(text);

    /// <summary>小文字の英単語 (a-z だけ) が、一覧にあるか。</summary>
    public bool IsWord(string lower) => _words.Contains(lower);
    internal bool IsWord(ReadOnlySpan<char> lower) => _words.Contains(lower);
}
