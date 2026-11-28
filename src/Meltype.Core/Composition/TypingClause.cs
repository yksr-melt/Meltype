// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>
/// 変換していない入力 (打っている間) の文節 1 つ。表示・確定の文字・確定の記録は、同じ文節の並び (<see cref="CompositionText.Clauses"/>) から作る。
/// 入力途中の子音は、それが入っている区間の文節が持つ (bug|w の g は英語の文節の打った文字、w は日本語の文節の Pending)。
/// すべての種類で場合分けするときは <see cref="Match"/> を使う。
/// </summary>
internal abstract record TypingClause
{
    private TypingClause() { }

    /// <summary>確定の記録の読み。</summary>
    public abstract string Reading { get; }

    internal abstract T Match<T>(Cases<T> cases);

    internal readonly record struct Cases<T>
    {
        public required Func<Kana, T> Kana { get; init; }
        public required Func<Converted, T> Converted { get; init; }
        public required Func<Typed, T> Typed { get; init; }
        public required Func<Literal, T> Literal { get; init; }
    }

    /// <summary>日本語の区間の文節 (ライブ変換の答え)。Pending は読みの後ろの入力途中の子音 (区間の最後の文節だけ)。</summary>
    public abstract record Japanese : TypingClause
    {
        private protected Japanese() { }

        public abstract string Pending { get; }

        /// <summary>区間の最後の文節に、区間に入っている入力途中の子音を持たせる。</summary>
        public abstract Japanese WithPending(string pending);
    }

    /// <summary>変換しなかった日本語の読み (ライブ変換が OFF・1 拍の区間)。</summary>
    public sealed record Kana(string Reading, string Pending = "") : Japanese
    {
        public override string Reading { get; } = Reading;
        public override string Pending { get; } = Pending;
        public override Japanese WithPending(string pending) => new Kana(Reading, pending);
        internal override T Match<T>(Cases<T> cases) => cases.Kana(this);
    }

    /// <summary>ライブ変換で変換した文節。Engine は変換エンジンの答え。</summary>
    public sealed record Converted(string Reading, string? Engine, string Text, string Pending = "") : Japanese
    {
        public override string Reading { get; } = Reading;
        public override string Pending { get; } = Pending;
        public override Japanese WithPending(string pending) => new Converted(Reading, Engine, Text, pending);
        internal override T Match<T>(Cases<T> cases) => cases.Converted(this);
    }

    /// <summary>英語・保護区間の区間。Span は打った文字に見せる表記を当てたもの。</summary>
    public sealed record Typed(string Reading, TextSpan Span) : TypingClause
    {
        public override string Reading { get; } = Reading;
        internal override T Match<T>(Cases<T> cases) => cases.Typed(this);
    }

    /// <summary>打ったまま見せる入力全体 (数字だけ・水平線・保護区間だけ)。見せる表記の規則を当てない。</summary>
    public sealed record Literal(string Reading, ComposedText Text) : TypingClause
    {
        public override string Reading { get; } = Reading;
        internal override T Match<T>(Cases<T> cases) => cases.Literal(this);
    }

    /// <summary>この文節の見せる文字。</summary>
    public ComposedText Shown => Match(new Cases<ComposedText>
    {
        Kana = kana => ComposedText.Kana(kana.Reading) + ComposedText.Kana(kana.Pending),
        Converted = converted => ComposedText.Converted(converted.Reading, converted.Text) + ComposedText.Kana(converted.Pending),
        Typed = typed => ComposedText.Of(typed.Span.Kind, typed.Span.Text),
        Literal = literal => literal.Text,
    });

    /// <summary>この文節を確定するときの記録 (変換エンジンの答えと文字)。</summary>
    public CommittedClause Committed => Match(new Cases<CommittedClause>
    {
        Kana = kana => new(kana.Reading + kana.Pending, null, kana.Reading + kana.Pending),
        Converted = converted => new(converted.Reading + converted.Pending, converted.Engine, converted.Text + converted.Pending),
        Typed = typed => new(typed.Reading, null, typed.Span.Text),
        Literal = literal => new(literal.Reading, null, literal.Text.Text),
    });
}
