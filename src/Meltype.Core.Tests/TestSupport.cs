// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Detection;

namespace Meltype.Tests;

internal static class TestSupport
{
    public static Settings DefaultSettings() => new Settings().Normalize();

    public static ScoreEngine CreateEngine(Settings? settings = null)
    {
        settings ??= DefaultSettings();
        return ScoreEngine.CreateDefault(() => settings);
    }

    /// <summary>実際の動作と同じく 1 文字ずつ判定し、最初に Undecided 以外になった結果を返す。語末まで来たら Space で確定した扱い。</summary>
    public static DetectionResult Classify(ScoreEngine engine, string word)
    {
        var letters = new string(word.ToLowerInvariant().Where(c => c is >= 'a' and <= 'z').ToArray());
        for (var i = 1; i <= letters.Length; i++)
        {
            var isFinal = i == letters.Length;
            var result = engine.Evaluate(new DetectionInput(letters[..i], letters[..i].Select(c => (int)char.ToUpperInvariant(c)).ToArray(), isFinal));
            if (result.Verdict != Verdict.Undecided) return result;
        }
        return engine.Evaluate(new DetectionInput(letters, [], true));
    }
}
