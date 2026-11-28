// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

using Meltype.Dictionary;
using Meltype.Notation;

namespace Meltype.Composition;

/// <summary>
/// 予測変換の候補。Text は一覧に出す語、Before は選んだときに語の前に残す文字 (最後の文節の続きの候補なら前の文節の見えている文字、
/// 読み全体の続きの候補なら空)、Reading は語に置き換わる打ちかけの読み。
/// </summary>
internal sealed record Prediction(string Text, string Before, string Reading, string? Color = null, bool NoLearning = false);

/// <summary>変換中の文節。英語の区間も 1 つの文節として扱う (候補は英字/全角英字)。</summary>
internal sealed class Clause(string reading, bool isEnglish, List<string> candidates)
{
    public string Reading { get; set; } = reading;
    public bool IsEnglish { get; } = isEnglish;
    public List<string> Candidates { get; set; } = candidates;
    public int Index { get; set; }
    public string Text => Candidates[Index];

    /// <summary>ユーザーが候補を選び直したか (確定時に学習する)。</summary>
    public bool Changed { get; set; }

    /// <summary>変換エンジンが返したときの Zenzai の答え (Zenzai の答えでなければ null)。今の文字列がこれと同じなら説明に「Zenzai」と出し、候補の一覧ではこの候補に注記を付ける。</summary>
    public string? ZenzaiText { get; set; }

    /// <summary>
    /// 日本語の文節の、文の中での変換結果 (変換エンジンの答え・ユーザー辞書の語)。<see cref="CompositionConversion"/> が、その読みだけの変換などの候補をこの後ろに並べる。
    /// 変換エンジンに頼まない文節 (英語・記号) は null。
    /// </summary>
    public string? Engine { get; init; }

    /// <summary><see cref="CompositionConversion"/> が候補を足し終えたか。</summary>
    public bool EngineCandidatesAdded { get; set; }

    /// <summary>外部変換エンジンの候補追加が完了したか。<see cref="Fixed"/> の文節では追加せずに完了扱いにする。</summary>
    public bool Expanded { get; set; }

    /// <summary>ユーティリティ変換の Replace の文節。候補を足さず、文節の区切りも動かさない。</summary>
    public bool Fixed { get; init; }

    /// <summary>
    /// ユーティリティ変換の Prepend で、候補の先頭に足した候補の数。変換エンジンの候補の一覧 (Expand) は、その後ろに入れる
    /// (先頭に足した漢字の間に 言偏にふるとり が入らないように)。
    /// </summary>
    public int Leading { get; set; }

    /// <summary>候補と同じ順・件数の注記 (ユーティリティ変換が決めたもの)。null なら候補から決める (<see cref="CandidateNotes"/>)。</summary>
    public IReadOnlyList<CandidateNote>? Notes { get; init; }

    /// <summary>候補と同じ順・件数の色見本 (#rrggbbaa)。無ければ null。</summary>
    public IReadOnlyList<string?>? Colors { get; init; }

    /// <summary>候補のうち、選んでも学習しないユーティリティ変換の候補 (<see cref="Utility.UtilityCandidates.NotLearned"/>)。</summary>
    public HashSet<string> NotLearned { get; } = new(StringComparer.Ordinal);

    /// <summary>この文節の読みを打ったときの英字 (あぴ → api)。分からなければ null。</summary>
    public string? Raw { get; set; }

    /// <summary>英語の文節を、打った英字をローマ字として読んだかな (thin → てぃん)。文節の区切りを動かすときに日本語の文節として読み直すのに使う。</summary>
    public string? Kana { get; set; }

    /// <summary>候補のうち英訳 (複雑な → complex) のもの。</summary>
    public HashSet<string> Translations { get; } = new(StringComparer.Ordinal);

    /// <summary>英日の区切りの案ごとの文を候補にした、文全体の文節なら、候補の文 → その案の文節。</summary>
    public Dictionary<string, List<Clause>>? Alternatives { get; init; }
}

/// <summary>
/// 直前に確定した語。英語とも日本語とも読める語 (i, sushi) を文脈が分からないまま確定したとき、
/// 次の語で文脈がはっきりしたら確定し直す (I want: 「胃」→「I」)。キャレットが動いたら (ほかのキー・クリック) 無効。
/// </summary>
/// <param name="AfterEnglish">確定したとき、前が英語だった (Are + you)。英文の中の語なので、後ろが日本語でも日本語に直さない (#265)。</param>
internal sealed record CommitRecord(ComposedText Text, string Raw, bool English, bool SpaceIntended, bool AfterEnglish = false);
