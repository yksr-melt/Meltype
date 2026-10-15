// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Drawing.Drawing2D;

namespace Meltype.Composition;

/// <summary>
/// 変換ボックス。フォーカスを奪わない最前面のウィンドウで、カーソル (キャレット) の下に出す。
/// 未確定の文字列に下線を引き、変換中は候補の一覧を出す。
/// </summary>
internal sealed class CompositionWindow : PopupForm
{
    private Palette _palette = Palette.Dark;
    private Color Background => _palette.Background;
    private Color Accent => _palette.Accent;

    /// <summary>変換ボックスの色の組み合わせ (設定の「変換ボックスの色」)。</summary>
    private sealed record Palette(Color Background, Color Text, Color Candidate, Color Hint, Color Underline, Color Accent,
        Color SuggestionEdge, Color SuggestionText, Color MeaningBackground, Color MeaningText)
    {
        public static readonly Palette Dark = new(Color.FromArgb(32, 34, 40), Color.White, Color.FromArgb(200, 200, 200), Color.FromArgb(150, 150, 150),
            Color.FromArgb(170, 170, 170), Color.FromArgb(76, 160, 255), Color.FromArgb(255, 196, 0), Color.FromArgb(255, 220, 120),
            Color.FromArgb(44, 47, 56), Color.FromArgb(225, 225, 225));

        public static readonly Palette Light = new(Color.FromArgb(250, 250, 252), Color.FromArgb(20, 20, 24), Color.FromArgb(60, 60, 66), Color.FromArgb(105, 105, 112),
            Color.FromArgb(120, 120, 120), Color.FromArgb(0, 103, 192), Color.FromArgb(214, 150, 0), Color.FromArgb(128, 80, 0),
            Color.FromArgb(240, 242, 246), Color.FromArgb(30, 30, 30));
    }

    /// <summary>色 (ライト / ダーク) と不透明度。変わったときだけ描き直す。</summary>
    public void SetAppearance(bool light, double opacity)
    {
        var palette = light ? Palette.Light : Palette.Dark;
        opacity = Math.Clamp(opacity, 0.3, 1.0);
        if (Math.Abs(Opacity - opacity) > 0.001)
        {
            Opacity = opacity;
            if (_meaningPopup is not null) _meaningPopup.Opacity = opacity;
        }
        if (palette == _palette) return;
        _palette = palette;
        BackColor = palette.Background;
        _meaningPopup?.SetPalette(palette);
        Invalidate();
    }

    /// <summary>
    /// Windows の「既定のアプリ モード」がライトか (HKCU\...\Themes\Personalize の AppsUseLightTheme)。読めなければ null。
    /// </summary>
    public static bool? WindowsUsesLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value ? value != 0 : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
    private const string DefaultFamily = "Yu Gothic UI";
    private Font _textFont = new(DefaultFamily, 13F);
    private Font _candidateFont = new(DefaultFamily, 11F);
    private Font _hintFont = new(DefaultFamily, 8.5F);
    private float _scale = 1F;
    private string _family = DefaultFamily;
    private string? _requestedFamily;

    /// <summary>文字の大きさの倍率 (1 = 打った文字が 13pt)。変わったときだけ作り直す。</summary>
    public void SetScale(float scale)
    {
        scale = Math.Clamp(scale, 0.6F, 2F);
        if (Math.Abs(scale - _scale) < 0.01F) return;
        _scale = scale;
        RebuildFonts();
    }

    /// <summary>フォント (設定の「変換ボックスのフォント」)。空・この PC に無いフォントなら既定のフォント。変わったときだけ作り直す。</summary>
    public void SetFontFamily(string? name)
    {
        name = name?.Trim() ?? "";
        if (name == _requestedFamily) return;
        _requestedFamily = name;
        var family = name.Length > 0 && IsInstalled(name) ? name : DefaultFamily;
        if (name.Length > 0 && family != name) Diagnostics.Log.Warn($"変換ボックスのフォント「{name}」がこの PC に無いので、{DefaultFamily} で出します。");
        if (family == _family) return;
        _family = family;
        RebuildFonts();
    }

