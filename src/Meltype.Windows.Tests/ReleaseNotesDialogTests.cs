// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

namespace Meltype.Tests;

/// <summary>リリースノートの画面。</summary>
internal static class ReleaseNotesDialogTests
{
    [Test]
    public static void ReleaseNotesDialog_LaysOutBulletsAndDoesNotSelectAll()
    {
        using var dialog = new UI.ReleaseNotesDialog("1.6.0", updated: true, "# Meltype v1.6.0\n\n- **項目** と `code`\n  - 入れ子");
        dialog.Show();
        Application.DoEvents();

        var text = Descendants(dialog).OfType<RichTextBox>().Single();
        Assert.Equal("Meltype v1.6.0\n\n・項目 と code\n  ・入れ子", text.Text.ReplaceLineEndings("\n"));
        Assert.Equal(0, text.SelectionLength, "開いたときに本文を全選択しない");
        Assert.True(dialog.ActiveControl is Button { Text: "閉じる" }, "開いたときは「閉じる」にフォーカスする");

        text.Select(0, "Meltype v1.6.0".Length);
        Assert.True(text.SelectionFont is { } heading && heading.Size > text.Font.Size, "見出しは本文より大きい");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
