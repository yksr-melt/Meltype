// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W

using Meltype.Input;

namespace Meltype.UI;

/// <summary>入力モード切り替えに使うキー操作を取り込む。</summary>
internal sealed class ShortcutCaptureForm : Form
{
    private readonly MeltypeEngine _engine;
    private readonly Label _shown = new() { AutoSize = true, Text = "「キーを取り込む」を押して、登録するキー操作を行ってください。" };
    private int _captureVersion;
    private ToggleShortcut? _candidate;
    private bool _recording;
    public ToggleShortcut? Shortcut { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<ToggleShortcut, string?> ValidateBinding { get; set; } = _ => null;

    public ShortcutCaptureForm(MeltypeEngine engine, ToggleShortcut? original)
    {
        _engine = engine;
        _candidate = original;
        Text = "入力モード切り替えキーの登録";
        Font = new Font("Yu Gothic UI", 9.5F);
        _shown.MaximumSize = new Size(LogicalToDeviceUnits(520), 0);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(16), WrapContents = false };
        var record = new Button { Text = "キーを取り込む", AutoSize = true };
        record.Click += (_, _) =>
        {
            _candidate = null; _recording = true;
            var version = ++_captureVersion;
            var recorder = new ShortcutRecorder();
            _shown.Text = "通常キー、または修飾キーと通常キーの組み合わせを押して離してください。";
            _engine.CaptureShortcut(e =>
            {
                var result = recorder.OnKey(e);
                if (result is null) return;
                _engine.CaptureShortcut(null);
                if (IsHandleCreated && !IsDisposed) BeginInvoke(() =>
                {
                    if (version != _captureVersion || !_recording) return;
                    _candidate = result; _recording = false;
                    _shown.Text = ShortcutEditor.Describe(result);
                });
            });
        };
        var ok = new Button { Text = "登録", AutoSize = true };
        ok.Click += (_, _) =>
        {
            if (_recording || _candidate is null) { _shown.Text = "キーを取り込み、すべてのキーを離してください。"; return; }
            var binding = _candidate;
            if (ValidateBinding(binding) is { } error) { _shown.Text = error; return; }
            Shortcut = binding; DialogResult = DialogResult.OK;
        };
        var cancel = new Button { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([ok, cancel]);
        body.Controls.AddRange([_shown, record, new Label { AutoSize = true, MaximumSize = new Size(LogicalToDeviceUnits(520), 0), Text = "日本語／英数の入力モードを切り替えます。\nAlt＋バッククォートと Alt＋半角／全角は別のキー操作として登録します。\nWindows や他のアプリで使うキー操作を登録すると、元の操作が使えなくなったり、入力モードの切り替えと同時に動いたりする場合があります。" }, buttons]);
        Controls.Add(body);
        if (original is not null) _shown.Text = ShortcutEditor.Describe(original);
        FormClosed += (_, _) => _engine.CaptureShortcut(null);
        Deactivate += (_, _) => { ++_captureVersion; _engine.CaptureShortcut(null); if (_recording) _candidate = null; _recording = false; };
    }

}
