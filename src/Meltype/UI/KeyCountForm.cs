// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;

namespace Meltype.UI;

/// <summary>
/// 打鍵数 (トレイの「打鍵数...」)。typect (タイプ数カウンター) のように、合計・今日・キーごと・日ごとの打鍵数を見せる。
/// typect で数えた分は「typect から引き継ぐ...」で足せる (一覧をコピーしたテキスト・CSV)。
/// </summary>
internal sealed class KeyCountForm : Form
{
    private readonly KeyCounter _counter;
    private readonly Label _summary = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), Font = new Font("Yu Gothic UI", 12F) };
    private readonly DataGridView _keyGrid = FormControls.ReadOnlyGrid();
    private readonly DataGridView _dayGrid = FormControls.ReadOnlyGrid();
    private readonly TextBox _analysis = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("BIZ UDGothic", 10F), BorderStyle = BorderStyle.None };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };

    public KeyCountForm(KeyCounter counter)
    {
        _counter = counter;
        Text = "Meltype 打鍵数";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(520, 560);
        MinimumSize = new Size(400, 360);
        Font = new Font("Yu Gothic UI", 9.5F);

        _keyGrid.Columns.Add("key", "キー");
        _keyGrid.Columns.Add("count", "回数");
        _keyGrid.Columns.Add("share", "割合");
        _dayGrid.Columns.Add("day", "日");
        _dayGrid.Columns.Add("count", "回数");

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var keyPage = new TabPage("キーごと");
        keyPage.Controls.Add(_keyGrid);
        var dayPage = new TabPage("日ごと");
        dayPage.Controls.Add(_dayGrid);
        var analysisPage = new TabPage("分析");
        analysisPage.Controls.Add(_analysis);
        tabs.TabPages.Add(analysisPage);
        tabs.TabPages.Add(keyPage);
        tabs.TabPages.Add(dayPage);

        var import = new Button { Text = "typect から引き継ぐ...", AutoSize = true };
        import.Click += (_, _) => Import();
        var reset = new Button { Text = "リセット...", AutoSize = true };
        reset.Click += (_, _) => Reset();
        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([close, reset, import]);

        Controls.Add(tabs);
        Controls.Add(_summary);
        Controls.Add(buttons);
        CancelButton = close;

        _refresh.Tick += (_, _) => UpdateSummary();
        _refresh.Start();
        FormClosed += (_, _) => _refresh.Dispose();
        Reload();
    }

    private void UpdateSummary() =>
        _summary.Text = $"合計 {_counter.Total:N0} 打    今日 {_counter.OnDay(DateTime.Today):N0} 打";

    private void Reload()
    {
        UpdateSummary();
        var total = Math.Max(1, _counter.TopKeys().Sum(p => p.Value));
        _keyGrid.Rows.Clear();
        foreach (var (key, count) in _counter.TopKeys())
            _keyGrid.Rows.Add(key, count.ToString("N0"), $"{100.0 * count / total:0.0}%");
        _dayGrid.Rows.Clear();
        foreach (var (day, count) in _counter.RecentDays())
            _dayGrid.Rows.Add(day, count.ToString("N0"));
        _analysis.Text = AnalysisText(_counter.Analyze(DateTime.Today));
    }

    private static string AnalysisText(KeyCounter.Analysis a)
    {
        var lines = new List<string>
        {
            $"打った日数        {a.ActiveDays:N0} 日",
            $"1 日の平均        {a.DailyAverage:N0} 打",
            $"いちばん打った日  {(a.BestDay is null ? "-" : $"{a.BestDay} ({a.BestDayCount:N0} 打)")}",
            $"続けて打った日数  {a.Streak:N0} 日",
            $"この 7 日間       {a.Last7Days:N0} 打 (その前の 7 日間 {a.Previous7Days:N0} 打{Change(a.Last7Days, a.Previous7Days)})",
            $"よく打つ時間帯    {(a.PeakHour < 0 ? "-" : $"{a.PeakHour} 時台")}",
            "",
            $"左手 / 右手       {a.LeftHandShare:P0} / {(a.LeftHandShare == 0 ? 0 : 1 - a.LeftHandShare):P0} (英字・数字・記号)",
            $"打ち直し          {a.CorrectionShare:P1} (BackSpace・Delete)",
            $"スペース          {a.SpaceShare:P1}",
            $"修飾キー          {a.ModifierShare:P1} (Shift・Ctrl・Alt・Win)",
            "",
            "時間帯ごと",
        };
        var max = Math.Max(1, a.Hours.Max());
        for (var h = 0; h < 24; h++)
            lines.Add($"{h,2} 時 {new string('█', (int)(30 * a.Hours[h] / max)),-30} {a.Hours[h]:N0}");
        return string.Join("\r\n", lines);
    }

    private static string Change(long now, long before) =>
        before == 0 ? "" : $", {(now - before) * 100.0 / before:+0;-0;±0}%";

    private void Import()
    {
        var choice = MessageBox.Show(this,
            "typect (タイプ数カウンター) で数えた打鍵数を、Meltype の打鍵数に足します。\n\n" +
            "「はい」: ファイル (CSV・テキスト) から読む\n「いいえ」: クリップボードから読む (typect の一覧を選んでコピーしておく)\n\n" +
            "1 行に「キー名, 回数」「日付, 回数」「合計, 回数」のどれかを書いた形を読みます。",
            "typect から引き継ぐ", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        string? text = null;
        if (choice == DialogResult.Yes)
        {
            using var dialog = new OpenFileDialog { Filter = "CSV・テキスト (*.csv;*.txt;*.tsv;*.log)|*.csv;*.txt;*.tsv;*.log|すべてのファイル (*.*)|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            text = ReadText(dialog.FileName);
        }
        else if (choice == DialogResult.No)
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        else return;
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this, "読めるものがありませんでした。", "typect から引き継ぐ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var result = _counter.Import(text);
        _counter.Save();
        Reload();
        MessageBox.Show(this,
            result.Total > 0 ? $"{result.Total:N0} 打を引き継ぎました (キー {result.Keys} 種類・{result.Days} 日分)。" : "打鍵数を読み取れませんでした。「キー名, 回数」の形か確かめてください。",
            "typect から引き継ぐ", MessageBoxButtons.OK, result.Total > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    /// <summary>UTF-8 で読めなければ Shift_JIS で読む (古い Windows のソフトの書き出し)。</summary>
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return new System.Text.UTF8Encoding(false, true).GetString(bytes); }
        catch (System.Text.DecoderFallbackException)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(932).GetString(bytes);
        }
    }

    private void Reset()
    {
        if (MessageBox.Show(this, "打鍵数をすべて 0 に戻しますか?", "打鍵数", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _counter.Reset();
        _counter.Save();
        Reload();
    }
}
