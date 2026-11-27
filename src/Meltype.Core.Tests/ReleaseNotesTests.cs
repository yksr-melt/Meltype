// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

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
        Assert.True(!ProjectInfo.ShouldShowReleaseNotes("1.3.0", "1.3.1", firstRun: false, autoShow: false), "設定で自動表示を OFF にしたら見せない");
        Assert.True(new Settings().ShowReleaseNotesAfterUpdate, "標準では更新後に見せる");
        Assert.True(!new Settings().ProfileValues().ContainsKey(nameof(Settings.ShowReleaseNotesAfterUpdate)), "プロファイルでは変えない");
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

        var title = Find(lines, "Meltype v1.6.0");
        Assert.Equal(ReleaseNotesMarkdown.LineKind.Heading, title.Kind);
        Assert.Equal(1, title.HeadingLevel);

        var lead = Find(lines, "説明の 強調 と code です。");
        Assert.Equal(ReleaseNotesMarkdown.LineKind.Text, lead.Kind);
        Assert.Equal("説明の |強調| と |code| です。", string.Join("|", lead.Spans.Select(s => s.Text)));
        Assert.True(lead.Spans[1].Bold, "**強調** は太字");
        Assert.True(lead.Spans[3].Code, "`code` は等幅");

        var changed = Find(lines, "変わったこと");
        Assert.Equal(ReleaseNotesMarkdown.LineKind.Heading, changed.Kind);
        Assert.Equal(2, changed.HeadingLevel);

        var item = Find(lines, "項目 です (#1749)。");
        Assert.Equal(ReleaseNotesMarkdown.LineKind.Bullet, item.Kind);
        Assert.Equal(0, item.Indent);
        Assert.True(item.Spans[0].Bold, "箇条書きの中の **項目** は太字");

        var nested = Find(lines, "入れ子です。");
        Assert.Equal(ReleaseNotesMarkdown.LineKind.Bullet, nested.Kind);
        Assert.Equal(2, nested.Indent, "入れ子の字下げは残す");

        var link = Find(lines, "報告フォーム から送れます。");
        Assert.Equal(ReleaseNotesMarkdown.LineKind.Bullet, link.Kind);

        var changelog = Find(lines, "Full Changelog: https://example.com/compare");
        Assert.True(changelog.Spans[0].Bold, "**Full Changelog** は太字");

        static ReleaseNotesMarkdown.Line Find(IReadOnlyList<ReleaseNotesMarkdown.Line> lines, string text) =>
            lines.Single(line => Plain(line) == text);
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
