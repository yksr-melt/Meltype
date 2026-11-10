// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Eisenferik

using Meltype.Detection;

namespace Meltype.Composition;

/// <summary>
/// よく使う語の読み (dictionaries/readings.txt と readings-extra.txt の「読み [TAB] 活用の種類」の読み)。
/// 読みの集合を一度だけ構築し、かな入力の判定とローマ字の打ち間違い直しで共有する。
/// </summary>
internal static class CommonReadings
{
    private static readonly Lazy<PackedWordSet> SharedWords = new(() => PackedWordSet.FromLines(ReadText(), firstField: true));

    /// <summary>読みの集合 (最初に使うときに読む)。</summary>
    public static PackedWordSet Words => SharedWords.Value;

    private static readonly Lazy<PackedWordSet> SharedKatakana = new(() => PackedWordSet.FromLines(DictionarySource.ReadEmbedded("katakana.txt")));

    /// <summary>カタカナで書く語の読み (dictionaries/katakana.txt)。よく使う語の印が無い語も含む。</summary>
    public static PackedWordSet Katakana => SharedKatakana.Value;

    private static readonly Lazy<PackedWordSet> SharedBrands = new(() => PackedWordSet.FromLines(
        string.Join('\n', DictionarySource.ReadEmbedded("brands.txt").Split('\n').Select(line => line.Split(' ')[0]))));

    /// <summary>英字で書く社名・サービス名の読み (dictionaries/brands.txt の ぎっとはぶ)。</summary>
    public static PackedWordSet Brands => SharedBrands.Value;

    private static readonly Lazy<PackedWordSet> SharedPhrases = new(() => PackedWordSet.FromLines(
        string.Join('\n', DictionarySource.DataLines(DictionarySource.ReadEmbedded("phrases.txt")).Select(line => line.Split('\t')[0].TrimStart('^')))));

    /// <summary>同梱の語句の読み (dictionaries/phrases.txt の どしたの。文節の頭の印 ^ は外す)。</summary>
    public static PackedWordSet Phrases => SharedPhrases.Value;

    /// <summary>活用形の生成に使う本文を読み直す。共有するのは読みの集合だけで、本文は保持しない。</summary>
    public static string ReadText() => DictionarySource.ReadEmbedded("readings.txt") + "\n" + DictionarySource.ReadEmbedded("readings-extra.txt");
}