    private static bool IsInstalled(string name)
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        return fonts.Families.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildFonts()
    {
        _textFont.Dispose();
        _candidateFont.Dispose();
        _hintFont.Dispose();
        _textFont = new Font(_family, 13F * _scale);
        _candidateFont = new Font(_family, 11F * _scale);
        // 案内の文字は小さくしすぎると読めないので、縮めるのは少しだけ
        _hintFont = new Font(_family, 8.5F * Math.Max(_scale, 0.9F));
    }

    /// <summary>倍率 1 のときの、打った文字の行の高さ (ピクセル)。</summary>
    public int BaseTextHeight { get; } = MeasureBaseHeight();

    private static int MeasureBaseHeight()
    {
        using var font = new Font(DefaultFamily, 13F);
        return font.Height;
    }
    private CompositionView? _view;
    private readonly ColorTextRenderer _color = new();

    public CompositionWindow()
    {
        BackColor = Background;
        Size = new Size(200, 40);
        _meaningTimer.Tick += (_, _) => ShowMeaning();
    }

    /// <summary>表示内容を更新する。anchor は表示位置 (キャレットの左下)。null なら今の位置のまま。</summary>
    /// <summary>変換ボックスの中で、打った文字の行の左端と、行の高さの真ん中の位置 (入力位置に重ねるときに使う)。</summary>
    public Point TextOffset => new(10, 8 + _textFont.Height / 2);

    /// <param name="overlay">anchor が入力位置に重ねる位置か。画面の下からはみ出すときは、候補の一覧が入るだけ上にずらす。</param>
    /// <param name="above">anchor が入力位置の上 (変換ボックスの左下の角を置く位置) か。候補の一覧が出て高くなっても下の端をそろえる。
    /// 画面の上に入らなければ belowY (入力位置の下) に出す。</param>
    public void ShowView(CompositionView view, Point? anchor, bool overlay = false, bool above = false, int belowY = 0)
    {
        _view = view;
        var size = Measure(view);
        if (anchor is not null) _above = above ? (anchor.Value.Y, belowY) : null;
        var location = anchor ?? Location;
        // 画面からはみ出さないようにする。
        var screen = Screen.FromPoint(location).WorkingArea;
        if (_above is { } a) location.Y = a.Bottom - size.Height >= screen.Top ? a.Bottom - size.Height : a.BelowY;
        if (location.X + size.Width > screen.Right) location.X = Math.Max(screen.Left, screen.Right - size.Width);
        if (location.Y + size.Height > screen.Bottom) location.Y = overlay ? Math.Max(screen.Top, screen.Bottom - size.Height) : Math.Max(screen.Top, location.Y - size.Height - 28);
        SetBounds(location.X, location.Y, size.Width, size.Height);
        if (!Visible) Show();
        Invalidate();
        UpdateMeaning(view);
    }

    /// <summary>入力位置の上に出しているときの、変換ボックスの下の端と、上に入らないときに出す位置 (入力位置の下)。</summary>
    private (int Bottom, int BelowY)? _above;

