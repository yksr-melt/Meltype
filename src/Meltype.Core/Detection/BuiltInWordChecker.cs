// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>
/// 同梱のよく使う英単語の一覧 (dictionaries/english-words.txt、SCOWL から作成) で英単語かを調べる。
/// Windows のスペルチェッカーが使えない環境 (Mac 版・Linux 版・テスト) で、その代わりに使う。打ち間違いの自動修正は無い。
/// </summary>
public sealed class BuiltInWordChecker : IWordChecker
{
    private static readonly Lazy<BuiltInWordChecker> SharedInstance = new(() => new BuiltInWordChecker(DictionarySource.ReadEmbedded("english-words.txt")));

    /// <summary>同梱の一覧を読んだもの (最初に使うときに読む)。</summary>
    public static BuiltInWordChecker Shared => SharedInstance.Value;

    private readonly PackedWordSet _words;

    public BuiltInWordChecker(string text) => _words = PackedWordSet.FromLines(text);

    public bool IsAvailable => _words.Count > 0;

    public bool IsWord(string lower) => _words.Contains(lower);
    internal bool IsWord(ReadOnlySpan<char> lower) => _words.Contains(lower);
}
