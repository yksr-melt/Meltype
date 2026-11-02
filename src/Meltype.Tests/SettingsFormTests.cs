// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

using Meltype.Config;

namespace Meltype.Tests;

internal static class SettingsFormTests
{
    [Test]
    public static void SettingsForm_FontCanBeChangedWithoutOpeningDropDown()
    {
        using var engine = new MeltypeEngine(new Settings(), null, null);
        using var form = new UI.SettingsForm(engine);
        var combo = Descendants(form).OfType<ComboBox>().Single(c => c.AccessibleName == "変換ボックスのフォント");
        Assert.True(combo.Items.Count > 1, "開いた時点で、この PC のフォントを選べる");
        Assert.Equal(0, combo.SelectedIndex, "既定のフォントから始まる");
        form.Show();
        combo.Focus();
        Assert.True(!combo.DroppedDown, "一覧を開いていない");
        Native.SendMessage(combo.Handle, Native.WM_KEYDOWN, (nint)(int)Keys.Down, 0);
        Native.SendMessage(combo.Handle, Native.WM_KEYUP, (nint)(int)Keys.Down, 0);
        Assert.Equal(1, combo.SelectedIndex, "閉じたまま ↓ で次のフォントを選べる");
        var collect = form.GetType().GetMethod("Collect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.Equal(combo.GetItemText(combo.Items[1]), ((Settings)collect.Invoke(form, null)!).CompositionFont, "選んだフォントを設定に戻せる");
    }

    [Test]
    public static void SettingsForm_RowsDoNotOverlapAfterOpeningAndResizing()
    {
        using var engine = new MeltypeEngine(new Settings(), null, null);
        using var form = new UI.SettingsForm(engine);
        form.Show();
        foreach (var width in new[] { form.Width, form.MinimumSize.Width })
        {
            form.Width = width;
            form.PerformLayout();
            var groups = Descendants(form).OfType<GroupBox>().Where(g => g.Controls.OfType<TableLayoutPanel>().Any()).ToList();
            Assert.True(groups.Count > 1, "分類ごとの枠を作っている");
            foreach (var group in groups)
            {
                var table = group.Controls.OfType<TableLayoutPanel>().Single();
                Assert.True(table.Bottom <= group.ClientSize.Height, $"分類の枠から表がはみ出さない: {group.Text}");
                var bottom = 0;
                foreach (var row in table.Controls.Cast<Control>().GroupBy(c => table.GetPositionFromControl(c).Row).OrderBy(r => r.Key))
                {
                    Assert.True(row.Key >= 0 && row.All(c => c.Height > 0), "各項目を行に配置している");
                    Assert.True(row.Min(c => c.Top) >= bottom, $"前の行と重ならない: {group.Text} の {row.Key} 行目 (幅 {width})");
                    bottom = row.Max(c => c.Bottom);
                }
            }
        }
    }

    [Test]
    public static void SettingsForm_CustomKindsLoadAndCanStillBeEdited()
    {
        using var engine = new MeltypeEngine(new Settings
        {
            AppKinds = [new() { Name = "チャット", LiveConversion = false }, new() { Name = "仕事", Base = AppProfile.Code }],
            AppRules = [new() { Process = "chat.exe", Kind = "チャット" }],
        }, null, null);
        using var form = new UI.SettingsForm(engine);
        var controls = Descendants(form).ToList();
        var rules = controls.OfType<DataGridView>().Single(g => g.Columns[0].HeaderText.StartsWith("プロセス名", StringComparison.Ordinal));
        var kinds = controls.OfType<DataGridView>().Single(g => g.AccessibleName == "独自の種類");
        Assert.Equal("チャット", rules.Rows[0].Cells[2].Value, "アプリの種類を読み込める");
        var collect = form.GetType().GetMethod("Collect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.Equal(engine.Settings.Clone().Normalize().ToJson(), ((Settings)collect.Invoke(form, null)!).ToJson(), "読み込んだ値を失わない");
        kinds.Rows[0].Cells[0].Value = "雑談";
        var choices = (DataGridViewComboBoxColumn)rules.Columns[2];
        Assert.True(choices.Items.Contains("雑談"), "読み込み後の編集は、アプリ別設定の選択肢にすぐ反映する");
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