    /// <summary>打った文字の大きさで text を描いたときの幅 (ピクセル)。</summary>
    public int TextWidth(string text)
    {
        using var g = CreateGraphics();
        return TextRenderer.MeasureText(g, text, _textFont, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
    }

    /// <summary>「もしかして」の行の高さ。</summary>
    private int SuggestionHeight => _candidateFont.Height + 10;

    /// <summary>候補の一覧に一度に出す数 (Microsoft IME と同じく 9 個)。</summary>
    private const int PageSize = 9;

    /// <summary>候補で止まってから意味を出すまでの時間。</summary>
    private const int MeaningDelayMs = 1500;
    private readonly System.Windows.Forms.Timer _meaningTimer = new() { Interval = MeaningDelayMs };
    private MeaningPopup? _meaningPopup;
    private string? _meaningKey;

    /// <summary>選んでいる候補が変わったら意味を消し、同じ候補のまましばらく止まったら意味を出す。</summary>
    private void UpdateMeaning(CompositionView view)
    {
        var key = view is { Converting: true, Meaning: not null } && view.SelectedIndex >= 0 ? $"{view.SelectedIndex}\n{view.Candidates[view.SelectedIndex]}\n{view.Meaning}" : null;
        if (key == _meaningKey)
        {
            if (_meaningPopup is { Visible: true }) PlaceMeaning(view);
            return;
        }
        _meaningKey = key;
        _meaningTimer.Stop();
        _meaningPopup?.Hide();
        if (key is not null) _meaningTimer.Start();
    }

    private void ShowMeaning()
    {
        _meaningTimer.Stop();
        if (_view is not { Meaning: { } meaning } view || !Visible) return;
        _meaningPopup ??= new MeaningPopup(_palette, Opacity);
        _meaningPopup.SetText(meaning, _candidateFont);
        PlaceMeaning(view);
        if (!_meaningPopup.Visible) _meaningPopup.Show();
    }

    /// <summary>意味の枠を、変換ボックスの右 (はみ出すなら左) の、選んでいる候補の行の高さに置く。</summary>
    private void PlaceMeaning(CompositionView view)
    {
        if (_meaningPopup is null) return;
        var row = Math.Max(0, view.SelectedIndex) % PageSize;
        var y = Top + 8 + _textFont.Height + 8 + (view.Suggestion is null ? 0 : SuggestionHeight) + row * (_candidateFont.Height + 4) - 4;
        var size = _meaningPopup.Size;
        var screen = Screen.FromControl(this).WorkingArea;
        var x = Right + 4;
        if (x + size.Width > screen.Right) x = Math.Max(screen.Left, Left - size.Width - 4);
        y = Math.Clamp(y, screen.Top, Math.Max(screen.Top, screen.Bottom - size.Height));
        _meaningPopup.Location = new Point(x, y);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) return;
        _meaningTimer.Stop();
        _meaningKey = null;
        _meaningPopup?.Hide();
    }

    /// <summary>候補の意味を出す小さな枠 (フォーカスを奪わない)。</summary>
    private sealed class MeaningPopup : PopupForm
    {
        private string _text = "";
        private Font? _font;
        private Palette _palette;

        public MeaningPopup(Palette palette, double opacity)
        {
            _palette = palette;
            Opacity = opacity;
            BackColor = palette.MeaningBackground;
        }

        public void SetPalette(Palette palette)
        {
            _palette = palette;
            BackColor = palette.MeaningBackground;
            Invalidate();
        }

