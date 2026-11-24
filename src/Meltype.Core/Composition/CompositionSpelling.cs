// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
// Copyright (C) 2026 yksr-melt

using Meltype.Diagnostics;
using Meltype.Dictionary;
using Meltype.Libraries.Chars;

namespace Meltype.Composition;

/// <summary>書き間違いの提案と修正、利用者が直した判定の記録。</summary>
internal static class CompositionSpelling
{
    internal static (int Start, int End, Misspelling Misspelling)? FindMisspelling(CompositionState state, CompositionEnv env) =>
        env.Services.Misspellings is { } dictionary ? CompositionText.FindMisspelling(state.Text, env, dictionary) : null;

    internal static void FixMisspelling(CompositionState state, (int Start, int End, Misspelling Misspelling) typo)
    {
        Diagnostics.Log.Decision($"もしかして: {Diagnostics.Log.Text(typo.Misspelling.Wrong)}→{Diagnostics.Log.Text(typo.Misspelling.Right)}に直しました。");
        CompositionText.ReplaceReading(state.Text, typo.Start, typo.End, typo.Misspelling.Right);
    }

    /// <summary>「もしかして」の案内。変換ボックスの、打った文字のすぐ下に出す (書き間違いが無ければ null)。</summary>
    internal static string? MisspellingSuggestion(CompositionState state, CompositionEnv env) => FindMisspelling(state, env) is { } typo ? $"もしかして: {typo.Misspelling.Right}　<Tab>で修正" : null;

    internal static void RecordCorrection(CompositionState state, CompositionEnv env, bool converting, string text, string suffix)
    {
        if (env.Corrected is null || CompositionText.Raw(state.Text).Length == 0 || !CompositionText.Raw(state.Text).Any(char.IsAsciiLetter)) return;
        string shown;
        if (converting)
        {
            // 英語 / 日本語を選び直した文節があるときだけ (漢字の選び直しは判定の間違いではない)
            var switched = state.Clauses.Any(c => c.Changed && (c.IsEnglish ? TextChars.IsJapaneseText(c.Text) : c.Raw is { } raw && c.Text == raw));
            if (!switched) return;
            shown = string.Concat(state.Clauses.Select(c => c.Candidates[0]));
        }
        else
        {
            if (state.Text.Mode == DisplayMode.Auto) return;
            var (mode, letterCase) = (state.Text.Mode, state.Text.Case);
            state.Text.Mode = DisplayMode.Auto;
            shown = CompositionText.Display(state.Text, env, final: true);
            (state.Text.Mode, state.Text.Case) = (mode, letterCase);
        }
        if (shown == text) return;
        var before = env.Context().PrecedingText ?? "";
        env.Corrected.Invoke(new Correction(CompositionText.Raw(state.Text), shown, text, suffix.Contains(' ') ? "Space (変換)" : "Enter (確定)",
            before.Length > 20 ? before[^20..] : before, DateTime.Now));
    }
}
