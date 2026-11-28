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
internal sealed record Prediction(string Text, string Before, string Reading);

/// <summary>変換中の文節。英語の区間も 1 つの文節として扱う (候補は英字/全角英字)。</summary>
internal sealed class Clause(string reading, bool isEnglish, List<string> built)
{
    public string Reading { get; set; } = reading;
    public bool IsEnglish { get; } = isEnglish;

    /// <summary>
    /// 文節を作るときに並べた候補 (<see cref="CompositionConversion"/> が文節を作る間だけ書き換える)。同じ文字の候補は持たない:
    /// 選んでいる候補は文字 (<see cref="Choice"/>) で持つので、同じ文字が 2 つあると ↓ で同じ文字に進んで先へ進めない。
    /// 足す所はどこも一覧にある文字を飛ばし、ユーティリティ変換の候補は <see cref="Utility.UtilityConversions.Convert"/> が最初の 1 つにまとめる。
    /// </summary>
    public List<string> Built { get; set; } = built;

    /// <summary>
    /// 変換中に文節を選んだときに、変換エンジンの候補の一覧を <see cref="Built"/> に入れた並び (<see cref="CompositionConversion.Expand"/>)。
    /// まだ頼んでいなければ null。
    /// </summary>
    public IReadOnlyList<string>? Expansion { get; set; }

    /// <summary>
    /// 一覧に無い形をキーで選んだもの (文節だけの F6〜F10・Esc のかな)。一覧の最後に並べる。
    /// 選び直しても一覧から消さない (↑↓ で戻れる)。
    /// </summary>
    public List<string> Added { get; } = [];

    /// <summary>選んでいる候補。null なら最初の候補。</summary>
    public string? Choice { get; set; }

    /// <summary>候補の一覧: <see cref="Expansion"/> (無ければ <see cref="Built"/>) の後ろに <see cref="Added"/>。</summary>
    public IReadOnlyList<string> Candidates
    {
        get
        {
            var listed = Expansion ?? Built;
            if (Added.Count == 0) return listed;
            var added = Added.Where(a => !listed.Contains(a)).ToList();
            return added.Count == 0 ? listed : [.. listed, .. added];
        }
    }

    public int Index
    {
        get
        {
            if (Choice is null) return 0;
            var candidates = Candidates;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == Choice) return i;
            }
            return -1;
        }
    }
    public string Text => Choice ?? Candidates[0];

    /// <summary>ユーザーが候補を選び直したか (確定時に学習する)。</summary>
    public bool Changed { get; set; }

    /// <summary>変換エンジンが返したときの Zenzai の答え (Zenzai の答えでなければ null)。今の文字列がこれと同じなら説明に「Zenzai」と出し、候補の一覧ではこの候補に注記を付ける。</summary>
    public string? ZenzaiText { get; set; }

    /// <summary>
    /// 日本語の文節の、文の中での変換結果 (変換エンジンの答え・ユーザー辞書の語)。<see cref="CompositionConversion"/> が、その読みだけの変換などの候補をこの後ろに並べる。
    /// 変換エンジンに頼まない文節 (英語・記号) は null。
    /// </summary>
    public string? Engine { get; init; }

    /// <summary>ユーティリティ変換の Replace の文節。候補を足さず、文節の区切りも動かさない。</summary>
    public bool Fixed { get; init; }

    /// <summary>
    /// 最初に並ぶ候補の数。ユーティリティ変換の Prepend と、選ぶ回数を覚えて上げた候補の前に、変換エンジンの候補を割り込ませない
    /// (先頭に足した漢字の間に 言偏にふるとり が入らないように)。
    /// </summary>
    public int Leading { get; set; }

    /// <summary>候補と同じ順・件数の注記 (ユーティリティ変換が決めたもの)。null なら候補から決める (<see cref="CandidateNotes"/>)。</summary>
    public IReadOnlyList<CandidateNote>? Notes { get; init; }

    /// <summary>候補と同じ順・件数の色見本 (#rrggbbaa)。無ければ null。</summary>
    public IReadOnlyList<string?>? Colors { get; init; }

    /// <summary>ユーティリティ変換の候補と、その候補を出す書式の ID (<see cref="Utility.UtilityCandidates.RankingKeys"/>)。</summary>
    public Dictionary<string, IReadOnlyList<Utility.UtilityFormat>> UtilityRankingKeys { get; } = new(StringComparer.Ordinal);

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
