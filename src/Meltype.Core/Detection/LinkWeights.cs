// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>日英の区切りのラティスで、辺をつなぐときに数える特徴。</summary>
internal enum LinkFeature
{
    /// <summary>日本語と英語の切り替え。日本語の側が名詞に付く付属語 (が・を・に・で・する: github|で) なら数えない。</summary>
    Switch,

    /// <summary>英文の続き ("I want ") から日本語への切り替え。付属語でも数える。</summary>
    SentenceSwitch,

    /// <summary>英単語どうしがスペースなしで続く。</summary>
    AdjacentEnglish,

    /// <summary>英字 → 日本語 → 英字 と区切りなしに戻る (procras|ち|cle)。あいだが付属語を含むとき (excel|と|ppt) は除く。</summary>
    Sandwich,

    /// <summary>大文字で始まる知らない語を小文字の途中で切って、付属語でない日本語を続ける (Ta|なか)。</summary>
    NameSplit,

    /// <summary>ltu・xtu で打った っ の後ろに入力が続く (mai|ltu|kau)。かなの前の っ は子音を重ねて打つ。</summary>
    SpelledSokuon,

    /// <summary>
    /// 小文字で終わる英語のすぐ後ろの日本語が あ行のかな (thing|う・rate|ぃす) で始まる: 英単語の最後の子音と次の母音の切り間違い。
    /// USBいれた と、母音字で終わる英語の後ろの大きい あ行 (issue|ある) は除く。
    /// </summary>
    VowelAfterEnglish,

    /// <summary>子音を重ねて打った っ で、前後の片方がかなでない (excel|と|ppt の tt)。</summary>
    DanglingSokuon,
}

/// <summary>
/// <see cref="LinkFeature"/> の 1 回ごとの重み。作った後は変わらない。
/// 値を変えるときは、--tune-weights で品質テストの打鍵の例が崩れない範囲を見てから、単体テストと --expect (issue-cases.tsv) でも確かめる。
/// </summary>
internal sealed class LinkWeights
{
    public const int Count = (int)LinkFeature.DanglingSokuon + 1;

    public static readonly LinkWeights Default = new(new Dictionary<LinkFeature, double>
    {
        [LinkFeature.Switch] = 1,
        [LinkFeature.SentenceSwitch] = 2,
        [LinkFeature.AdjacentEnglish] = 1,
        [LinkFeature.Sandwich] = 1,
        [LinkFeature.NameSplit] = 3,
        [LinkFeature.SpelledSokuon] = 2,
        [LinkFeature.VowelAfterEnglish] = 1,
        // 語にならないかな 1 音と同じ重さにする (っ そのものの重み LightKanaCost と合わせて UnknownKanaCost)
        [LinkFeature.DanglingSokuon] = CompositionDetector.UnknownKanaCost - CompositionDetector.LightKanaCost,
    });

    private readonly double[] _values;

    private LinkWeights(IReadOnlyDictionary<LinkFeature, double> values)
    {
        _values = new double[Count];
        for (var i = 0; i < Count; i++) _values[i] = values[(LinkFeature)i];
    }

    private LinkWeights(double[] values) => _values = values;

    public double this[LinkFeature feature] => _values[(int)feature];

    /// <summary>1 つの特徴の重みだけを変えたもの。</summary>
    public LinkWeights With(LinkFeature feature, double value)
    {
        var values = (double[])_values.Clone();
        values[(int)feature] = value;
        return new LinkWeights(values);
    }

    public override string ToString() => string.Join(", ", Enum.GetValues<LinkFeature>().Select(f => $"{f} {this[f]}"));
}
