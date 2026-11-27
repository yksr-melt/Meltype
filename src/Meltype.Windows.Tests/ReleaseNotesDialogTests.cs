// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

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

    [Test]
    public static void ReleaseNotesDialog_DontShowAgainChangesAutoShowSetting()
    {
        bool? showAfterUpdate = null;
        using var dialog = new UI.ReleaseNotesDialog("1.6.0", updated: true, "- 項目", showAfterUpdate: true, show => showAfterUpdate = show);
        var dontShow = Descendants(dialog).OfType<CheckBox>().Single();
        Assert.Equal("今後表示しない", dontShow.Text);
        Assert.True(!dontShow.Checked, "自動表示が ON ならチェックしない");
        dontShow.Checked = true;
        Assert.Equal(false, showAfterUpdate, "チェックしたら自動表示を OFF にする");
        dontShow.Checked = false;
        Assert.Equal(true, showAfterUpdate, "外したら自動表示を ON に戻す");

        using var off = new UI.ReleaseNotesDialog("1.6.0", updated: false, null, showAfterUpdate: false, _ => { });
        Assert.True(Descendants(off).OfType<CheckBox>().Single().Checked, "自動表示が OFF ならチェックしておく");
    }

    [Test]
    public static void ReleaseNotesDialog_DontShowAgainIsSavedThroughTheEngine()
    {
        using var engine = new MeltypeEngine(TestRuntime.Create());
        var configPath = Path.Combine(engine.Runtime.DataDirectory, "config.json");
        Assert.True(engine.Settings.ShowReleaseNotesAfterUpdate, "既定では更新後に出す");

        UI.ReleaseNotesDialog.ShowFor("1.6.0", updated: true, engine);
        Application.DoEvents();
        var dialog = Application.OpenForms.OfType<UI.ReleaseNotesDialog>().Single();
        try
        {
            var dontShow = Descendants(dialog).OfType<CheckBox>().Single();
            Assert.True(!dontShow.Checked, "自動表示が ON ならチェックしない");

            dontShow.Checked = true;
            Assert.True(!engine.Settings.ShowReleaseNotesAfterUpdate, "チェックしたら動いている設定も OFF にする");
            Assert.True(!Config.Settings.Load(configPath).ShowReleaseNotesAfterUpdate, "config.json にも保存する");

            dontShow.Checked = false;
            Assert.True(engine.Settings.ShowReleaseNotesAfterUpdate, "外したら ON に戻す");
        }
        finally
        {
            dialog.Close();
        }
    }

    [Test]
    public static void ReleaseNotes_StartupShowsOnceAndRespectsAutoShow()
    {
        var updated = new Config.AppState { ReleaseNotesShown = "1.6.0" };
        Assert.True(!UI.TrayApplicationContext.TakeReleaseNotes(updated, "1.7.0", firstRun: false, autoShow: false), "設定が OFF なら自動では出さない");
        Assert.Equal("1.7.0", updated.ReleaseNotesShown, "出さなくても、その版は見せたことにする");
        Assert.True(!UI.TrayApplicationContext.TakeReleaseNotes(updated, "1.7.0", firstRun: false, autoShow: true), "同じ版は二度出さない");

        Assert.True(UI.TrayApplicationContext.TakeReleaseNotes(new Config.AppState { ReleaseNotesShown = "1.6.0" }, "1.7.0", firstRun: false, autoShow: true), "ON なら更新後に出す");
        Assert.True(!UI.TrayApplicationContext.TakeReleaseNotes(new Config.AppState(), "1.7.0", firstRun: true, autoShow: true), "初めての起動では出さない");
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
