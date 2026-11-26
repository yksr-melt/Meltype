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
    internal static (int Start, int End, Misspelling Misspelling)? FindMisspelling(CompositionInput input, CompositionEnv env) =>
        env.Services.Misspellings is { } dictionary ? CompositionText.FindMisspelling(input.Text, env, dictionary) : null;

    internal static void FixMisspelling(CompositionInput input, (int Start, int End, Misspelling Misspelling) typo)
    {
        Diagnostics.Log.Decision($"もしかして: {Diagnostics.Log.Text(typo.Misspelling.Wrong)}→{Diagnostics.Log.Text(typo.Misspelling.Right)}に直しました。");
        CompositionText.ReplaceReading(input.Text, typo.Start, typo.End, typo.Misspelling.Right);
    }

    /// <summary>「もしかして」の案内。変換ボックスの、打った文字のすぐ下に出す (書き間違いが無ければ null)。</summary>
    internal static string? MisspellingSuggestion(CompositionInput input, CompositionEnv env) => FindMisspelling(input, env) is { } typo ? $"もしかして: {typo.Misspelling.Right}　<Tab>で修正" : null;

    internal static void RecordCorrection(CompositionScene.WithInput scene, CompositionEnv env, string text, string suffix)
    {
        var typed = scene.Input.Text;
        if (env.Corrected is null || CompositionText.Raw(typed).Length == 0 || !CompositionText.Raw(typed).Any(char.IsAsciiLetter)) return;
        string shown;
        if (scene is CompositionScene.Converting { Conversion.Clauses: var clauses })
        {
            // 英語 / 日本語を選び直した文節があるときだけ (漢字の選び直しは判定の間違いではない)
            var switched = clauses.Any(c => c.Changed && (c.IsEnglish ? TextChars.IsJapaneseText(c.Text) : c.Raw is { } raw && c.Text == raw));
            if (!switched) return;
            shown = string.Concat(clauses.Select(c => c.Candidates[0]));
        }
        else
        {
            if (typed.Mode == DisplayMode.Auto) return;
            var (mode, letterCase) = (typed.Mode, typed.Case);
            typed.Mode = DisplayMode.Auto;
            shown = CompositionText.Display(typed, env, final: true);
            (typed.Mode, typed.Case) = (mode, letterCase);
        }
        if (shown == text) return;
        var before = env.Context().PrecedingText ?? "";
        env.Corrected.Invoke(new Correction(CompositionText.Raw(typed), shown, text, suffix.Contains(' ') ? "Space (変換)" : "Enter (確定)",
            before.Length > 20 ? before[^20..] : before, DateTime.Now));
    }
}
