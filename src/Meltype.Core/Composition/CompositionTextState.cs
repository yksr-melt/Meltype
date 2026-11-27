// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Collections.Immutable;
using System.Text;
using Meltype.Detection;

namespace Meltype.Composition;

/// <summary>打ったものの 1 要素。打った文字 (<see cref="Raw"/>) は 1 要素 1 文字。</summary>
internal abstract record TypedKey
{
    private TypedKey() { }

    public abstract char Raw { get; }

    /// <summary>この要素を、前の要素までの読み reading に足す。</summary>
    internal abstract void ReadInto(TextReading reading, TextEnv env);

    /// <summary>ローマ字入力で打った文字 (英字・数字・記号・空白)。</summary>
    public sealed record Letter(char Char) : TypedKey
    {
        public override char Raw => Char;
        internal override void ReadInto(TextReading reading, TextEnv env) => CompositionText.ReadLetter(reading, env, Char);
    }

    /// <summary>かな入力 (JIS) のキー。Key は打ったキーの英字・記号。</summary>
    public sealed record Kana(char Key, char Value) : TypedKey
    {
        public override char Raw => Key;
        internal override void ReadInto(TextReading reading, TextEnv env) => CompositionText.ReadKana(reading, Key, Value);
    }

    /// <summary>
    /// BackSpace で後ろを消しても残した音 (matcha + BackSpace の っ)。後ろのキーを見て決めた音は、残った打ったものだけを読み直すと変わる (mat は ま + t) ので、見えていた音のまま持つ。
    /// </summary>
    public sealed record Kept(char Key, string Value) : TypedKey
    {
        public override char Raw => Key;
        internal override void ReadInto(TextReading reading, TextEnv env) => CompositionText.ReadKept(reading, env, Key, Value);
    }

    /// <summary>残した音 (<see cref="Kept"/>) の 2 文字目からの打った文字 (kyo の y・o)。残した音の単位に打った文字を足す。</summary>
    public sealed record KeptRaw(char Key) : TypedKey
    {
        public override char Raw => Key;
        internal override void ReadInto(TextReading reading, TextEnv env) => reading.Units[^1] = reading.Units[^1] with { Raw = reading.Units[^1].Raw + Key };
    }

    /// <summary>キー以外から入った読み (選んだ候補の読み・再変換・NICOLA・打ち直した文節・もしかしての修正)。</summary>
    public sealed record Reading(char Value) : TypedKey
    {
        public override char Raw => Value;
        internal override void ReadInto(TextReading reading, TextEnv env) => CompositionText.ReadKana(reading, Value, Value);
    }
}

/// <summary>
/// 1 回の入力で打ったもの (書き換えない値)。単位・入力途中の子音は、打ったものと今の env から <see cref="CompositionText.Read"/> で作る。
/// </summary>
internal sealed record CompositionTextState
{
    public ImmutableList<TypedKey> Keys { get; init; } = [];

    /// <summary>
    /// 読みの途中を直している間の、カーソルの位置 (前にある打った要素の数。Esc でひらがなに戻してから ← で動かした)。直していなければ null。
    /// カーソルより後ろは、見えていた読みの単位どおりの要素 (<see cref="TypedKey.Kept"/>・<see cref="TypedKey.KeptRaw"/>)。
    /// 打つ・消すのはカーソルの位置で、読むのはカーソルより前だけ。ほかのキー・確定の前にカーソルを外す (<see cref="CompositionText.MergeTail"/>)。
    /// </summary>
    public int? Caret { get; init; }

    public DisplayMode Mode { get; init; } = DisplayMode.Auto;

    /// <summary>英字 (F9 / F10) で見せるときの大文字・小文字。</summary>
    public LetterCase Case { get; init; } = LetterCase.AsTyped;

    /// <summary>この入力だけ自動判定を使う (自動判定を使わないときに Tab で提案を受け入れた)。</summary>
    public bool SuggestionAccepted { get; init; }

    /// <summary>
    /// 確定・変換に進んだ (Space・Enter など) ときに打ってあった範囲。その範囲は、表示の形 (F6〜F10) を変えても続けて打っても、進んだときの読み
    /// (打ち間違いの直し・笑いの w・入力途中の子音を確定した形) を使う。範囲の後ろに打った英字は、範囲の境で切って読む。
    /// </summary>
    public Settlement? Settled { get; init; }

