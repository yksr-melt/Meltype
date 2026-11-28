// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;

namespace Meltype.Composition;

/// <summary>
/// 入力欄の場面。場面と一緒に終わる値は、その場面の型が持つ。場面を移すのは <see cref="CompositionLifecycle"/> だけ。
/// すべての場面で場合分けするときは <see cref="Match"/> を使う (場面を足すと、直していない所がコンパイルエラーになる)。
/// </summary>
internal abstract class CompositionScene
{
    private CompositionScene() { }

    internal abstract T Match<T>(Cases<T> cases);

    internal readonly record struct Cases<T>
    {
        public required Func<Japanese, T> Japanese { get; init; }
        public required Func<Direct, T> Direct { get; init; }
        public required Func<Judging, T> Judging { get; init; }
        public required Func<EnglishWord, T> EnglishWord { get; init; }
        public required Func<Typing, T> Typing { get; init; }
        public required Func<Converting, T> Converting { get; init; }
    }

    /// <summary>日本語で、何も打っていない。</summary>
    internal sealed class Japanese : CompositionScene
    {
        internal override T Match<T>(Cases<T> cases) => cases.Japanese(this);
    }

    /// <summary>英数 (直接入力) で、何も打っていない。キーをアプリに渡す (Mac の「英数」キー、「かな」キーで戻す)。</summary>
    internal sealed class Direct : CompositionScene
    {
        internal override T Match<T>(Cases<T> cases) => cases.Direct(this);
    }

    /// <summary>英数で打ち始めた語がローマ字かを判定している。打鍵はすぐアプリに送り (待たせない)、ローマ字と分かったら消して変換ボックスに入れ直す。</summary>
    internal sealed class Judging : CompositionScene
    {
        public readonly StringBuilder Letters = new();

        /// <summary>Letters の各文字を打ったキーの位置 (<see cref="KeyPress.Position"/>)。かな入力で日本語に戻すとき、かなを位置で決める。- は 0。</summary>
        public readonly List<int> Positions = [];

        /// <summary>アプリに通した文字 (文字の無いキーは \0)。</summary>
        public readonly StringBuilder Sent = new();

        public long LastKeyTime;

        /// <summary>学習しない入力 (<see cref="CompositionInput.Private"/>)。</summary>
        public bool Private;

        /// <summary>
        /// 英語と判定した語の続きか。打鍵の間が空いて語を英語と判定した (<see cref="CompositionDirectInput.Tick"/>) そのキーが英字なら、
        /// そのキーから新しい判定も始まる。
        /// </summary>
        public required bool InEnglishWord;

        internal override T Match<T>(Cases<T> cases) => cases.Judging(this);
    }

    /// <summary>英数で打ち始めた語を英語と判定して、その続きを打っている。</summary>
    internal sealed class EnglishWord : CompositionScene
    {
        internal override T Match<T>(Cases<T> cases) => cases.EnglishWord(this);
    }

    /// <summary>変換ボックスに入力がある。</summary>
    internal abstract class WithInput : CompositionScene
    {
        private protected WithInput(CompositionInput input) => Input = input;

        public CompositionInput Input { get; }
    }

    /// <summary>変換ボックスに打っている。</summary>
    internal sealed class Typing(CompositionInput input) : WithInput(input)
    {
        internal override T Match<T>(Cases<T> cases) => cases.Typing(this);
    }

    /// <summary>変換して候補を選んでいる。取り消すと、打っている場面に戻る。</summary>
    internal sealed class Converting(CompositionInput input, Conversion conversion) : WithInput(input)
    {
        public Conversion Conversion { get; } = conversion;

        internal override T Match<T>(Cases<T> cases) => cases.Converting(this);
    }
}

/// <summary>変換ボックスの入力 1 つ分 (打ち始めてから、確定するか取り消すまで) の値。</summary>
internal sealed class CompositionInput
{
    public required CompositionTextState Text { get; set; }

    /// <summary>入力が終わったら英数に戻る (英数で再変換した・入力中に OS が IME を閉じた・ゲームに移った)。</summary>
    public required bool Direct { get; set; }

    /// <summary>再変換で置き換える、選択していた文字。</summary>
    public ReconversionSelection? Reconversion { get; init; }

    /// <summary>学習しない入力か。入力を覚えないように求める入力欄で打った入力は、欄を離れて確定しても学習しない。</summary>
    public required bool Private { get; set; }

    /// <summary>予測変換の候補。null なら、次に表示するときに今の打ちかけから作る。</summary>
    public PredictionList? Predictions { get; set; }

    /// <summary>F6〜F10 (Ctrl+U/I/O/P・無変換・ひらがなキーも) でかな・英字にしたか。次に文字を打ったら確定する。</summary>
    public bool ModeByKey { get; set; }

    /// <summary>Esc で戻した語は、判定として覚えない。</summary>
    public bool EscReverted { get; set; }

    /// <summary>続けて打つ前に選んでいた候補 (読み → 文字)。</summary>
    public Dictionary<string, string> KeptChoices { get; } = [];

    /// <summary>
    /// F6〜F10 (と同じ表示の形を選ぶキー) で抜けた変換と、そのときの打った文字。確定の記録 (<see cref="CompositionCommit.CommittedClauses"/>) に使う。
    /// 打った文字が変わっていたら使わない。
    /// </summary>
    public (Conversion Conversion, string Raw)? LeftConversion { get; set; }
}
