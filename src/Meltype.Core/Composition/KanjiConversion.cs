// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

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

/// <summary>
/// ひらがな → 漢字かな混じりの変換。Meltype 自身は変換の辞書を持たず、全 OS で Mozc (と Zenzai) を使う (<see cref="HybridConverter"/>)。
/// </summary>
public interface IKanjiConverter
{
    /// <summary>全体を 1 回で変換する。変換できなければ null。before は <see cref="ConvertClauses"/> と同じ。</summary>
    string? Convert(string hiragana, string? before);

    /// <summary>
    /// 文節に区切って変換する。区切れなければ null。before はキャレットの前の確定済みの文字列そのまま (変換の文脈として使うだけで、結果には含めない)。
    /// どこまでをどう使うかは変換エンジンが決める。
    /// </summary>
    IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before);
}

/// <summary>キャレットの前の確定済みの文字列から、変換エンジンが文脈に使う部分を取り出す。</summary>
internal static class EngineContext
{
    /// <summary>Mozc の文脈: 同じ文の日本語の部分 (最大 10 文字)。前が日本語でなければ null。</summary>
    public static string? Sentence(string? before)
    {
        if (string.IsNullOrEmpty(before) || CompositionController.LanguageOf(before) != false) return null;
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

/// <summary>
/// Zenzai を使わずに変換できる変換エンジン (HybridConverter)。
/// ConvertStandalone は文節ごとの候補に足す「その読みだけの変換」。文節の数だけ頼むので、Zenzai に頼むと 1 文節ごとに待たされる
/// (CPU のみで、文節に分かれた文の変換 1 回あたり中央値 98 ミリ秒増えた)。
/// ConvertClausesStandalone は英日の区切りの案 (2 番目以降) の変換。Zenzai は最後に頼まれた 1 件だけを裏で処理するので、
/// 案ごとに頼むと、いちばん良い案の依頼が後の案の依頼に上書きされて、その答えが遅れる。
/// 最初の候補 (文の中での変換結果) は ConvertClauses のままなので、Zenzai の結果が使われる。
/// </summary>
public interface IStandaloneConverter
{
    string? ConvertStandalone(string hiragana);

    IReadOnlyList<ConversionClause>? ConvertClausesStandalone(string hiragana, string? before);
}

/// <summary>
/// 入力スレッドが変換エンジンを待ってよい時間。入力スレッドからだけ使う。
/// Enable するまでは制限なし。結果を受け取る経路を持つフロントだけが Enable する (--repro・--self-test は答えが出るまで待つ)。
/// </summary>
public sealed class ConversionBudget
{
    private long _deadline = long.MaxValue;

    public bool Enabled { get; private set; }

    public int RemainingMs => (int)Math.Clamp(_deadline - Environment.TickCount64, 0, int.MaxValue);

    public void Enable()
    {
        Enabled = true;
        _deadline = Environment.TickCount64;
    }

    public void Start(int milliseconds)
    {
        if (Enabled) _deadline = Environment.TickCount64 + milliseconds;
    }

    /// <summary>convert の間は milliseconds までしか待たない (ライブ変換は 0。代わりの変換エンジンに待つ時間を残すときも使う)。</summary>
    public T WithLimit<T>(int milliseconds, Func<T> convert)
    {
        if (!Enabled) return convert();
        var previous = _deadline;
        _deadline = Math.Min(previous, Environment.TickCount64 + milliseconds);
        try { return convert(); }
        finally { _deadline = previous; }
    }
}

/// <summary>結果をワーカーで作り、できたら知らせる変換エンジン。</summary>
public interface IBackgroundConverter
{
    ConversionBudget Budget { get; }

    /// <summary>
    /// 変換エンジンのキャッシュに結果が入った (または変換エンジンが使えるようになった)。ワーカーのスレッドで呼ぶ。
    /// 受け取った側は入力スレッドに移して RefreshConversion を呼ぶ。結果を渡さないので、古い読みの結果は表示に使われない。
    /// </summary>
    event Action? ResultReady;
}

/// <summary>確定した変換を覚えられる変換エンジン (Mozc)。次から同じ読みで同じ文字列を先に出す。</summary>
public interface ILearningConverter
{
    /// <summary>ユーザーが確定した文節 (読みと文字列、区切りも確定したとおり) を覚えさせる。before はキャレットの前の確定済みの文字列そのまま。</summary>
    void Learn(string? before, IReadOnlyList<ConversionClause> clauses);
}
