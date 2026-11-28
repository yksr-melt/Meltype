// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meltype.Composition;

/// <summary>
/// Meltype IME (TSF) から届いたキャレット位置に対して、滑らかに追従するクリックスルー・レイヤードオーバーレイ。
/// UI Automation のポーリングや常駐スレッドを持たず、移動中だけタイマーを動かして単一 DIB 上でマスクとカーソルを合成描画する。
/// </summary>
internal sealed class SmoothCaretWindow : PopupForm
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const int FrameIntervalMs = 8;
    private const int MaxBitmapWidth = 320;
    private const int MaxBitmapHeight = 128;
    private const int BackgroundColorTolerance = 10;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const uint ULW_ALPHA = 0x00000002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_NOSENDCHANGING = 0x0400;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private readonly Control _invoker;
    private readonly SmoothCaretMotion _motion = new();
    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = FrameIntervalMs };
    private readonly Native.WinEventProc _winEventCallback;
    private int[]? _pixelScratch;

    private bool _enabled;
    private bool _layeredCleared = true;
    private uint _activeThread;
    private IntPtr _lastForeground;
    private IntPtr _caretHook;
    private long _lastFrameTicks;

    // 以下の色キャッシュとサンプリング状態はすべて UI スレッドからのみ読み書きする
    private int _caretRgb = 0x141414;
    private int _backgroundRgb = 0xFFFFFF;
    private bool _hasUniformBackground;
    private IntPtr _sampledForeground;
    private long _lastColorSampleMs;
    private bool _sampleInFlight;

    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _oldBitmap;
    private IntPtr _dibBits;

    private readonly record struct ColorSample(int CaretRgb, int BestRgb, int BestMatches);

    public SmoothCaretWindow(Control invoker)
    {
        _invoker = invoker;
        _winEventCallback = OnWinEvent;
        MinimumSize = new Size(1, 1);
        Size = new Size(4, 18);
        _animTimer.Tick += (_, _) => OnAnimationTick();
    }

    protected override bool ClickThrough => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED;
            return cp;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (!enabled)
        {
            _animTimer.Stop();
            UninstallHook();
            _activeThread = 0;
            _motion.Reset();
            ClearLayered();
            if (Visible) Hide();
        }
    }

    public void ClearThread(uint thread)
    {
        if (_activeThread != thread || thread == 0) return;
        _activeThread = 0;
        _animTimer.Stop();
        _motion.Reset();
        ClearLayered();
    }

    public void OnImeCaret(uint thread, Rectangle? caret, bool isInput, bool focusEntered)
    {
        if (!_enabled || IsDisposed || thread == 0) return;
        if (_activeThread != thread)
        {
            _activeThread = thread;
            _animTimer.Stop();
            _motion.Reset();
            ClearLayered();
        }

        InstallHook();

        if (focusEntered)
        {
            _animTimer.Stop();
            ClearLayered();
            if (caret is { } entered && IsValidScreenCaret(entered))
            {
                RequestColorSample(entered);
                _motion.Snap(entered.Left, entered.Top, entered.Height, ResolveWidth(entered.Width));
            }
            else
            {
                _motion.Reset();
            }
            return;
        }

        if (isInput)
            _motion.NotifyInput();

        if (caret is { } rect)
            ApplyCaretRect(rect);
    }

    public void OnCaretRect(uint thread, Rectangle rect)
    {
        if (!_enabled || IsDisposed || thread == 0) return;
        if (_activeThread != thread)
        {
            _activeThread = thread;
            _animTimer.Stop();
            _motion.Reset();
            ClearLayered();
        }

        InstallHook();
        ApplyCaretRect(rect);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // アクティブな Meltype IME の入力スレッドで発生したキャレット移動だけを扱う (アイドル時・他アプリの負荷ゼロ)
        if (!_enabled || IsDisposed || idObject != unchecked((int)Native.OBJID_CARET) || thread == 0 || thread != _activeThread)
            return;

        // UI スレッドから対象アプリへ同期 SendMessage する AccessibleObjectFromWindow は使わず、
        // カーネル状態のみを読む GetGUIThreadInfo でデッドロックを防ぎつつ自前補間や Win32 キャレットを捉える
        if (TryReadWin32Caret(thread) is { } rect)
            ApplyCaretRect(rect);
    }

    private static Rectangle? TryReadWin32Caret(uint thread)
    {
        var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(thread, ref info) || info.hwndCaret == IntPtr.Zero || info.rcCaret.Bottom <= info.rcCaret.Top)
            return null;

        var top = new Native.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
        if (!Native.ClientToScreen(info.hwndCaret, ref top) || (top.X == 0 && top.Y == 0))
            return null;

        var width = Math.Clamp(info.rcCaret.Right - info.rcCaret.Left, 1, 3);
        var height = Math.Max(10, info.rcCaret.Bottom - info.rcCaret.Top);
        return height < 200 ? new Rectangle(top.X, top.Y, width, height) : null;
    }

    private void ApplyCaretRect(Rectangle rect)
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground != IntPtr.Zero && foreground != _lastForeground)
        {
            _lastForeground = foreground;
            _hasUniformBackground = false;
            _animTimer.Stop();
            _motion.Reset();
            ClearLayered();
        }

        if (!IsValidScreenCaret(rect)) return;

        RequestColorSample(rect);

        var caretWidth = ResolveWidth(rect.Width);
        var useMask = _hasUniformBackground && foreground == _sampledForeground;
        var wasActive = _motion.IsActive;
        if (_motion.SetTarget(rect.Left, rect.Top, rect.Height, caretWidth, nativeCaretMasked: useMask, timestampMs: Environment.TickCount64))
        {
            EnsureWindowReady();
            if (!wasActive)
            {
                _ = SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
                _lastFrameTicks = Stopwatch.GetTimestamp();
                RenderLayeredAtomic(nativeCaretMasked: useMask);
                _animTimer.Start();
            }
            else
            {
                RenderLayeredAtomic(nativeCaretMasked: useMask);
            }
        }
        else if (wasActive)
        {
            _animTimer.Stop();
            ClearLayered();
        }
    }

    private static bool IsValidScreenCaret(Rectangle rect) =>
        rect.Height is > 0 and < 200 && (rect.X != 0 || rect.Y != 0) &&
        Screen.AllScreens.Any(s => s.Bounds.Contains(rect.Location));

    private static int ResolveWidth(int reportedWidth) =>
        reportedWidth is >= 1 and <= 3 ? reportedWidth : Math.Clamp(SystemInformation.CaretWidth, 1, 2);

    private void RequestColorSample(Rectangle rect)
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero || _sampleInFlight) return;
        var now = Environment.TickCount64;
        if (foreground == _sampledForeground && (now - _lastColorSampleMs < 1500 || _motion.IsActive))
            return;

        _sampleInFlight = true;
        _lastColorSampleMs = now;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var sample = SampleScreenColors(rect);
            try
            {
                if (!IsDisposed && !_invoker.IsDisposed && _invoker.IsHandleCreated)
                    _invoker.BeginInvoke(() => ApplyColorSample(foreground, sample));
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
            }
        });
    }

    private void ApplyColorSample(IntPtr foreground, ColorSample? sample)
    {
        _sampleInFlight = false;
        var sameWindow = foreground == _sampledForeground;
        _sampledForeground = foreground;
        if (sample is not { } s)
        {
            if (!sameWindow) _hasUniformBackground = false;
            return;
        }

        _caretRgb = s.CaretRgb;
        if (s.BestMatches >= 4)
        {
            _backgroundRgb = s.BestRgb;
            _hasUniformBackground = true;
        }
        else if (!sameWindow || s.BestMatches <= 2)
        {
            _hasUniformBackground = false;
        }
    }

    /// <summary>インスタンス状態に一切触れず、ワーカースレッド上で画面の余白 6 点をサンプルして返す純粋関数。</summary>
    private static ColorSample? SampleScreenColors(Rectangle rect)
    {
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return null;
        try
        {
            Span<int> samples = stackalloc int[6];
            var count = 0;
            var topY = rect.Top + 1;
            var bottomY = Math.Max(topY, rect.Bottom - 2);
            ReadSample(hdc, rect.Left + 4, topY, samples, ref count);
            ReadSample(hdc, rect.Left + 12, topY, samples, ref count);
            ReadSample(hdc, rect.Left - 6, topY, samples, ref count);
            ReadSample(hdc, rect.Left + 4, bottomY, samples, ref count);
            ReadSample(hdc, rect.Left + 12, bottomY, samples, ref count);
            ReadSample(hdc, rect.Left - 6, bottomY, samples, ref count);

            if (count == 0) return null;

            var bestRgb = samples[0];
            var bestMatches = 0;
            for (var i = 0; i < count; i++)
            {
                var matches = 0;
                for (var j = 0; j < count; j++)
                {
                    if (ColorsClose(samples[i], samples[j], BackgroundColorTolerance))
                        matches++;
                }
                if (matches > bestMatches)
                {
                    bestMatches = matches;
                    bestRgb = samples[i];
                }
            }

            var r = (bestRgb >> 16) & 0xFF;
            var g = (bestRgb >> 8) & 0xFF;
            var b = bestRgb & 0xFF;
            var luminance = (r * 299 + g * 587 + b * 114) / 1000;
            var caretRgb = luminance >= 128 ? 0x141414 : 0xF2F2F2;
            return new ColorSample(caretRgb, bestRgb, bestMatches);
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static void ReadSample(IntPtr hdc, int x, int y, Span<int> samples, ref int count)
    {
        var pixel = GetPixel(hdc, x, y);
        if (pixel == uint.MaxValue) return;
        var r = (int)(pixel & 0xFF);
        var g = (int)((pixel >> 8) & 0xFF);
        var b = (int)((pixel >> 16) & 0xFF);
        samples[count++] = (r << 16) | (g << 8) | b;
    }

    private static bool ColorsClose(int rgbA, int rgbB, int tolerance)
    {
        var dr = Math.Abs(((rgbA >> 16) & 0xFF) - ((rgbB >> 16) & 0xFF));
        var dg = Math.Abs(((rgbA >> 8) & 0xFF) - ((rgbB >> 8) & 0xFF));
        var db = Math.Abs((rgbA & 0xFF) - (rgbB & 0xFF));
        return dr <= tolerance && dg <= tolerance && db <= tolerance;
    }

    private void OnAnimationTick()
    {
        if (!_enabled || IsDisposed)
        {
            _animTimer.Stop();
            ClearLayered();
            return;
        }

        var nowTicks = Stopwatch.GetTimestamp();
        var dtMs = Math.Clamp((nowTicks - _lastFrameTicks) * 1000f / Stopwatch.Frequency, 0.5f, 50f);
        _lastFrameTicks = nowTicks;

        var useMask = _hasUniformBackground && _lastForeground == _sampledForeground;
        if (_motion.Step(dtMs, nativeCaretMasked: useMask))
        {
            RenderLayeredAtomic(nativeCaretMasked: useMask);
        }
        else
        {
            _animTimer.Stop();
            ClearLayered();
        }
    }

    private void InstallHook()
    {
        if (_caretHook != IntPtr.Zero) return;
        const uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        _caretHook = Native.SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _winEventCallback, 0, 0, flags);
    }

    private void UninstallHook()
    {
        if (_caretHook == IntPtr.Zero) return;
        Native.UnhookWinEvent(_caretHook);
        _caretHook = IntPtr.Zero;
    }

    private void EnsureWindowReady()
    {
        EnsureGdiBuffer();
        if (!Visible)
        {
            ClearLayered();
            ShowOnTop();
        }
    }

    private void EnsureGdiBuffer()
    {
        if (_dib != IntPtr.Zero) return;
        _pixelScratch ??= new int[MaxBitmapWidth * MaxBitmapHeight];
        _memDc = CreateCompatibleDC(IntPtr.Zero);
        var bmi = new BITMAPINFO
        {
            biSize = Marshal.SizeOf<BITMAPINFO>(),
            biWidth = MaxBitmapWidth,
            biHeight = -MaxBitmapHeight,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };
        _dib = CreateDIBSection(_memDc, ref bmi, 0, out _dibBits, IntPtr.Zero, 0);
        if (_dib != IntPtr.Zero)
            _oldBitmap = SelectObject(_memDc, _dib);
    }

    private void RenderLayeredAtomic(bool nativeCaretMasked)
    {
        EnsureGdiBuffer();
        if (_dib == IntPtr.Zero || _dibBits == IntPtr.Zero || _pixelScratch is null || !IsHandleCreated) return;

        var opacity = Math.Clamp(_motion.Opacity, 0f, 1f);
        if (opacity <= 0.01f)
        {
            ClearLayered();
            return;
        }

        var currentX = _motion.CurrentX;
        var caretBaseX = (int)MathF.Floor(currentX);
        var caretY = (int)MathF.Round(_motion.CurrentY);
        var caretHeight = Math.Clamp((int)MathF.Round(_motion.CurrentHeight), 10, MaxBitmapHeight);
        var caretWidth = Math.Clamp(_motion.CurrentWidth, 1f, 2.5f);
        var caretRightX = caretBaseX + 4;

        var targetX = (int)MathF.Round(_motion.TargetX);
        var targetY = (int)MathF.Round(_motion.TargetY);
        var targetHeight = Math.Clamp((int)MathF.Round(_motion.TargetHeight), 10, MaxBitmapHeight);
        var nativeWidth = Math.Clamp((int)MathF.Round(_motion.TargetWidth), 1, 2);
        var movingRight = _motion.TargetX >= _motion.CurrentX;
        var maskWidth = movingRight ? nativeWidth + 1 : nativeWidth;
        var maskRightX = targetX + maskWidth;

        var boxLeft = nativeCaretMasked ? Math.Min(caretBaseX, targetX) : caretBaseX;
        var boxRight = nativeCaretMasked ? Math.Max(caretRightX, maskRightX) : caretRightX;
        var boxTop = nativeCaretMasked ? Math.Min(caretY, targetY) : caretY;
        var boxBottom = nativeCaretMasked ? Math.Max(caretY + caretHeight, targetY + targetHeight) : caretY + caretHeight;

        var boxWidth = boxRight - boxLeft;
        var boxHeight = boxBottom - boxTop;
        if (boxWidth > MaxBitmapWidth || boxHeight > MaxBitmapHeight)
        {
            nativeCaretMasked = false;
            boxLeft = caretBaseX;
            boxRight = caretRightX;
            boxTop = caretY;
            boxBottom = caretY + caretHeight;
            boxWidth = Math.Clamp(boxRight - boxLeft, 1, MaxBitmapWidth);
            boxHeight = Math.Clamp(boxBottom - boxTop, 1, MaxBitmapHeight);
        }

        Array.Clear(_pixelScratch, 0, MaxBitmapWidth * boxHeight);

        var bgRgb = _backgroundRgb;
        var bgR = (bgRgb >> 16) & 0xFF;
        var bgG = (bgRgb >> 8) & 0xFF;
        var bgB = bgRgb & 0xFF;
        if (nativeCaretMasked)
        {
            var opaqueBg = unchecked((int)0xFF000000) | (bgRgb & 0x00FFFFFF);
            var localMaskX = targetX - boxLeft;
            var localMaskY = targetY - boxTop;
            var maxMaskRow = Math.Min(boxHeight, localMaskY + targetHeight);
            var maxMaskCol = Math.Min(boxWidth, localMaskX + maskWidth);
            for (var y = Math.Max(0, localMaskY); y < maxMaskRow; y++)
            {
                var rowOffset = y * MaxBitmapWidth;
                for (var x = Math.Max(0, localMaskX); x < maxMaskCol; x++)
                    _pixelScratch[rowOffset + x] = opaqueBg;
            }
        }

        var fracX = currentX - caretBaseX;
        var sameLineAsTarget = MathF.Abs(_motion.TargetY - _motion.CurrentY) < 4f;
        var targetColStart = targetX;
        var targetColEnd = targetX + nativeWidth - 1;

        var rgb = _caretRgb;
        var cR = (rgb >> 16) & 0xFF;
        var cG = (rgb >> 8) & 0xFF;
        var cB = rgb & 0xFF;

        Span<int> colAlpha = stackalloc int[4];
        var anyVisible = nativeCaretMasked;
        for (var col = 0; col < 4; col++)
        {
            var screenX = caretBaseX + col;
            if (!nativeCaretMasked && sameLineAsTarget && screenX >= targetColStart && screenX <= targetColEnd)
            {
                colAlpha[col] = 0;
                continue;
            }

            var left = MathF.Max(fracX, col);
            var right = MathF.Min(fracX + caretWidth, col + 1f);
            var coverage = Math.Clamp(right - left, 0f, 1f);
            if (coverage <= 0.01f)
            {
                colAlpha[col] = 0;
                continue;
            }

            var boosted = MathF.Pow(coverage, 0.78f);
            var a = (int)MathF.Round(255f * opacity * boosted);
            colAlpha[col] = a > 1 ? a : 0;
            if (colAlpha[col] > 0) anyVisible = true;
        }

        if (!anyVisible)
        {
            ClearLayered();
            return;
        }

        var localCaretX = caretBaseX - boxLeft;
        var localCaretY = caretY - boxTop;
        for (var row = 0; row < caretHeight; row++)
        {
            var y = localCaretY + row;
            if (y < 0 || y >= boxHeight) continue;
            var isCap = (row == 0 || row == caretHeight - 1) && caretHeight >= 6;
            var rowOffset = y * MaxBitmapWidth;

            for (var col = 0; col < 4; col++)
            {
                var a = isCap ? (colAlpha[col] * 3) / 4 : colAlpha[col];
                if (a <= 0) continue;
                var x = localCaretX + col;
                if (x < 0 || x >= boxWidth) continue;

                var existing = _pixelScratch[rowOffset + x];
                if (existing != 0)
                {
                    var inv = 255 - a;
                    var outR = (cR * a + bgR * inv + 127) / 255;
                    var outG = (cG * a + bgG * inv + 127) / 255;
                    var outB = (cB * a + bgB * inv + 127) / 255;
                    _pixelScratch[rowOffset + x] = unchecked((int)0xFF000000) | (outR << 16) | (outG << 8) | outB;
                }
                else
                {
                    _pixelScratch[rowOffset + x] = PackPremultipliedArgb(a, cR, cG, cB);
                }
            }
        }

        Marshal.Copy(_pixelScratch, 0, _dibBits, MaxBitmapWidth * boxHeight);

        var dstPt = new Native.POINT { X = boxLeft, Y = boxTop };
        var size = new SIZE { cx = boxWidth, cy = boxHeight };
        var srcPt = new Native.POINT { X = 0, Y = 0 };
        var blend = new BLENDFUNCTION
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA,
        };
        _ = UpdateLayeredWindow(Handle, IntPtr.Zero, ref dstPt, ref size, _memDc, ref srcPt, 0, ref blend, ULW_ALPHA);
        _layeredCleared = false;
    }

    private void ClearLayered()
    {
        if (_layeredCleared || !IsHandleCreated || _dib == IntPtr.Zero || _dibBits == IntPtr.Zero) return;

        Marshal.WriteInt32(_dibBits, 0);
        var dstPt = new Native.POINT { X = -32000, Y = -32000 };
        var size = new SIZE { cx = 1, cy = 1 };
        var srcPt = new Native.POINT { X = 0, Y = 0 };
        var blend = new BLENDFUNCTION
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 0,
            AlphaFormat = AC_SRC_ALPHA,
        };
        _ = UpdateLayeredWindow(Handle, IntPtr.Zero, ref dstPt, ref size, _memDc, ref srcPt, 0, ref blend, ULW_ALPHA);
        _layeredCleared = true;
    }

    private static int PackPremultipliedArgb(int a, int r, int g, int b)
    {
        a = Math.Clamp(a, 0, 255);
        var pr = (r * a + 127) / 255;
        var pg = (g * a + 127) / 255;
        var pb = (b * a + 127) / 255;
        return (a << 24) | (pr << 16) | (pg << 8) | pb;
    }

    private void ReleaseGdiResources()
    {
        if (_memDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero)
        {
            _ = SelectObject(_memDc, _oldBitmap);
            _oldBitmap = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero)
        {
            _ = DeleteObject(_dib);
            _dib = IntPtr.Zero;
            _dibBits = IntPtr.Zero;
        }
        if (_memDc != IntPtr.Zero)
        {
            _ = DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
    }

    protected override void Dispose(bool disposing)
    {
        UninstallHook();
        if (disposing)
        {
            _animTimer.Dispose();
        }
        ReleaseGdiResources();
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref Native.POINT pptDst,
        ref SIZE psize,
        IntPtr hdcSrc,
        ref Native.POINT pptSrc,
        uint crKey,
        ref BLENDFUNCTION pblend,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hdc, int x, int y);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFO pbmi,
        uint usage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);
}
