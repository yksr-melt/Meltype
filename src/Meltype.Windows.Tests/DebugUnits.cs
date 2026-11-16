// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 調査用: 打った英字がどう 1 音ずつの単位に分かれ、どこが英語の区間になるかを表示する (Windows のスペルチェッカーあり)。
///   dotnet run --project src/Meltype.Windows.Tests -- --units meetingga
/// </summary>
internal static class DebugUnits
{
    public static void Run(string text)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        CompositionTests.Detector.SpellChecker = Detection.WindowsSpellChecker.Shared;
        var composition = new Composition.CompositionText(CompositionTests.Detector);
        foreach (var c in text) composition.Append(c);
        Console.WriteLine("単位: " + string.Join(" ", composition.Units.Select(u => $"[{u.Raw}:{u.Kana}]")) + (composition.Pending.Length > 0 ? $" 打ちかけ: {composition.Pending}" : ""));
        Console.WriteLine("区間: " + string.Join(" ", composition.Segments().Select(s => $"[{(s.IsEnglish ? "英" : "日")}:{s.Raw}]")));
    }
}
