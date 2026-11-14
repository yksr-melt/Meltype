// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

using Meltype.Config;

namespace Meltype.Tests;

internal static class SettingsFormTests
{
    [Test]
    public static void SettingsForm_ApplyKeepsWindowOpenAndCancelDiscardsLaterEdits()
    {
        using var engine = new MeltypeEngine(TestRuntime.Create());
        using var form = new UI.SettingsForm(engine, new AppState());
        form.Show();

        var controls = Descendants(form).ToList();
        var live = controls.OfType<UI.ToggleSwitch>()
            .Single(c => c.AccessibleName == "ライブ変換");
        var auto = controls.OfType<UI.ToggleSwitch>()
            .Single(c => c.AccessibleName == "英語 / 日本語を自動で判定する");
        var apply = controls.OfType<Button>().Single(c => c.Text == "適用");
        var cancel = controls.OfType<Button>().Single(c => c.Text == "キャンセル");
        var originalLive = live.Checked;
        var originalAuto = auto.Checked;
        var configPath = Path.Combine(engine.Runtime.DataDirectory, "config.json");

        live.Checked = !originalLive;
        apply.PerformClick();
        Assert.True(form.Visible, "適用しても画面は開いたまま");
        Assert.Equal(!originalLive, engine.Settings.LiveConversion, "変更を適用する");
        Assert.Equal(!originalLive, Settings.Load(configPath).LiveConversion, "ファイルにも保存する");

        live.Checked = originalLive;
        apply.PerformClick();
        Assert.Equal(originalLive, engine.Settings.LiveConversion, "元の値にも再適用できる");

        auto.Checked = !originalAuto;
        cancel.PerformClick();
        Assert.True(form.IsDisposed, "キャンセルで画面を閉じる");
        Assert.Equal(originalLive, engine.Settings.LiveConversion, "適用済みの値は残る");
        Assert.Equal(originalAuto, engine.Settings.AutoDetection, "適用後の未適用の変更は捨てる");
    }

    [Test]
    public static void SettingsForm_ApplyDoesNotOverwriteLaterTrayChange()
    {
        using var engine = new MeltypeEngine(TestRuntime.Create());
        using var form = new UI.SettingsForm(engine, new AppState());
        form.Show();

        var controls = Descendants(form).ToList();
        var live = controls.OfType<UI.ToggleSwitch>()
            .Single(c => c.AccessibleName == "ライブ変換");
        var apply = controls.OfType<Button>().Single(c => c.Text == "適用");
        var original = live.Checked;

        live.Checked = !original;
        apply.PerformClick();
        Assert.Equal(!original, engine.Settings.LiveConversion, "画面の変更を適用する");

        // 設定画面を開いたまま、トレイ側で同じ項目を変更した状態を作る。
        var traySettings = engine.Settings.Clone();
        traySettings.LiveConversion = original;
        engine.ApplySettings(traySettings);

        apply.PerformClick();
        Assert.Equal(original, engine.Settings.LiveConversion, "未編集の画面はトレイ側の変更を戻さない");

        form.Close(); // × と同じく変更を反映して閉じる
        Assert.Equal(original, engine.Settings.LiveConversion, "閉じるときもトレイ側の変更を戻さない");
    }

    [Test]
    public static void SettingsForm_FontCanBeChangedWithoutOpeningDropDown()
    {
        using var engine = new MeltypeEngine(TestRuntime.Create());
        using var form = new UI.SettingsForm(engine, new AppState());
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
        using var engine = new MeltypeEngine(TestRuntime.Create());
        using var form = new UI.SettingsForm(engine, new AppState());
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
        using var engine = new MeltypeEngine(TestRuntime.Create(new Settings
        {
            AppKinds = [new() { Name = "チャット", LiveConversion = false }, new() { Name = "仕事", Base = AppProfile.Code }],
            AppRules = [new() { Process = "chat.exe", Kind = "チャット" }],
        }));
        using var form = new UI.SettingsForm(engine, new AppState());
        var controls = Descendants(form).ToList();
        var rules = controls.OfType<DataGridView>().Single(g => g.Columns[0].HeaderText.StartsWith("プロセス名", StringComparison.Ordinal));
        var kinds = controls.OfType<DataGridView>().Single(g => g.AccessibleName == "独自の種類");
        Assert.Equal("チャット", rules.Rows[0].Cells[2].Value, "アプリの種類を読み込める");
        var collect = form.GetType().GetMethod("Collect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.Equal(engine.Running.Clone().Normalize().ToJson(), ((Settings)collect.Invoke(form, null)!).ToJson(), "読み込んだ値を失わない (動かしている設定を見せる)");
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
