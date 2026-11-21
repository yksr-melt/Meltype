// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Libraries.Chars;

namespace Meltype.Engine;

/// <summary>
/// 文節 1 つ分の変換結果。Reading はその文節の読み (ひらがな)。
/// Zenzai は、この文字列が Zenzai の答えか (変換ボックスに「Zenzai」と出す。issue #1152)。
/// </summary>
public sealed record ConversionClause(string Reading, string Text, bool Zenzai = false);

/// <summary>
/// 助詞だけの文節の読み。変換エンジンは助詞だけの文節を漢字にしてしまう (github + に + push → 二) ので、かなのまま使う。
/// CompositionController (文節の候補) と HybridConverter (Zenzai の結果の当てはめ) で同じものを使う。
/// </summary>
internal static class JapaneseParticles
{
    private static readonly HashSet<string> Readings = ["は", "が", "を", "に", "で", "と", "も", "へ", "の", "や", "か", "から", "まで", "より"];

    internal static bool IsParticle(string reading) => Readings.Contains(reading);
}

/// <summary>変換エンジンへの依頼。</summary>
/// <param name="Hiragana">読み。</param>
/// <param name="Before">
/// キャレットの前の確定済みの文字列そのまま (変換の文脈として使うだけで、結果には含めない)。どこまでをどう使うかは変換エンジンが決める。
/// </param>
public sealed record ConversionRequest(string Hiragana, string? Before)
{
    /// <summary>
    /// Zenzai にも頼むか。英日の区切りの 2 番目以降の案と、文節ごとの候補に足す「その読みだけの変換」は頼まない。
    /// Zenzai は最後に頼まれた 1 件だけを裏で処理するので、案ごとに頼むと、いちばん良い案の依頼が後の案の依頼に上書きされて答えが遅れる。
    /// 文節ごとに頼むと、文節の数だけ待たされる (CPU のみで、文節に分かれた文の変換 1 回あたり中央値 98 ミリ秒増えた)。
    /// </summary>
    public bool Zenzai { get; init; } = true;

    /// <summary>
    /// 答えを待ってよい時刻 (Environment.TickCount64)。過ぎても答えが無ければ null を返し、答えができたら <see cref="IKanjiConverter.ResultReady"/> で知らせる。
    /// long.MaxValue なら答えるまで待つ (結果を後から受け取れないフロント: --repro・--self-test)。
    /// </summary>
    public long Deadline { get; init; } = long.MaxValue;

    /// <summary>deadline (Environment.TickCount64) までの残りのミリ秒。過ぎていれば 0。</summary>
    internal static int RemainingMs(long deadline) => (int)Math.Clamp(deadline - Environment.TickCount64, 0, int.MaxValue);
}

/// <summary>
/// ひらがな → 漢字かな混じりの変換。Meltype 自身は変換の辞書を持たず、全 OS で Mozc (と Zenzai) を使う (<see cref="HybridConverter"/>)。
/// </summary>
public interface IKanjiConverter
{
    /// <summary>文節に区切って変換する。変換できなければ (変換エンジンが使えない・Deadline までに答えが無い) null。</summary>
    IReadOnlyList<ConversionClause>? Convert(ConversionRequest request);

    /// <summary>ユーザーが確定した文節 (読みと文字列、区切りも確定したとおり) を覚えさせる。before はキャレットの前の確定済みの文字列そのまま。覚えない変換エンジンは何もしない。</summary>
    void Learn(string? before, IReadOnlyList<ConversionClause> clauses) { }

    /// <summary>
    /// Deadline の後に変換エンジンのキャッシュに結果が入った (または変換エンジンが使えるようになった)。ワーカーのスレッドで呼ぶ。
    /// 受け取った側は入力スレッドに移して RefreshConversion を呼ぶ。結果を渡さないので、古い読みの結果は表示に使われない。
    /// </summary>
    event Action? ResultReady { add { } remove { } }
}

/// <summary>キャレットの前の確定済みの文字列から、変換エンジンが文脈に使う部分を取り出す。</summary>
internal static class EngineContext
{
    /// <summary>Mozc の文脈: 同じ文の日本語の部分 (最大 10 文字)。前が日本語でなければ null。</summary>
    public static string? Sentence(string? before)
    {
        if (string.IsNullOrEmpty(before) || TextChars.LanguageOf(before) != false) return null;
        var start = before.AsSpan().LastIndexOfAny(SentenceEndSearch);
        var text = before[(start + 1)..].Trim();
        return text.Length == 0 ? null : text.Length > 10 ? text[^10..] : text;
    }

    /// <summary>
    /// Zenzai の文脈: 改行を除いた、文をまたぐ文章 (最大 100 文字)。英語もそのまま渡す (GitHub + にわたるこうじ → に渡る工事)。無ければ null。
    /// ニューラルモデルは前の文までの長い文脈ほど強い (文脈の無い きょう → 強 が、「昨日」だけで 今日 になる)。
    /// </summary>
    public static string? Passage(string? before)
    {
        var text = before?.Replace("\r", "").Replace("\n", "").Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > 100 ? text[^100..] : text;
    }

    private static readonly System.Buffers.SearchValues<char> SentenceEndSearch = System.Buffers.SearchValues.Create("。．！？\n\r");
}

