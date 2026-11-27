// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Diagnostics;

namespace Meltype.UI;

/// <summary>ログ表示と判定理由のデバッグ表示 (設計書 §28)。ログはメモリ上にだけあり、設定でファイル出力も選べる。</summary>
internal sealed class LogForm : Form
{
    private readonly MeltypeEngine _engine;
    private readonly TextBox _text;
    private readonly Label _imeState;
    private readonly CheckBox _decisionsOnly;
    private readonly TextBox _testInput = new() { Dock = DockStyle.Top, ImeMode = ImeMode.Disable };
    private readonly TextBox _testResult = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F) };
    private readonly System.Windows.Forms.Timer _timer;
    private long _shownVersion = -1;

    public LogForm(MeltypeEngine engine)
    {
        _engine = engine;
        Text = "Meltype ログ / 判定理由";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(900, 520);
        Font = new Font("Yu Gothic UI", 9F);

        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9F),
        };
        _imeState = new Label { AutoSize = true, Padding = new Padding(0, 6, 12, 0) };
        _decisionsOnly = new CheckBox { Text = "判定と IME 切替だけ表示", AutoSize = true };
        _decisionsOnly.CheckedChanged += (_, _) => { _shownVersion = -1; RefreshLog(); };
        var copy = new Button { Text = "コピー", AutoSize = true };
        copy.Click += (_, _) => { if (_text.TextLength > 0) Clipboard.SetText(_text.Text); };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 6, 0) };
        top.Controls.AddRange([_imeState, _decisionsOnly, copy]);

        // 判定テスト: 打った英字をローマ字 (日本語) と英語のどちらと判定するかと理由を、動かしている設定で出す。
        // 判定理由を見る場所を 1 つにするため、設定画面からここに移した (issue #1934)
        var testLabel = new Label { Text = "判定テスト (ローマ字か英語かの判定。英字で入力):", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 2) };
        _testInput.TextChanged += (_, _) => RunTest();
        var testPanel = new Panel { Dock = DockStyle.Bottom, Height = 130, Padding = new Padding(6, 0, 6, 6) };
        testPanel.Controls.Add(_testResult);
        testPanel.Controls.Add(_testInput);
        testPanel.Controls.Add(testLabel);

        Controls.Add(_text);
        Controls.Add(top);
        Controls.Add(testPanel);

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => RefreshLog();
        _timer.Start();
        RefreshLog();
    }

    private void RefreshLog()
    {
        var state = _engine.Keyboard?.SystemImeState;
        _imeState.Text = $"前面アプリの IME: {(state is { } s ? s.ToString() : "不明")}";

        var version = Log.Version;
        if (version == _shownVersion) return;
        _shownVersion = version;
        var entries = Log.Snapshot().AsEnumerable();
        if (_decisionsOnly.Checked) entries = entries.Where(e => e.Level is LogLevel.Decision or LogLevel.Warn or LogLevel.Error);
        _text.Text = string.Join("\r\n", entries.Select(e => e.ToString()));
        _text.SelectionStart = _text.TextLength;
        _text.ScrollToCaret();
    }

    private void RunTest()
    {
        var lines = new List<string>();
        foreach (var word in _testInput.Text.ToLowerInvariant().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var letters = new string(word.Where(c => c is >= 'a' and <= 'z').ToArray());
            // 実際の動作と同じく 1 文字ずつ判定し、最初に結論が出た時点の結果を表示する。
            for (var i = 1; i <= letters.Length; i++)
            {
                var result = _engine.Evaluate(letters[..i]);
                if (result.Verdict != Detection.Verdict.Undecided || i == letters.Length)
                {
                    var verdict = result.Verdict == Detection.Verdict.Undecided ? "Unknown (Space で確定)" : result.Verdict.ToString();
                    lines.Add($"{word}: {verdict} — \"{letters[..i]}\" の時点, JP={result.JapaneseScore} EN={result.EnglishScore}\r\n    " +
                              string.Join("\r\n    ", result.Contributions.Select(c => c.ToString())));
                    break;
                }
            }
        }
        _testResult.Text = string.Join("\r\n", lines);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
