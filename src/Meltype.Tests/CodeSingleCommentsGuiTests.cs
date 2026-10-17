// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

using System.Reflection;
using Meltype.Config;
using Meltype.UI;

namespace Meltype.Tests;

/// <summary>WinForms の編集・収集・保存の試験。OS キーから実 IME を通す試験とは別。</summary>
internal static class CodeSingleCommentsGuiTests
{
    [Test]
    public static void Editor_AddEditDisableAndDeleteLastEntry()
    {
        using var host = new Form { Size = new Size(800, 600) };
        using var editor = new CodeSingleCommentsEditor();
        host.Controls.Add(editor);
        ShowOffscreen(host);
        editor.LoadValue(new CodeSingleCommentSettings());
        var grid = Descendants(editor).OfType<DataGridView>().Single();
        Assert.Equal(0, grid.Rows.Count, "Custom の初期値は空");
        Assert.True(!grid.AllowUserToAddRows, "追加ボタンで作る空行と、プレースホルダーを混在させない");
        Button(editor, "追加").PerformClick();
        Assert.True(grid.IsCurrentCellInEditMode, "追加した行をすぐ編集できる");
        ((TextBox)grid.EditingControl!).Text = "%";
        var value = editor.ReadValue();
        Assert.Equal("%", value.Custom.Single().Token, "セルの編集を終える前の値も収集する");
        Assert.True(value.Custom[0].Enabled, "新規は ON");
        grid.Rows[0].Cells[1].Value = false;
        foreach (var toggle in Descendants(editor).OfType<ToggleSwitch>()) toggle.Checked = false;
        value = editor.ReadValue();
        Assert.True(!value.Custom[0].Enabled, "無効でも登録値は残る");
        Assert.True(!value.BuiltIn.SlashSlash && !value.BuiltIn.Hash && !value.BuiltIn.DoubleHyphen && !value.BuiltIn.Rem, "既定4種類も独立して OFF");
        grid.ClearSelection();
        grid.Rows[0].Cells[0].Selected = true;
        Button(editor, "選んだ行を削除").PerformClick();
        Assert.Equal(0, editor.ReadValue().Custom.Count, "最後の1件も削除できる");
    }

    [Test]
    public static void Editor_UsesCoreValidationWithoutCorrectingInvalidRows()
    {
        using var editor = new CodeSingleCommentsEditor();
        var grid = Descendants(editor).OfType<DataGridView>().Single();
        foreach (var token in new[] { "", " ", "a b", "％", "//", "#", "--", "rem", "ReM" })
        {
            grid.Rows.Clear();
            grid.Rows.Add(token, false);
            Reject(editor);
            Assert.Equal(token, grid.Rows[0].Cells[0].Value, "無効な行も削除・Trim・変換しない");
        }
        grid.Rows.Clear();
        grid.Rows.Add("NOTE", true);
        grid.Rows.Add("NOTE", false);
        Reject(editor);
        Assert.Equal(2, grid.Rows.Count, "重複も消さない");
        grid.Rows[1].Cells[0].Value = "note";
        Assert.Equal("NOTE,note", string.Join(",", editor.ReadValue().Custom.Select(c => c.Token)), "Custom の大小文字は別");
    }

    [Test]
    public static void Form_InvalidEditsBlockClosingAndProfileSwitchAndTestCanContinue()
    {
        var settings = new Settings().Normalize().AddProfile("別設定")!.SwitchProfile(Settings.DefaultProfileName);
        using var engine = new MeltypeEngine(settings, null, null, null);
        using var form = new SettingsForm(engine);
        ShowOffscreen(form);
        var before = engine.Settings.ToJson();
        var grid = CommentsGrid(form);
        grid.Rows.Add("％", false);
        // 判定テスト・Toggle のイベントから Collect へ到達しても例外で落ちない。
        Field<TextBox>(form, "_testInput").Text = "kyouha";
        Descendants(form).OfType<ToggleSwitch>().First(t => t.AccessibleName == "ライブ変換").Toggle();
        Assert.True(Field<TextBox>(form, "_testResult").Text.Contains("誤り"), "判定テストは不正値を知らせる");
        Field<ComboBox>(form, "_profiles").SelectedItem = "別設定";
        Assert.Equal(Settings.DefaultProfileName, Field<ComboBox>(form, "_profiles").SelectedItem, "切替を取り消す");
        Assert.Equal("％", grid.Rows[0].Cells[0].Value, "編集中の値を残す");
        var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
        Invoke(form, "OnFormClosing", closing);
        Assert.True(closing.Cancel, "OK・× 共通の保存処理を取り消す");
        Assert.Equal(before, engine.Settings.ToJson(), "保存前は稼働中設定を変えない");
        Assert.True(Descendants(form).OfType<Label>().Any(l => l.AccessibleRole == AccessibleRole.Alert && l.Text.Contains("印字可能 ASCII")), "Core のエラーを画面に表示");
        Button(form, "キャンセル").PerformClick();
        Assert.True(form.IsDisposed, "不正値があってもキャンセルで閉じる");
        Assert.Equal(before, engine.Settings.ToJson(), "キャンセルは破棄");
    }