    /// <summary>かな入力 (JIS) か。</summary>
    public bool KanaInput { get; init; }

    /// <summary>
    /// この入力の打ち始めの / を中黒 (・) にするか (文章の入力欄で打ち始めた: 本体が入力を始めるときに決める)。
    /// 入力の間は変えないので、BackSpace で原文を打ち直しても同じ結果になる。
    /// </summary>
    public bool SlashAtStart { get; init; }

    /// <summary>( ) を全角にするか (設定「カッコを全角で入力」)。</summary>
    public bool FullWidthParentheses { get; init; }

    /// <summary>[ ] を「」ではなく［］にするか (設定)。</summary>
    public bool SquareBrackets { get; init; }

    public bool IsEmpty => Keys.IsEmpty;

    /// <summary>打った文字そのもの。</summary>
    public string TypedRaw => string.Concat(Keys.Take(Caret ?? Keys.Count).Select(k => k.Raw));
}

/// <summary>
/// 確定・変換に進んだときの範囲: 打ったものの先頭 Count 要素を、そのときの表示の形 Mode で読んだ形に決める。
/// Previous は、その前に進んだときの範囲 (続けて打って、また Space を押した)。範囲を読むときに使う。
/// </summary>
internal sealed record Settlement(int Count, DisplayMode Mode, Settlement? Previous);

/// <summary>読んだ結果を決める env の値。どれかが変わったら読み直す。</summary>
internal readonly record struct ReadingKey(CompositionDetector Detector, Config.Settings Settings, RomajiTypoCorrector? RomajiTypos, FragmentOrigin? Fragment, TextContext Context, LanguageMemory? Memory, int MemoryVersion);

/// <summary>
/// 打ったもの (<see cref="State"/>) を読んだもの: かな 1 音ずつの単位と、まだ音にならない子音 (k, ky, n …)。
/// <see cref="CompositionText.Read"/> が作る間だけ書き換え、作った後は書き換えない。
/// </summary>
internal sealed class TextReading(CompositionTextState state)
{
    public CompositionTextState State { get; } = state;

    public readonly List<CompositionUnit> Units = [];

    public readonly StringBuilder Pending = new();

    /// <summary>確定・変換に進んだ範囲 (<see cref="CompositionTextState.Settled"/>) を読んだ単位の数。この単位は、後ろのキーを読むときも打ち間違いを直すときも変えない。</summary>
    public int SettledUnits { get; set; }

    /// <summary>残した音 (<see cref="TypedKey.Kept"/>) までの単位の数。この単位は、後ろに打った英字と合わせて読み直さない。</summary>
    public int KeptUnits { get; set; }

    public DisplayMode Mode => State.Mode;
    public LetterCase Case => State.Case;
    public bool SuggestionAccepted => State.SuggestionAccepted;
    public bool KanaInput => State.KanaInput;
    public bool SlashAtStart => State.SlashAtStart;
    public bool FullWidthParentheses => State.FullWidthParentheses;
    public bool SquareBrackets => State.SquareBrackets;

    public TextReading Clone(CompositionTextState state)
    {
        var copy = new TextReading(state) { SettledUnits = SettledUnits, KeptUnits = KeptUnits };
        copy.Units.AddRange(Units);
        copy.Pending.Append(Pending);
        return copy;
    }
}

/// <summary>最近入れた count 個だけを持つ控え。古いものから捨てる。</summary>
internal sealed class Recent<TKey, TValue>(int count, IEqualityComparer<TKey> comparer) where TKey : notnull where TValue : class
{
    private readonly TKey?[] keys = new TKey?[count];
    private readonly TValue?[] values = new TValue?[count];
    private int next;

    private int IndexOf(TKey key)
    {
        for (var i = 0; i < keys.Length; i++)
        {
            if (values[i] is not null && comparer.Equals(keys[i]!, key)) return i;
        }
        return -1;
    }

    public bool TryGetValue(TKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TValue? value)
    {
        var i = IndexOf(key);
        value = i >= 0 ? values[i] : null;
        return value is not null;
    }

    public void Set(TKey key, TValue value)
    {
        var i = IndexOf(key);
        if (i < 0)
        {
            i = next;
            next = (next + 1) % keys.Length;
        }
        (keys[i], values[i]) = (key, value);
    }

    public void Remove(TKey key)
    {
        var i = IndexOf(key);
        if (i >= 0) (keys[i], values[i]) = (default, null);
    }
}
