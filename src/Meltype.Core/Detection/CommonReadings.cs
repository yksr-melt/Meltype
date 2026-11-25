// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Eisenferik

using System.Text;
using Meltype.Detection;
using Meltype.Libraries.Lexicon;

namespace Meltype.Detection;

/// <summary>
/// よく使う語の読み (dictionaries/readings.txt と readings-extra.txt の「読み [TAB] 活用の種類」の読み)。
/// 読みの集合を一度だけ構築し、かな入力の判定とローマ字の打ち間違い直しで共有する。
/// </summary>
internal static class CommonReadings
{
    private static readonly Lazy<PackedWordSet> SharedWords = new(() => PackedWordSet.FromLines(ReadText(), firstField: true));

    /// <summary>読みの集合 (最初に使うときに読む)。</summary>
    public static PackedWordSet Words => SharedWords.Value;

    private static readonly Lazy<PackedWordSet> SharedIchidanStems = new(() => PackedWordSet.FromWords(
        DictionarySource.DataLines(ReadText())
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length >= 2 && parts[0].Length >= 2 && parts[0].EndsWith('る') &&
                            parts[1].Split(',').Contains("v1"))
            .Select(parts => parts[0][..^1])));

    /// <summary>一段動詞の語幹 (負け・褒め・しゃれ)。させる が使役形か する の活用かを見分ける。</summary>
    public static PackedWordSet IchidanStems => SharedIchidanStems.Value;

    private static readonly Lazy<PackedWordSet> SharedKatakana = new(() => PackedWordSet.FromLines(DictionarySource.ReadEmbedded("katakana.txt")));

    /// <summary>カタカナで書く語の読み (dictionaries/katakana.txt)。よく使う語の印が無い語も含む。</summary>
    public static PackedWordSet Katakana => SharedKatakana.Value;

    private static readonly Lazy<PackedWordSet> SharedNames = new(() => PackedWordSet.FromWords(
        DictionarySource.DataLines(DictionarySource.ReadEmbedded("names.txt")).Select(WithoutLongVowels)));

    /// <summary>日本人の姓・名の読み (dictionaries/names.txt)。長音を除いた形 (さとう → さと) で入れる。<see cref="WithoutLongVowels"/> で引く。</summary>
    public static PackedWordSet Names => SharedNames.Value;

    /// <summary>
    /// お段の後ろの う・お と、う段の後ろの う を除く (さとう・おおの・ゆうき → さと・おの・ゆき)。
    /// ヘボン式のローマ字は長音を書かない (Sato・Ono・Yuki) ので、打った綴りの読みと名前の読みを、両方この形にして比べる。
    /// </summary>
    public static string WithoutLongVowels(string kana)
    {
        var result = new StringBuilder(kana.Length);
        foreach (var c in kana)
        {
            var previous = result.Length > 0 ? result[^1] : '\0';
            var oRow = "おこそとのほもよろをごぞどぼぽょぉ".Contains(previous);
            var uRow = "うくすつぬふむゆるぐずづぶぷゅぅ".Contains(previous);
            if (c == 'う' && (oRow || uRow) || c == 'お' && oRow) continue;
            result.Append(c);
        }
        return result.ToString();
    }

    private static readonly Lazy<PackedWordSet> SharedBrands = new(() => PackedWordSet.FromLines(
        string.Join('\n', DictionarySource.ReadEmbedded("brands.txt").Split('\n').Select(line => line.Split(' ')[0]))));

    /// <summary>英字で書く社名・サービス名の読み (dictionaries/brands.txt の ぎっとはぶ)。</summary>
    public static PackedWordSet Brands => SharedBrands.Value;

    private static readonly Lazy<PackedWordSet> SharedPhrases = new(() => PackedWordSet.FromLines(
        string.Join('\n', DictionarySource.DataLines(DictionarySource.ReadEmbedded("phrases.txt")).Select(line => line.Split('\t')[0].TrimStart('^')))));

    /// <summary>同梱の語句の読み (dictionaries/phrases.txt の どしたの。文節の頭の印 ^ は外す)。</summary>
    public static PackedWordSet Phrases => SharedPhrases.Value;

    private static readonly Lazy<PackedWordSet> SharedRare = new(() => PackedWordSet.FromLines(RareText(), firstField: true));

    /// <summary>よく使う語でない語の 3 文字以上の読み (dictionaries/readings-rare.txt の ろんぱ・かんれんづける)。日英の区切りだけで使い、打ち間違い直しには使わない。</summary>
    public static PackedWordSet Rare => SharedRare.Value;

    /// <summary>readings-rare.txt の本文を読み直す (活用形の生成に使う)。</summary>
    public static string RareText() => DictionarySource.ReadEmbedded("readings-rare.txt");

    /// <summary>活用形の生成に使う本文を読み直す。共有するのは読みの集合だけで、本文は保持しない。</summary>
    public static string ReadText() => DictionarySource.ReadEmbedded("readings.txt") + "\n" + DictionarySource.ReadEmbedded("readings-extra.txt");
}
