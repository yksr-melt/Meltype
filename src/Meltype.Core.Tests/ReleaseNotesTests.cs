// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Tests;

/// <summary>更新したあとの初めての起動で、リリースノートを一度だけ見せる (issue #1207)。</summary>
internal static class ReleaseNotesTests
{
    [Test]
    public static void ReleaseNotes_AreShownOnceAfterAnUpdate()
    {
        Assert.True(ProjectInfo.ShouldShowReleaseNotes("1.3.0", "1.3.1", firstRun: false), "更新したら見せる");
        Assert.True(ProjectInfo.ShouldShowReleaseNotes(null, "1.3.1", firstRun: false), "この仕組みが無い版からの更新でも見せる");
        Assert.True(!ProjectInfo.ShouldShowReleaseNotes("1.3.1", "1.3.1", firstRun: false), "見せた版では、次からは見せない");
        Assert.True(!ProjectInfo.ShouldShowReleaseNotes(null, "1.3.1", firstRun: true), "新しく入れたときは見せない");
        Assert.True(!ProjectInfo.ShouldShowReleaseNotes(null, "?", firstRun: false), "版が分からなければ見せない");
    }

    [Test]
    public static void ReleaseNotes_AreSplitIntoDecoratedLines()
    {
        const string markdown = """
            # Meltype v1.6.0

            説明の **強調** と `code` です。

            ## 変わったこと

            - **項目** です (#1749)。
              - 入れ子です。
            - [報告フォーム](https://example.com/report) から送れます。

            **Full Changelog**: https://example.com/compare
            """;

        var lines = ReleaseNotesMarkdown.Parse(markdown);
        Assert.Equal(11, lines.Count);

        Assert.Equal(ReleaseNotesMarkdown.LineKind.Heading, lines[0].Kind);
        Assert.Equal(1, lines[0].HeadingLevel);
        Assert.Equal("Meltype v1.6.0", Plain(lines[0]));

        Assert.Equal(ReleaseNotesMarkdown.LineKind.Text, lines[2].Kind);
        Assert.Equal("説明の 強調 と code です。", Plain(lines[2]));
        Assert.Equal("説明の |強調| と |code| です。", string.Join("|", lines[2].Spans.Select(s => s.Text)));
        Assert.True(lines[2].Spans[1].Bold, "**強調** は太字");
        Assert.True(lines[2].Spans[3].Code, "`code` は等幅");

        Assert.Equal(ReleaseNotesMarkdown.LineKind.Heading, lines[4].Kind);
        Assert.Equal(2, lines[4].HeadingLevel);
        Assert.Equal("変わったこと", Plain(lines[4]));

        Assert.Equal(ReleaseNotesMarkdown.LineKind.Bullet, lines[6].Kind);
        Assert.Equal(0, lines[6].Indent);
        Assert.Equal("項目 です (#1749)。", Plain(lines[6]));
        Assert.True(lines[6].Spans[0].Bold, "箇条書きの中の **項目** は太字");
        Assert.Equal(ReleaseNotesMarkdown.LineKind.Bullet, lines[7].Kind);
        Assert.Equal(2, lines[7].Indent, "入れ子の字下げは残す");
        Assert.Equal("入れ子です。", Plain(lines[7]));
        Assert.Equal(ReleaseNotesMarkdown.LineKind.Bullet, lines[8].Kind);
        Assert.Equal("報告フォーム から送れます。", Plain(lines[8]), "リンクは URL を出さず、文字だけ残す");

        Assert.Equal("Full Changelog: https://example.com/compare", Plain(lines[10]));
        Assert.True(lines[10].Spans[0].Bold, "**Full Changelog** は太字");

        static string Plain(ReleaseNotesMarkdown.Line line) => string.Concat(line.Spans.Select(s => s.Text));
    }

    [Test]
    public static void ReleaseUrls_PointToTheTagAndTheList()
    {
        Assert.Equal("https://github.com/yksr-melt/Meltype/releases/tag/v1.3.1-pre.3", ProjectInfo.ReleaseUrl("1.3.1-pre.3"));
        Assert.Equal("https://github.com/yksr-melt/Meltype/releases", ProjectInfo.ReleasesUrl);
        Assert.True(!new Settings().ToJson().Contains(nameof(AppState.ReleaseNotesShown)), "config.json (プロファイル) には入れない");
    }
}
