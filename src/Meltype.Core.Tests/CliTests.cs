// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ayappy-mugicha

namespace Meltype.Tests;

internal static class CliTests
{
    [Test]
    public static void Version_PrintsMeltypePrefixAndExitsZero()
    {
        var previous = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var code = Program.Main(["--version"]);
            Assert.Equal(0, code, "exit code");
            var line = writer.ToString().TrimEnd('\r', '\n');
            Assert.True(line.StartsWith("Meltype ", StringComparison.Ordinal), line);
            Assert.True(line.Length > "Meltype ".Length, line);
        }
        finally
        {
            Console.SetOut(previous);
        }
    }
    [Test]
    public static void Repro_LearnsLessonsBeforeTyping()
    {
        // --learn の手順で選んだ変換を覚えてから打つ。打つ入力欄は覚えたものを読むだけで、覚えない
        var history = new Meltype.Learning.ConversionHistory(null);
        CompositionTests.Keyboard Create() => new(live: true, history: history) { Learning = false };
        var learned = Repro.Learn(Create, [Repro.Lesson.Parse("hashiwo → 箸を")]);
        Assert.Equal("箸を", string.Join(",", learned ?? []), "手順で確定したもの");
        var result = Repro.Run(Create, "hashiwo", "enter", convert: true);
        Assert.Equal("箸を", result.Committed, "手順で選んだ変換で確定する。手順で確定したものは前の文にならない");
        Assert.Equal("箸を", result.Converted, "手順で選んだ変換が最初の候補になる");
        Assert.Equal(1, history.Count, "打った入力欄は覚えない");

        Assert.True(Repro.Learn(Create, []) is null, "手順が無ければ学習を出さない");
        var fresh = Repro.Run(() => new CompositionTests.Keyboard(live: true) { Learning = false }, "hashiwo", "space", convert: true);
        Assert.True(fresh.Converted != "箸を", "覚えていなければ変換エンジンの最初の候補: " + fresh.Converted);
    }

    [Test]
    public static void Repro_LessonWithoutWordCommitsAsTyped()
    {
        var lesson = Repro.Lesson.Parse("niti");
        Assert.True(lesson.Text is null, "確定した語が無ければ、打って Enter で確定する");
        Assert.Equal("niti", Repro.Lesson.Parse(" niti → ").Keys, "前後の空白は打たない");
    }
}