    [Test]
    public static void Form_ProfileSwitchExportImportAndDefaultsKeepIndependentValues()
    {
        var initial = new Settings().Normalize();
        initial.CodeSingleComments.Custom.Add(new CodeSingleComment { Token = "%" });
        var settings = initial.AddProfile("別設定")!;
        settings.CodeSingleComments.Custom[0].Token = "@@";
        settings = settings.SwitchProfile(Settings.DefaultProfileName);
        using var engine = new MeltypeEngine(settings, null, null, null);
        using var form = new SettingsForm(engine);
        ShowOffscreen(form);
        var grid = CommentsGrid(form);
        grid.Rows[0].Cells[0].Value = "NOTE";
        Field<ComboBox>(form, "_profiles").SelectedItem = "別設定";
        Assert.Equal("@@", grid.Rows[0].Cells[0].Value, "切替先の表");
        Field<ComboBox>(form, "_profiles").SelectedItem = Settings.DefaultProfileName;
        Assert.Equal("NOTE", grid.Rows[0].Cells[0].Value, "元の編集を維持");
        var collected = (Settings)Invoke(form, "Collect")!;
        var imported = collected.ImportProfile(collected.ExportProfile())!;
        Invoke(form, "LoadFrom", imported);
        Assert.Equal("NOTE", grid.Rows[0].Cells[0].Value, "入出力後も表に戻せる");
        Button(form, "既定値に戻す").PerformClick();
        var defaults = (Settings)Invoke(form, "Collect")!;
        Assert.Equal(0, defaults.CodeSingleComments.Custom.Count, "復元後の Custom は空");
        Assert.True(defaults.CodeSingleComments.BuiltIn.SlashSlash && defaults.CodeSingleComments.BuiltIn.Hash && defaults.CodeSingleComments.BuiltIn.DoubleHyphen && defaults.CodeSingleComments.BuiltIn.Rem, "復元後の既定4種類は ON");
        Assert.Equal("%", engine.Settings.CodeSingleComments.Custom.Single().Token, "保存前の復元・切替は engine へ反映しない");
        Button(form, "キャンセル").PerformClick();
    }

    [Test]
    public static void Form_SaveMergesTrayEditsAndPersistsAndReopens()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-comments-gui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            using var engine = new MeltypeEngine(new Settings(), path, null, null);
            using (var form = new SettingsForm(engine))
            {
                ShowOffscreen(form);
                CommentsGrid(form).Rows.Add("%", false);
                Descendants(form).OfType<ToggleSwitch>().Single(t => t.AccessibleName == "既定の REM").Checked = false;
                var tray = engine.Settings.Clone();
                tray.DetectionLevel = DetectionLevel.Conservative;
                engine.ApplySettings(tray);
                Button(form, "OK").PerformClick();
                Assert.True(form.IsDisposed, "OK で保存して閉じる");
            }
            Assert.Equal(DetectionLevel.Conservative, engine.Settings.DetectionLevel, "画面外で変えた値を維持");
            var loaded = Settings.Load(path);
            Assert.Equal("%", loaded.CodeSingleComments.Custom.Single().Token, "ディスクにも保存");
            Assert.True(!loaded.CodeSingleComments.Custom[0].Enabled && !loaded.CodeSingleComments.BuiltIn.Rem, "ON/OFF を保存");
            using var restarted = new MeltypeEngine(loaded, null, null, null);
            using var reopened = new SettingsForm(restarted);
            Assert.Equal("%", CommentsGrid(reopened).Rows[0].Cells[0].Value, "再起動相当の読み込み");
            // × も保存する (FormClosing の共通経路)。
            CommentsGrid(reopened).Rows.Clear();
            Invoke(reopened, "OnFormClosing", new FormClosingEventArgs(CloseReason.UserClosing, false));
            Assert.Equal(0, restarted.Settings.CodeSingleComments.Custom.Count, "最後の1件を消して × で保存");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Reject(CodeSingleCommentsEditor editor)
    {
        try { editor.ReadValue(); }
        catch (CodeSingleCommentValidationException) { return; }
        throw new AssertionException("Core の検証エラーが必要");
    }

    private static void ShowOffscreen(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        form.Show();
        Application.DoEvents();
    }

    private static DataGridView CommentsGrid(Control form) => Descendants(form).OfType<CodeSingleCommentsEditor>().SelectMany(Descendants).OfType<DataGridView>().Single();
    private static Button Button(Control root, string text) => Descendants(root).OfType<Button>().Single(b => b.Text == text);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static object? Invoke(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
