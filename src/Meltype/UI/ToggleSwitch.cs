// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 amanetoki7

using System.ComponentModel;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Meltype.UI;

/// <summary>
/// ON/OFF のスイッチ。Windows 11 の設定アプリ (WinUI 3 の ToggleSwitch) と同じ見た目と操作にする:
/// 40×20 の丸いトラックに丸いつまみ。ON はアクセント色で塗り、OFF は枠だけ。右に「ON」「OFF」の文字を出す。
/// クリックか Space で切り替える。Text を入れると、左に見出しを出す (横に並べる欄用)。
/// 表のセル (<see cref="ToggleSwitchCell"/>) も同じ描き方にするため、描画は static の DrawSwitch にまとめてある。
/// </summary>
internal sealed class ToggleSwitch : Control
{
    // WinUI 3 の寸法 (96 DPI でのピクセル)。つまみは、カーソルを乗せると少し大きく、押している間は横に伸びる
    private const int TrackWidth = 40, TrackHeight = 20, Knob = 12, KnobHover = 14, KnobPressedWidth = 17, KnobPressedHeight = 14;
    private const int Gap = 12; // トラックと文字の間
    private const int Pad = 4;  // フォーカスの枠を描く余白
    private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;

    private bool _checked, _hover, _pressed, _spaceDown;
    private string _onText = "ON", _offText = "OFF";

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        AccessibleRole = AccessibleRole.CheckButton;
        TabStop = true;
        AutoSize = true;
        FitToContent();
    }

    public event EventHandler? CheckedChanged;

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>ON のときに右に出す文字。</summary>
    [DefaultValue("ON")]
    public string OnText
    {
        get => _onText;
        set { _onText = value; FitToContent(); }
    }

    /// <summary>OFF のときに右に出す文字。</summary>
    [DefaultValue("OFF")]
    public string OffText
    {
        get => _offText;
        set { _offText = value; FitToContent(); }
    }

    /// <summary>利用者が切り替えたとき (クリック・Space キー・アクセシビリティの操作。プログラムから Checked を変えたときは起きない)。</summary>
    public event EventHandler? Toggled;

    public void Toggle()
    {
        Checked = !Checked;
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    protected override Size DefaultSize => new(TrackWidth + Gap + 30 + Pad * 2, TrackHeight + Pad * 2);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var dpi = DeviceDpi;
        var header = Text.Length > 0 ? Measure(Text, Font).Width + Scale(Gap, dpi) : 0;
        var body = SwitchSize(dpi, Font, _onText, _offText);
        return new Size(Scale(Pad, dpi) * 2 + header + body.Width, Scale(Pad, dpi) * 2 + body.Height);
    }

    private void FitToContent()
    {
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitToContent();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        FitToContent();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        FitToContent();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var dpi = DeviceDpi;
        var fore = Enabled ? ForeColor : SystemColors.GrayText;
        var x = Scale(Pad, dpi);
        if (Text.Length > 0)
        {
            var size = Measure(Text, Font);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(x, 0, size.Width, Height), fore, Flags);
            x += size.Width + Scale(Gap, dpi);
        }
        // 押したまま外に出している間は、押していない見た目に戻す (ボタンと同じ。戻ってきたらまた押した見た目)
        var pressed = (_pressed && _hover) || _spaceDown;
        var track = DrawSwitch(e.Graphics, new Point(x, (Height - Scale(TrackHeight, dpi)) / 2), dpi, _checked, Enabled, _hover, pressed, Font, fore, _checked ? _onText : _offText);
        if (Focused && ShowFocusCues) DrawFocus(e.Graphics, track, dpi, ForeColor);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_pressed) return;
        _pressed = false;
        // 押したまま外に出して離したら切り替えない (ボタンと同じ)
        if (ClientRectangle.Contains(e.Location)) Toggle();
        else Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && !e.Alt && !e.Control)
        {
            _spaceDown = true;
            Invalidate();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && _spaceDown)
        {
            _spaceDown = false;
            Toggle();
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _spaceDown = false; // Space を押したまま Alt+Tab などで離れたら、戻っても切り替えない
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessibleObject(this);

    /// <summary>ナレーターにチェックボックスとして読ませる (状態と「切り替え」の操作付き)。</summary>
    private sealed class SwitchAccessibleObject(ToggleSwitch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.CheckButton;
        public override AccessibleStates State => base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);
        public override string DefaultAction => "切り替え";
        public override string? Value
        {
            get => owner.Checked ? owner.OnText : owner.OffText;
            set { }
        }
        public override void DoDefaultAction() => owner.Toggle();
    }

    // ---- 描画 (この部品と表のセルで共通) ----

    private static int Scale(int logical, int dpi) => (int)Math.Round(logical * dpi / 96.0);

    private static Size Measure(string text, Font font) => TextRenderer.MeasureText(text, font, Size.Empty, Flags);

    /// <summary>トラック (つまみの入る丸い枠) の大きさ。</summary>
    internal static Size TrackSize(int dpi) => new(Scale(TrackWidth, dpi), Scale(TrackHeight, dpi));

    /// <summary>トラックと、その右の文字 (ON / OFF の広い方) を合わせた大きさ。文字が無ければトラックだけ。</summary>
    internal static Size SwitchSize(int dpi, Font font, string? onText, string? offText)
    {
        var track = TrackSize(dpi);
        if (string.IsNullOrEmpty(onText) && string.IsNullOrEmpty(offText)) return track;
        var on = Measure(onText ?? "", font);
        var off = Measure(offText ?? "", font);
        return new Size(track.Width + Scale(Gap, dpi) + Math.Max(on.Width, off.Width), Math.Max(track.Height, Math.Max(on.Height, off.Height)));
    }

    /// <summary>
    /// location を左上にしてトラックとつまみを描き、stateText があれば右に出す。戻り値はトラックの範囲 (フォーカスの枠・クリックの判定に使う)。
    /// 色は WinUI 3 のライトテーマの値。OFF: 黒 45% の枠と黒 62% のつまみ / ON: アクセント色の塗りと白いつまみ。
    /// ハイコントラストでは Windows の色をそのまま使う。
    /// </summary>
    internal static Rectangle DrawSwitch(Graphics g, Point location, int dpi, bool on, bool enabled, bool hover, bool pressed, Font font, Color foreColor, string? stateText)
    {
        var track = new Rectangle(location, TrackSize(dpi));
        Color fill, border, knob;
        if (SystemInformation.HighContrast)
        {
            border = enabled ? SystemColors.WindowText : SystemColors.GrayText;
            fill = on ? SystemColors.Highlight : SystemColors.Window;
            knob = on ? SystemColors.HighlightText : border;
        }
        else if (on)
        {
            var accent = AccentColor();
            fill = !enabled ? Color.FromArgb(55, 0, 0, 0) : pressed ? Color.FromArgb(204, accent) : hover ? Color.FromArgb(230, accent) : accent;
            border = fill;
            knob = Color.White;
        }
        else
        {
            fill = !enabled ? Color.Transparent : pressed ? Color.FromArgb(24, 0, 0, 0) : hover ? Color.FromArgb(15, 0, 0, 0) : Color.FromArgb(6, 0, 0, 0);
            border = Color.FromArgb(enabled ? 114 : 55, 0, 0, 0);
            knob = Color.FromArgb(enabled ? 158 : 92, 0, 0, 0);
        }

        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Pill(track))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }
        using (var path = Pill(RectangleF.Inflate(track, -0.5f, -0.5f)))
        using (var pen = new Pen(border))
        {
            g.DrawPath(pen, path);
        }
        int knobWidth, knobHeight;
        if (pressed && enabled)
        {
            knobWidth = Scale(KnobPressedWidth, dpi);
            knobHeight = Scale(KnobPressedHeight, dpi);
        }
        else
        {
            knobWidth = knobHeight = Scale(hover && enabled ? KnobHover : Knob, dpi);
        }
        var margin = (track.Height - knobHeight) / 2;
        var knobX = on ? track.Right - margin - knobWidth : track.Left + margin;
        using (var brush = new SolidBrush(knob))
        {
            g.FillEllipse(brush, new Rectangle(knobX, track.Top + margin, knobWidth, knobHeight));
        }
        g.SmoothingMode = smoothing;

        if (!string.IsNullOrEmpty(stateText))
        {
            var size = Measure(stateText, font);
            TextRenderer.DrawText(g, stateText, font, new Rectangle(track.Right + Scale(Gap, dpi), track.Top, size.Width, track.Height), foreColor, Flags);
        }
        return track;
    }

    /// <summary>キーボードで選んでいるときの枠 (WinUI 3 と同じく、トラックの少し外に 2px の線)。</summary>
    internal static void DrawFocus(Graphics g, Rectangle track, int dpi, Color color)
    {
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, Scale(2, dpi));
        using var path = Pill(RectangleF.Inflate(track, Scale(2, dpi), Scale(2, dpi)));
        g.DrawPath(pen, path);
        g.SmoothingMode = smoothing;
    }

    /// <summary>両端が半円の長方形。</summary>
    private static GraphicsPath Pill(RectangleF bounds)
    {
        var diameter = bounds.Height;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }

    private static Color? _accent;

    static ToggleSwitch()
    {
        // 「設定 > 個人用設定 > 色」でアクセント色を変えたら読み直す
        SystemEvents.UserPreferenceChanged += (_, _) => _accent = null;
    }

    /// <summary>Windows のアクセント色 (HKCU\...\DWM の AccentColor、ABGR)。読めなければトレイのアイコンと同じ青。</summary>
    internal static Color AccentColor()
    {
        if (_accent is { } cached) return cached;
        var color = Color.FromArgb(0, 120, 212);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr) color = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        _accent = color;
        return color;
    }
}

