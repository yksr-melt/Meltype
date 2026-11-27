// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 yksr-melt

using Meltype.Dictionary;

namespace Meltype.Composition;


/// <summary>
/// 変換ボックスに表示する内容。変換中は文節ごとの文字列 (Clauses) と選択中の文節、その文節の候補を持つ。
/// <para>
/// Committed は、この表示を確定したときに文書へ入る文字列。IBus はフォーカスが外れたとき
/// 「表示中の preedit」をそのまま確定するので、表示と確定が違うと文書と本体の認識がずれる
/// (「かn」を表示したまま確定すると、本体は「かん」で確定していて食い違う)。
/// </para>
/// <para>
/// ContextHint は入力内容や変換エンジンに応じた案内 (手動の英字提案・予測候補の選択など)。固定の操作一覧は含めない。
/// 書き間違いの修正は Suggestion に入れる。案内が無ければ null。
/// </para>
/// Notes は Candidates と同じ順序・件数の注記。注記がない候補は None、リスト全体を省略するときは null。
/// 入力欄に入りうる文字の欄 (Text・Candidates・Clauses・Suggestion・Predictions・Committed) は、CompositionOutput.Show が設定の句読点にする。
/// そういう欄を足したら Show にも足す。
/// </summary>
public sealed record CompositionView(
    string Text,
    IReadOnlyList<string> Candidates,
    int SelectedIndex,
    bool Converting,
    string Hint,
    IReadOnlyList<string>? Clauses = null,
    int SelectedClause = -1,
    IReadOnlyList<CandidateNote>? Notes = null,
    string? Meaning = null,
    string? Suggestion = null,
    IReadOnlyList<string>? Predictions = null,
    int SelectedPrediction = -1,
    string? Typed = null,
    string? Committed = null,
    string? ContextHint = null,
    // 一覧に番号を出すか (数字キーで選べる変換中の候補だけ。SessionResult.ForFront が決める)
    bool Numbered = false,
    // 変換中の文字の中のカーソルを、末尾から何文字 (UTF-16) 前に置くか (読みの途中を直している間だけ 1 以上)
    int CaretBack = 0,
    // 候補と同じ順・件数の色見本 (#rrggbbaa)。色見本が無い候補は null、一覧全体で不要なら null
    IReadOnlyList<string?>? Colors = null);

/// <summary>キーを 1 つ押した。Ch は入力する文字 (キーボード配列は OS 側で解決済み。文字を生まないキーなら null)。</summary>
internal readonly record struct KeyPress(int Vk, char? Ch, bool Shift, bool Control, bool Alt, bool Command, long TimeMs)
{
    public bool Modifier => Control || Alt || Command;
}

/// <summary>選択中の確定済みの文字と、その読み (再変換)。</summary>
internal sealed record ReconversionSelection(string Text, string Reading);
