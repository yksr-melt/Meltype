// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// 入力モード (あ / A) をカーソルの近くに一瞬だけ出す小さなウィンドウ。フォーカスを奪わない。
/// Meltype キーボードの使用中は Windows の IME を OFF にしているので、タスクバーの IME の表示は常に「A」になる。
/// 今どちらで入力されるかは、入力欄に入ったときと 半角/全角 を押したときにこれで知らせる。
/// Meltype IME では明示的に入力モードを切り替えたときに同じ表示を使う。
/// 入力できない理由 (前の版の Meltype IME が残っている) を、打っている所に出すのにも使う (<see cref="FlashNotice"/>)。
/// </summary>
internal sealed class ModeIndicatorWindow : PopupForm
{
    internal static readonly Color DefaultJapaneseColor = Color.FromArgb(76, 160, 255);
    internal static readonly Color DefaultDirectColor = Color.FromArgb(110, 110, 120);
    // 設定で変えた色 (issue #359)。
    private Color _japaneseColor = DefaultJapaneseColor, _directColor = DefaultDirectColor, _textColor = Color.White;
    private readonly Font _font = new("Yu Gothic UI", 14F, FontStyle.Bold);
    private readonly System.Windows.Forms.Timer _hideTimer = new();
    private readonly Font _noticeFont = new("Yu Gothic UI", 10F);
    private string _text = "あ";
    private Font _shown;

    public ModeIndicatorWindow()
    {
        _shown = _font;
        Size = new Size(34, 34);
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    // クリックは下のウィンドウへ通す。
    protected override bool ClickThrough => true;

    /// <summary>色を変える (設定「入力モードの表示の色」。null なら既定の色)。</summary>
    public void SetColors((int R, int G, int B)? japanese, (int R, int G, int B)? direct, (int R, int G, int B)? text)
    {
        static Color Of((int R, int G, int B)? rgb, Color fallback) => rgb is var (r, g, b) ? Color.FromArgb(r, g, b) : fallback;
        _japaneseColor = Of(japanese, DefaultJapaneseColor);
        _directColor = Of(direct, DefaultDirectColor);
        _textColor = Of(text, Color.White);
    }

    /// <summary>japanese なら「あ」、そうでなければ「A」を location に出し、しばらくしたら消す。</summary>
    public void Flash(bool japanese, Point location, int milliseconds)
    {
        _text = japanese ? "あ" : "A";
        _shown = _font;
        BackColor = japanese ? _japaneseColor : _directColor;
        Size = new Size(34, 34);
        Place(location, milliseconds);
    }

    /// <summary>text を、入力モードの表示と同じ所に、文字に合わせた大きさで出し、しばらくしたら消す。</summary>
    public void FlashNotice(string text, Point location, int milliseconds)
    {
        _text = text;
        _shown = _noticeFont;
        BackColor = _directColor;
        var size = TextRenderer.MeasureText(text, _noticeFont);
        Size = new Size(size.Width + 16, Math.Max(34, size.Height + 12));
        Place(location, milliseconds);
    }

    private void Place(Point location, int milliseconds)
    {
        var screen = Screen.FromPoint(location).WorkingArea;
        location.X = Math.Clamp(location.X, screen.Left, screen.Right - Width);
        location.Y = Math.Clamp(location.Y, screen.Top, screen.Bottom - Height);
        Location = location;
        if (!Visible) Show();
        Invalidate();
        _hideTimer.Stop();
        _hideTimer.Interval = milliseconds;
        _hideTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, _text, _shown, ClientRectangle, _textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer.Dispose();
            _font.Dispose();
            _noticeFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