/// <summary>
/// 表の中の ON/OFF の列 (アプリ別設定の「有効」・独自の種類の「最初は英数」)。
/// チェックボックスの列の動き (クリック・Space で切り替え、値は bool) のまま、見た目を <see cref="ToggleSwitch"/> にする。
/// </summary>
internal sealed class ToggleSwitchColumn : DataGridViewCheckBoxColumn
{
    public ToggleSwitchColumn(string header, int weight)
    {
        CellTemplate = new ToggleSwitchCell();
        HeaderText = header;
        FillWeight = weight;
    }
}

internal sealed class ToggleSwitchCell : DataGridViewCheckBoxCell
{
    private const int Margin = 6; // セルの左端からの余白 (96 DPI)
    private int _hoverRow = -1, _pressedRow = -1;

    /// <summary>クリックで切り替わる範囲 = スイッチと文字。セルの中の座標。</summary>
    protected override Rectangle GetContentBounds(Graphics graphics, DataGridViewCellStyle cellStyle, int rowIndex)
    {
        if (DataGridView is null || rowIndex < 0 || ColumnIndex < 0) return Rectangle.Empty;
        return Content(new Rectangle(Point.Empty, GetSize(rowIndex)), cellStyle);
    }

    private Rectangle Content(Rectangle cell, DataGridViewCellStyle style)
    {
        var dpi = DataGridView!.DeviceDpi;
        var size = ToggleSwitch.SwitchSize(dpi, style.Font ?? DataGridView.Font, "ON", "OFF");
        var track = ToggleSwitch.TrackSize(dpi);
        var x = cell.X + style.Padding.Left + (int)Math.Round(Margin * dpi / 96.0);
        return new Rectangle(x, cell.Y + (cell.Height - track.Height) / 2, size.Width, track.Height);
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates cellState,
        object? value, object? formattedValue, string? errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
        if (DataGridView is null) return;
        var selected = (cellState & DataGridViewElementStates.Selected) != 0;
        if ((paintParts & DataGridViewPaintParts.Background) != 0)
        {
            using var brush = new SolidBrush(selected ? cellStyle.SelectionBackColor : cellStyle.BackColor);
            graphics.FillRectangle(brush, cellBounds);
        }
        if ((paintParts & DataGridViewPaintParts.Border) != 0) PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);
        if ((paintParts & DataGridViewPaintParts.ContentForeground) != 0)
        {
            // 編集中 (クリックした直後) は formattedValue に切り替え後の値が来る
            var on = formattedValue is true || formattedValue is CheckState.Checked;
            var enabled = !ReadOnly && DataGridView.Enabled;
            var pressed = rowIndex == _pressedRow;
            var fore = !enabled ? SystemColors.GrayText : selected ? cellStyle.SelectionForeColor : cellStyle.ForeColor;
            var font = cellStyle.Font ?? DataGridView.Font;
            var content = Content(cellBounds, cellStyle);
            var track = ToggleSwitch.DrawSwitch(graphics, content.Location, DataGridView.DeviceDpi, on, enabled, rowIndex == _hoverRow, pressed, font, fore, on ? "ON" : "OFF");
            var current = DataGridView.CurrentCellAddress.X == ColumnIndex && DataGridView.CurrentCellAddress.Y == rowIndex;
            if ((paintParts & DataGridViewPaintParts.Focus) != 0 && current && DataGridView.Focused) ToggleSwitch.DrawFocus(graphics, track, DataGridView.DeviceDpi, fore);
        }
        if ((paintParts & DataGridViewPaintParts.ErrorIcon) != 0 && DataGridView.ShowCellErrors && !string.IsNullOrEmpty(errorText)) PaintErrorIcon(graphics, clipBounds, cellBounds, errorText);
    }

    protected override void OnMouseEnter(int rowIndex)
    {
        base.OnMouseEnter(rowIndex);
        _hoverRow = rowIndex;
        DataGridView?.InvalidateCell(ColumnIndex, rowIndex);
    }

    protected override void OnMouseLeave(int rowIndex)
    {
        base.OnMouseLeave(rowIndex);
        _hoverRow = -1;
        _pressedRow = -1;
        DataGridView?.InvalidateCell(ColumnIndex, rowIndex);
    }

    // 押している間はつまみが横に伸びる (チェックボックスのセルの押下状態は外から読めないので、自分で持つ)
    protected override void OnMouseDown(DataGridViewCellMouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !GetContentBounds(e.RowIndex).Contains(e.X, e.Y)) return;
        _pressedRow = e.RowIndex;
        DataGridView?.InvalidateCell(ColumnIndex, e.RowIndex);
    }

    protected override void OnMouseUp(DataGridViewCellMouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pressedRow < 0) return;
        var row = _pressedRow;
        _pressedRow = -1;
        DataGridView?.InvalidateCell(ColumnIndex, row);
    }
}
