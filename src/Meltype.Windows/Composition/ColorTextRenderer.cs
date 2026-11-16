// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype.Composition;

/// <summary>
/// 絵文字をカラーで描く (変換ボックスの候補の一覧用)。GDI (TextRenderer) では絵文字が白黒になるので、
/// 絵文字を含む文字列だけ Direct2D + DirectWrite の「カラーフォントを有効にする」描画を使う。
/// WPF や追加のライブラリを使わずに、COM の関数表を直接呼ぶ (Windows 8.1 以降)。使えなければ Available が false。
/// </summary>
internal sealed unsafe class ColorTextRenderer : IDisposable
{
    private static readonly Guid IidD2D1Factory = new("06152247-6f50-465a-9245-118bfd3b6007");
    private static readonly Guid IidDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    [DllImport("d2d1.dll")] private static extern int D2D1CreateFactory(int factoryType, in Guid riid, IntPtr options, out IntPtr factory);
    [DllImport("dwrite.dll")] private static extern int DWriteCreateFactory(int factoryType, in Guid iid, out IntPtr factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct RenderTargetProperties
    {
        public int Type, Format, AlphaMode;
        public float DpiX, DpiY;
        public int Usage, MinLevel;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct RectF { public float Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ColorF { public float R, G, B, A; }

    // 関数表の位置 (d2d1.h / dwrite.h の宣言順)
    private const int Release = 2;
    private const int FactoryCreateDCRenderTarget = 16;
    private const int DWriteCreateTextFormat = 15;
    private const int TextFormatSetParagraphAlignment = 4, TextFormatSetWordWrapping = 5;
    private const int TargetCreateSolidColorBrush = 8, TargetDrawText = 27, TargetClear = 47, TargetBeginDraw = 48, TargetEndDraw = 49, TargetBindDC = 57;
    private const int DrawTextEnableColorFont = 4;

    private IntPtr _d2d, _dwrite, _target;
    private readonly Dictionary<(string, float), IntPtr> _formats = [];

    public bool Available { get; }

    public ColorTextRenderer()
    {
        try
        {
            if (D2D1CreateFactory(0, IidD2D1Factory, IntPtr.Zero, out _d2d) < 0) return;
            if (DWriteCreateFactory(0, IidDWriteFactory, out _dwrite) < 0) return;
            var properties = new RenderTargetProperties { Format = 87 /* B8G8R8A8_UNORM */, AlphaMode = 3 /* IGNORE */, DpiX = 96, DpiY = 96 };
            IntPtr target;
            var create = (delegate* unmanaged[Stdcall]<IntPtr, RenderTargetProperties*, IntPtr*, int>)Slot(_d2d, FactoryCreateDCRenderTarget);
            if (create(_d2d, &properties, &target) < 0) return;
            _target = target;
            Available = true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Diagnostics.Log.Info($"カラー絵文字を描けません (白黒で表示します): {ex.Message}");
        }
    }

    /// <summary>絵文字 (サロゲートペア・異体字セレクター・記号の絵文字) を含むか。</summary>
    public static bool ContainsEmoji(string text)
    {
        foreach (var c in text)
        {
            if (char.IsSurrogate(c) || c == '️' || c is >= '☀' and <= '➿' || c is >= '⬀' and <= '⯿') return true;
        }
        return false;
    }

    /// <summary>
    /// hdc の bounds (ピクセル) に text を描く。背景は back で塗る (DC に描いた内容とは重ねられないため)。
    /// fontSizePixels はピクセル単位の文字の大きさ。失敗したら false (呼び出し側で GDI で描く)。
    /// </summary>
    public bool Draw(IntPtr hdc, Rectangle bounds, string text, string fontFamily, float fontSizePixels, Color fore, Color back)
    {
        if (!Available || text.Length == 0) return false;
        var format = Format(fontFamily, fontSizePixels);
        if (format == IntPtr.Zero) return false;
        var rect = new Rect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        var bind = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Rect*, int>)Slot(_target, TargetBindDC);
        if (bind(_target, hdc, &rect) < 0) return false;

        ((delegate* unmanaged[Stdcall]<IntPtr, void>)Slot(_target, TargetBeginDraw))(_target);
        var backColor = ToColorF(back);
        ((delegate* unmanaged[Stdcall]<IntPtr, ColorF*, void>)Slot(_target, TargetClear))(_target, &backColor);

        var foreColor = ToColorF(fore);
        IntPtr brush;
        var createBrush = (delegate* unmanaged[Stdcall]<IntPtr, ColorF*, IntPtr, IntPtr*, int>)Slot(_target, TargetCreateSolidColorBrush);
        if (createBrush(_target, &foreColor, IntPtr.Zero, &brush) >= 0)
        {
            var layout = new RectF { Left = 0, Top = 0, Right = bounds.Width, Bottom = bounds.Height };
            fixed (char* chars = text)
            {
                var draw = (delegate* unmanaged[Stdcall]<IntPtr, char*, uint, IntPtr, RectF*, IntPtr, int, int, void>)Slot(_target, TargetDrawText);
                draw(_target, chars, (uint)text.Length, format, &layout, brush, DrawTextEnableColorFont, 0);
            }
            ReleaseObject(brush);
        }
        ulong tag1, tag2;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, ulong*, ulong*, int>)Slot(_target, TargetEndDraw))(_target, &tag1, &tag2);
        return hr >= 0;
    }

    private IntPtr Format(string family, float size)
    {
        if (_formats.TryGetValue((family, size), out var cached)) return cached;
        IntPtr format;
        fixed (char* familyName = family)
        fixed (char* locale = "ja-jp")
        {
            var create = (delegate* unmanaged[Stdcall]<IntPtr, char*, IntPtr, int, int, int, float, char*, IntPtr*, int>)Slot(_dwrite, DWriteCreateTextFormat);
            // 太さ 400 (標準)、斜体なし、幅 5 (標準)
            if (create(_dwrite, familyName, IntPtr.Zero, 400, 0, 5, size, locale, &format) < 0) format = IntPtr.Zero;
        }
        if (format != IntPtr.Zero)
        {
            // 上下中央・折り返さない
            ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(format, TextFormatSetParagraphAlignment))(format, 2);
            ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(format, TextFormatSetWordWrapping))(format, 1);
        }
        _formats[(family, size)] = format;
        return format;
    }

    private static ColorF ToColorF(Color color) => new() { R = color.R / 255f, G = color.G / 255f, B = color.B / 255f, A = 1 };

    private static IntPtr Slot(IntPtr instance, int index) => (*(IntPtr**)instance)[index];

    private static void ReleaseObject(IntPtr instance)
    {
        if (instance != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(instance, Release))(instance);
    }

    public void Dispose()
    {
        foreach (var format in _formats.Values) ReleaseObject(format);
        _formats.Clear();
        ReleaseObject(_target);
        ReleaseObject(_dwrite);
        ReleaseObject(_d2d);
        _target = _dwrite = _d2d = IntPtr.Zero;
    }
}