        public void SetText(string text, Font font)
        {
            _text = text;
            _font = font;
            var size = TextRenderer.MeasureText(text, font, new Size(420, 0), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            Size = new Size(size.Width + 16, size.Height + 10);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var border = new Pen(Color.FromArgb(110, _palette.Accent))) e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            if (_font is null) return;
            TextRenderer.DrawText(e.Graphics, _text, _font, new Rectangle(8, 5, Width - 16, Height - 10), _palette.MeaningText,
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>打ったキーを出すか。見えている文字と同じ (英字のまま) なら出さない。</summary>
    private static string? ShowsTyped(CompositionView view) =>
        view.Typed is { Length: > 0 } typed && typed != view.Text ? typed : null;

    private Size Measure(CompositionView view)
    {
        using var g = CreateGraphics();
        var width = TextRenderer.MeasureText(g, view.Text, _textFont).Width + 20;
        if (view.Clauses is { Count: > 0 } clauses)
        {
            var clausesWidth = clauses.Sum(c => TextRenderer.MeasureText(g, c, _textFont, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width + 4);
            width = Math.Max(width, clausesWidth + 20);
        }
        var height = _textFont.Height + 16;
        if (ShowsTyped(view) is { } typed)
        {
            width = Math.Max(width, TextRenderer.MeasureText(g, typed, _hintFont).Width + 20);
            height += _hintFont.Height + 2;
        }
        if (view.Suggestion is { } suggestion)
        {
            width = Math.Max(width, TextRenderer.MeasureText(g, suggestion, _candidateFont).Width + 32);
            height += SuggestionHeight;
        }
        if (view.Converting)
        {
            for (var i = 0; i < view.Candidates.Count; i++)
            {
                // 英訳の候補は、右に「英訳」と出す分だけ広くする
                var note = view.Notes?.ElementAtOrDefault(i) is { } n ? TextRenderer.MeasureText(g, n, _hintFont).Width + 12 : 0;
                width = Math.Max(width, TextRenderer.MeasureText(g, $"9  {view.Candidates[i]}", _candidateFont).Width + 28 + note);
            }
            height += Math.Min(view.Candidates.Count, PageSize) * (_candidateFont.Height + 4) + 6;
            // 2 ページ以上あるときは、下に「3 / 27」を出す分
            if (view.Candidates.Count > PageSize) height += _hintFont.Height + 2;
        }
        else if (view.Predictions is { Count: > 0 } predictions)
        {
            // 予測変換の候補 (番号は付けない。Tab で選ぶ)
            foreach (var prediction in predictions) width = Math.Max(width, TextRenderer.MeasureText(g, $"▸ {prediction}", _candidateFont).Width + 28);
            height += predictions.Count * (_candidateFont.Height + 4) + 6;
        }
        width = Math.Max(width, TextRenderer.MeasureText(g, view.Hint, _hintFont).Width + 16);
        height += _hintFont.Height + 6;
        return new Size(Math.Min(width, 900), height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var view = _view;
        if (view is null) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var border = new Pen(Accent)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        var y = 8;
        if (view.Clauses is { Count: > 0 } clauses)
        {
            // 変換中: 文節ごとに下線を引き、選択中の文節は背景を付けて太線にする。
            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            var x = 10;
            for (var i = 0; i < clauses.Count; i++)
            {
                var width = TextRenderer.MeasureText(g, clauses[i], _textFont, Size.Empty, flags).Width;
                var selected = i == view.SelectedClause;
                if (selected)
                {
                    using var highlight = new SolidBrush(Color.FromArgb(90, Accent));
                    g.FillRectangle(highlight, x - 1, y - 1, width + 2, _textFont.Height + 2);
                }
                DrawText(g, clauses[i], _textFont, new Point(x, y), _palette.Text, selected ? Blend(Background, Color.FromArgb(90, Accent)) : Background, flags);
                using (var underline = new Pen(selected ? Accent : _palette.Underline, selected ? 3 : 1))
                {
                    g.DrawLine(underline, x + 1, y + _textFont.Height + 1, x + width - 2, y + _textFont.Height + 1);
                }
                x += width + 4;
            }
            y += _textFont.Height + 8;
        }
        else
        {
            DrawText(g, view.Text, _textFont, new Point(10, y), _palette.Text, Background, TextFormatFlags.NoPrefix);
            var textWidth = TextRenderer.MeasureText(g, view.Text, _textFont).Width;
            y += _textFont.Height;
            using (var underline = new Pen(_palette.Text, 1) { DashStyle = DashStyle.Dot })
            {
                g.DrawLine(underline, 12, y, 10 + textWidth - 4, y);
            }
            y += 8;
        }

        if (ShowsTyped(view) is { } typed)
        {
            // 打ったキー (設定「打ったキーを表示」): 打ち間違いに気づけるように、文字の下に小さく出す
            TextRenderer.DrawText(g, typed, _hintFont, new Point(10, y - 2), _palette.Hint, TextFormatFlags.NoPrefix);
            y += _hintFont.Height + 2;
        }

        if (view.Suggestion is { } suggestion)
        {
            // もしかして: 打った文字のすぐ下に目立つように (Tab で直せる)
            var box = new Rectangle(6, y - 2, Width - 12, SuggestionHeight - 4);
            using (var fill = new SolidBrush(Color.FromArgb(70, _palette.SuggestionEdge))) g.FillRectangle(fill, box);
            using (var edge = new Pen(_palette.SuggestionEdge)) g.DrawRectangle(edge, box);
            TextRenderer.DrawText(g, suggestion, _candidateFont, new Point(12, y + 1), _palette.SuggestionText, TextFormatFlags.NoPrefix);
            y += SuggestionHeight;
        }

        if (view.Converting)
        {
            // 9 個ずつのページに分けて、選んでいる候補のページだけを出す (候補が多いと画面に収まらないため)。
            var first = Math.Max(0, view.SelectedIndex) / PageSize * PageSize;
            var listTop = y;
            for (var i = first; i < Math.Min(view.Candidates.Count, first + PageSize); i++)
            {
                var rowHeight = _candidateFont.Height + 4;
                if (i == view.SelectedIndex)
                {
                    using var highlight = new SolidBrush(Color.FromArgb(60, Accent));
                    g.FillRectangle(highlight, 4, y - 2, Width - 8, rowHeight);
                }
                DrawText(g, $"{i - first + 1}  {view.Candidates[i]}", _candidateFont, new Point(12, y),
                    i == view.SelectedIndex ? _palette.Text : _palette.Candidate,
                    i == view.SelectedIndex ? Blend(Background, Color.FromArgb(60, Accent)) : Background, TextFormatFlags.NoPrefix);
                if (view.Notes?.ElementAtOrDefault(i) is { } note)
                {
                    // 英訳の候補: 右端に小さく「英訳」
                    var noteWidth = TextRenderer.MeasureText(g, note, _hintFont).Width;
                    TextRenderer.DrawText(g, note, _hintFont, new Point(Width - noteWidth - 10, y + (_candidateFont.Height - _hintFont.Height) / 2), Accent, TextFormatFlags.NoPrefix);
                }
                y += rowHeight;
            }
            if (view.Candidates.Count > PageSize)
            {
                // 最後のページで候補が少なくても、ページ番号と案内は同じ位置に (ページを送っても窓の大きさが変わらないように)
                y = listTop + PageSize * (_candidateFont.Height + 4);
                var page = $"{Math.Max(0, view.SelectedIndex) + 1} / {view.Candidates.Count}";
                var pageWidth = TextRenderer.MeasureText(g, page, _hintFont).Width;
                TextRenderer.DrawText(g, page, _hintFont, new Point(Width - pageWidth - 10, y + 1), _palette.Hint, TextFormatFlags.NoPrefix);
                y += _hintFont.Height + 2;
            }
            y += 6;
        }
        else if (view.Predictions is { Count: > 0 } predictions)
        {
            for (var i = 0; i < predictions.Count; i++)
            {
                var rowHeight = _candidateFont.Height + 4;
                var selected = i == view.SelectedPrediction;
                if (selected)
                {
                    using var highlight = new SolidBrush(Color.FromArgb(60, Accent));
                    g.FillRectangle(highlight, 4, y - 2, Width - 8, rowHeight);
                }
                DrawText(g, $"{(selected ? "▸" : "  ")} {predictions[i]}", _candidateFont, new Point(12, y),
                    selected ? _palette.Text : _palette.Candidate,
                    selected ? Blend(Background, Color.FromArgb(60, Accent)) : Background, TextFormatFlags.NoPrefix);
                y += rowHeight;
            }
            y += 6;
        }
        TextRenderer.DrawText(g, view.Hint, _hintFont, new Point(8, y), _palette.Hint, TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// 文字列を描く。絵文字を含むならカラーで描く (GDI だと白黒になるため)。back はその場所の背景色 (半透明の強調を重ねた後の色)。
    /// </summary>
    private void DrawText(Graphics g, string text, Font font, Point location, Color fore, Color back, TextFormatFlags flags)
    {
        if (_color.Available && ColorTextRenderer.ContainsEmoji(text))
        {
            var size = TextRenderer.MeasureText(g, text, font, Size.Empty, flags);
            var width = Math.Min(size.Width + 8, Math.Max(1, Width - location.X - 2));
            var bounds = new Rectangle(location.X, location.Y, width, Math.Max(size.Height, font.Height));
            // GetHdc の間は Graphics のプロパティを読めないので、先に文字の大きさを求める。
            var pixels = font.SizeInPoints * g.DpiY / 72f;
            var hdc = g.GetHdc();
            try
            {
                if (_color.Draw(hdc, bounds, text, font.Name, pixels, fore, back)) return;
            }
            catch (Exception ex)
            {
                // カラーで描けなくても、変換ボックスは止めずに白黒で描く。
                Diagnostics.Log.Warn($"カラー絵文字を描けませんでした: {ex.Message}");
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }
        TextRenderer.DrawText(g, text, font, location, fore, flags);
    }

    /// <summary>背景に半透明の色を重ねた後の色。</summary>
    private static Color Blend(Color back, Color overlay) => Color.FromArgb(
        back.R + (overlay.R - back.R) * overlay.A / 255,
        back.G + (overlay.G - back.G) * overlay.A / 255,
        back.B + (overlay.B - back.B) * overlay.A / 255);

    protected override void Dispose(bool disposing)

    {
        if (disposing)
        {
            _textFont.Dispose();
            _candidateFont.Dispose();
            _hintFont.Dispose();
            _color.Dispose();
            _meaningTimer.Dispose();
            _meaningPopup?.Dispose();
        }
        base.Dispose(disposing);
    }
}
