// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

using System.Diagnostics;
using System.Runtime.InteropServices;
using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// システム全体の入力欄でテキストカーソルが動くとき、Word のように滑らかに追従するクリックスルー・レイヤードオーバーレイ。
/// UpdateLayeredWindow によるサブピクセル描画と DwmFlush 垂直同期駆動でちらつき・カクつきをなくし、
/// 単色背景では移動先のネイティブキャレットを一時マスクで隠すことで常に 1 本の美しいカーソルとして見せる。
/// </summary>
internal sealed class SmoothCaretWindow : PopupForm
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint EVENT_OBJECT_TEXTSELECTIONCHANGED = 0x8014;
    private const int IdlePollIntervalMs = 45;
    private const int BurstPollIntervalMs = 12;
    private const int BurstDurationMs = 140;
    private const int StripWidth = 4;
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
    private readonly Func<Rectangle?> _uiaCaretProvider;
    private readonly SmoothCaretMotion _motion = new();
    private readonly CaretMaskWindow _maskWindow = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = IdlePollIntervalMs };
    private readonly Native.WinEventProc _winEventCallback;
    private readonly Action _onVSyncFrameDelegate;
    private readonly object _vsyncLock = new();
    private readonly Thread _vsyncThread;
    private readonly int[] _pixelScratch = new int[StripWidth * MaxBitmapHeight];

    private bool _enabled;
    private bool _suppressed;
    private bool _layeredCleared = true;
    private bool _vsyncActive;
    private bool _vsyncStop;
    private int _frameQueued;
    private long _lastFrameTicks;
    private long _burstUntilMs;
    private long _caretGeneration;
    private IntPtr _lastForeground;
    private IntPtr _caretHook;
    private IntPtr _selectionHook;
    private int _uiaQueryRunning;
    private int _uiaDirty;

    private volatile int _caretRgb = 0x141414;
    private volatile int _backgroundRgb = 0xFFFFFF;
    private volatile bool _hasUniformBackground;
    private IntPtr _sampledForeground;
    private long _lastColorSampleMs;

    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _oldBitmap;
    private IntPtr _dibBits;

    public SmoothCaretWindow(Control invoker, Func<Rectangle?> uiaCaretProvider)
    {
        _invoker = invoker;
        _uiaCaretProvider = uiaCaretProvider;
        _winEventCallback = OnWinEvent;
        _onVSyncFrameDelegate = OnVSyncFrame;

        MinimumSize = new Size(1, 1);
        Size = new Size(StripWidth, 18);

        _pollTimer.Tick += (_, _) => OnPollTick();

        _vsyncThread = new Thread(RunVSyncLoop)
        {
            IsBackground = true,
            Name = "MeltypeSmoothCaretVSync",
        };
        _vsyncThread.Start();
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
        UpdateState();
    }

    /// <summary>変換ボックスが表示されている間はシステム側オーバーレイを隠す (変換ボックス内のスムースカーソルを使うため)。</summary>
    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed) return;
        _suppressed = suppressed;
        UpdateState();
    }

    /// <summary>キー入力・確定・矢印移動の直後に短時間の高頻度ポーリングを行い、アプリのキャレット更新を遅延なく捉える。</summary>
    public void NotifyActivity()
    {
        if (!_enabled || _suppressed || IsDisposed) return;
        _burstUntilMs = Environment.TickCount64 + BurstDurationMs;
        if (_pollTimer.Interval != BurstPollIntervalMs)
            _pollTimer.Interval = BurstPollIntervalMs;
        PollCaret();
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (!_enabled || _suppressed || IsDisposed) return;
        if (eventType == EVENT_OBJECT_LOCATIONCHANGE && idObject != unchecked((int)Native.OBJID_CARET))
            return;
        PollCaret();
    }

    private void OnPollTick()
    {
        if (!_enabled || _suppressed || IsDisposed) return;
        if (_pollTimer.Interval != IdlePollIntervalMs && Environment.TickCount64 >= _burstUntilMs)
            _pollTimer.Interval = IdlePollIntervalMs;
        PollCaret();
    }

    private void PollCaret()
    {
        if (!_enabled || _suppressed || IsDisposed) return;
        if (PollWin32Caret()) return;

        Volatile.Write(ref _uiaDirty, 1);
        if (Interlocked.CompareExchange(ref _uiaQueryRunning, 1, 0) != 0) return;

        ThreadPool.QueueUserWorkItem(_ => RunUiaQueryLoop());
    }

    private void RunUiaQueryLoop()
    {
        try
        {
            while (Interlocked.Exchange(ref _uiaDirty, 0) != 0 && _enabled && !_suppressed && !IsDisposed)
            {
                var generation = Interlocked.Increment(ref _caretGeneration);
                var foreground = Native.GetForegroundWindow();
                var bounds = _uiaCaretProvider();
                if (bounds is { Width: >= 0, Height: > 0 and < 200 } rect && (rect.X != 0 || rect.Y != 0))
                {
                    SampleBackgroundAndCaretColorOnWorker(foreground, rect);
                    if (!IsDisposed && !_invoker.IsDisposed && _invoker.IsHandleCreated)
                    {
                        _invoker.BeginInvoke(() =>
                        {
                            // 古い非同期クエリが後から到着しても新しい座標を巻き戻さない
                            if (_enabled && !_suppressed && !IsDisposed && generation == Interlocked.Read(ref _caretGeneration))
                                ApplyCaretTarget(foreground, rect);
                        });
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _uiaQueryRunning, 0);
        }
    }

    private void UpdateState()
    {
        if (_enabled && !_suppressed)
        {
            EnsureWindowsReady();
            InstallHooks();
            _pollTimer.Interval = IdlePollIntervalMs;
            _pollTimer.Start();
            return;
        }

        _pollTimer.Stop();
        UninstallHooks();
        SetVSyncActive(false);
        _motion.Reset();
        ClearOverlayAndMask();
        if (!_enabled)
        {
            if (Visible) Hide();
            if (_maskWindow.Visible) _maskWindow.Hide();
        }
    }

    private void InstallHooks()
    {
        const uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        if (_caretHook == IntPtr.Zero)
            _caretHook = Native.SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _winEventCallback, 0, 0, flags);
        if (_selectionHook == IntPtr.Zero)
            _selectionHook = Native.SetWinEventHook(EVENT_OBJECT_TEXTSELECTIONCHANGED, EVENT_OBJECT_TEXTSELECTIONCHANGED, IntPtr.Zero, _winEventCallback, 0, 0, flags);
    }

    private void UninstallHooks()
    {
        if (_caretHook != IntPtr.Zero)
        {
            Native.UnhookWinEvent(_caretHook);
            _caretHook = IntPtr.Zero;
        }
        if (_selectionHook != IntPtr.Zero)
        {
            Native.UnhookWinEvent(_selectionHook);
            _selectionHook = IntPtr.Zero;
        }
    }

    private bool PollWin32Caret()
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        var thread = Native.GetWindowThreadProcessId(foreground, out _);
        var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(thread, ref info) || info.hwndCaret == IntPtr.Zero)
            return false;

        var top = new Native.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
        if (!Native.ClientToScreen(info.hwndCaret, ref top) || (top.X == 0 && top.Y == 0))
            return false;

        Interlocked.Increment(ref _caretGeneration);
        var width = Math.Clamp(info.rcCaret.Right - info.rcCaret.Left, 1, 3);
        var height = Math.Max(10, info.rcCaret.Bottom - info.rcCaret.Top);
        var rect = new Rectangle(top.X, top.Y, width, height);
        ScheduleWorkerColorSample(foreground, rect);
        ApplyCaretTarget(foreground, rect);
        return true;
    }

    private void ApplyCaretTarget(IntPtr foreground, Rectangle rect)
    {
        if (foreground != IntPtr.Zero && foreground != _lastForeground)
        {
            _lastForeground = foreground;
            _hasUniformBackground = false;
            _motion.Reset();
            ClearOverlayAndMask();
        }

        if (!Screen.AllScreens.Any(s => s.Bounds.Contains(rect.Location))) return;

        var caretWidth = rect.Width is >= 1 and <= 3 ? rect.Width : Math.Clamp(SystemInformation.CaretWidth, 1, 2);
        var useMask = _hasUniformBackground && foreground == _sampledForeground;
        var wasActive = _motion.IsActive;
        if (_motion.SetTarget(rect.Left, rect.Top, rect.Height, caretWidth, nativeCaretMasked: useMask))
        {
            EnsureWindowsReady();
            if (useMask)
            {
                // 移動先のネイティブキャレット幅だけ背景色で一時的に覆い、その手前に動くカーソルを重ねる
                var maskWidth = Math.Clamp(caretWidth, 1, 2);
                _maskWindow.ShowMask(rect.Left, rect.Top, maskWidth, Math.Clamp(rect.Height, 10, MaxBitmapHeight), _backgroundRgb);
                _ = SetWindowPos(_maskWindow.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
            }
            else
            {
                _maskWindow.ClearMask();
            }

            // オーバーレイカーソルは常にマスクより手前に置く
            if (!wasActive || useMask)
            {
                _ = SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
            }

            if (!wasActive)
                _lastFrameTicks = Stopwatch.GetTimestamp();

            RenderLayered(nativeCaretMasked: useMask);
            SetVSyncActive(true);
        }
        else if (wasActive)
        {
            SetVSyncActive(false);
            ClearOverlayAndMask();
        }
    }

    private void ScheduleWorkerColorSample(IntPtr foreground, Rectangle rect)
    {
        var now = Environment.TickCount64;
        if (foreground == _sampledForeground && now - Interlocked.Read(ref _lastColorSampleMs) < 1500)
            return;
        Interlocked.Exchange(ref _lastColorSampleMs, now);
        ThreadPool.QueueUserWorkItem(_ => SampleBackgroundAndCaretColorOnWorker(foreground, rect));
    }

    /// <summary>
    /// UI スレッドを止めないようワーカースレッド側でキャレット周囲の 4 点をサンプルし、
    /// 3 点以上が同じ単色背景ならマスク有効化＋背景色キャッシュと明暗に応じたキャレット色を更新する。
    /// </summary>
    private void SampleBackgroundAndCaretColorOnWorker(IntPtr foreground, Rectangle rect)
    {
        var now = Environment.TickCount64;
        if (foreground == _sampledForeground && now - Interlocked.Read(ref _lastColorSampleMs) < 1500)
            return;
        if (_motion.IsActive && foreground == _sampledForeground)
            return;

        Interlocked.Exchange(ref _lastColorSampleMs, now);
        _sampledForeground = foreground;

        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return;
        try
        {
            // 行の上下余白とキャレット右側の 4 点をサンプルし、文字インクや画像背景を誤判定しないようにする
            Span<int> samples = stackalloc int[4];
            var count = 0;
            ReadSample(hdc, rect.Left + 5, rect.Top + 1, samples, ref count);
            ReadSample(hdc, rect.Left + 5, rect.Bottom - 2, samples, ref count);
            ReadSample(hdc, rect.Left + 10, rect.Top + rect.Height / 2, samples, ref count);
            ReadSample(hdc, rect.Left - 5, rect.Top + 1, samples, ref count);

            if (count == 0)
            {
                _hasUniformBackground = false;
                return;
            }

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
            _caretRgb = luminance >= 128 ? 0x141414 : 0xF2F2F2;
            _backgroundRgb = bestRgb;
            _hasUniformBackground = bestMatches >= 3;
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

    private void SetVSyncActive(bool active)
    {
        lock (_vsyncLock)
        {
            if (_vsyncActive == active) return;
            _vsyncActive = active;
            if (active) Monitor.PulseAll(_vsyncLock);
        }
    }

    private void RunVSyncLoop()
    {
        while (true)
        {
            lock (_vsyncLock)
            {
                while (!_vsyncActive && !_vsyncStop)
                    Monitor.Wait(_vsyncLock);
                if (_vsyncStop) return;
            }

            // モニター本来のリフレッシュレート (60Hz / 120Hz / 144Hz / 240Hz) に完全同期して起床する
            if (DwmFlush() != 0)
                Thread.Sleep(8);

            if (!_vsyncActive || _vsyncStop) continue;
            if (Interlocked.CompareExchange(ref _frameQueued, 1, 0) == 0)
            {
                try
                {
                    if (!IsDisposed && !_invoker.IsDisposed && _invoker.IsHandleCreated)
                        _invoker.BeginInvoke(_onVSyncFrameDelegate);
                    else
                        Interlocked.Exchange(ref _frameQueued, 0);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                {
                    Interlocked.Exchange(ref _frameQueued, 0);
                }
            }
        }
    }

    private void OnVSyncFrame()
    {
        Interlocked.Exchange(ref _frameQueued, 0);
        if (!_enabled || _suppressed || IsDisposed)
        {
            SetVSyncActive(false);
            ClearOverlayAndMask();
            return;
        }

        var nowTicks = Stopwatch.GetTimestamp();
        var dtMs = Math.Clamp((nowTicks - _lastFrameTicks) * 1000f / Stopwatch.Frequency, 0.5f, 50f);
        _lastFrameTicks = nowTicks;

        var masked = _maskWindow.IsMaskVisible;
        if (_motion.Step(dtMs, nativeCaretMasked: masked))
        {
            RenderLayered(nativeCaretMasked: masked);
        }
        else
        {
            SetVSyncActive(false);
            ClearOverlayAndMask();
        }
    }

    private void EnsureWindowsReady()
    {
        EnsureGdiBuffer();
        _maskWindow.EnsureReady();
        if (!Visible)
        {
            ClearLayered();
            ShowOnTop();
        }
    }

    private void EnsureGdiBuffer()
    {
        if (_dib != IntPtr.Zero) return;
        _memDc = CreateCompatibleDC(IntPtr.Zero);
        var bmi = new BITMAPINFO
        {
            biSize = Marshal.SizeOf<BITMAPINFO>(),
            biWidth = StripWidth,
            biHeight = -MaxBitmapHeight, // 上端原点のトップダウン DIB
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };
        _dib = CreateDIBSection(_memDc, ref bmi, 0, out _dibBits, IntPtr.Zero, 0);
        if (_dib != IntPtr.Zero)
            _oldBitmap = SelectObject(_memDc, _dib);
    }

    private void RenderLayered(bool nativeCaretMasked)
    {
        EnsureGdiBuffer();
        if (_dib == IntPtr.Zero || _dibBits == IntPtr.Zero || !IsHandleCreated) return;

        var opacity = Math.Clamp(_motion.Opacity, 0f, 1f);
        if (opacity <= 0.01f)
        {
            ClearLayered();
            return;
        }

        var currentX = _motion.CurrentX;
        var baseX = (int)MathF.Floor(currentX);
        var baseY = (int)MathF.Round(_motion.CurrentY);
        var height = Math.Clamp((int)MathF.Round(_motion.CurrentHeight), 10, MaxBitmapHeight);
        var width = Math.Clamp(_motion.CurrentWidth, 1f, 2.5f);
        var fracX = currentX - baseX;

        var sameLineAsTarget = MathF.Abs(_motion.TargetY - _motion.CurrentY) < 4f;
        var targetColStart = (int)MathF.Round(_motion.TargetX);
        var targetColEnd = targetColStart + Math.Max(1, (int)MathF.Round(_motion.TargetWidth)) - 1;

        var rgb = _caretRgb;
        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;

        Span<int> rowPixels = stackalloc int[StripWidth];
        Span<int> capPixels = stackalloc int[StripWidth];
        var anyVisible = false;

        for (var col = 0; col < StripWidth; col++)
        {
            var screenX = baseX + col;
            // 単色背景マスクが出ていない場合のみ、ネイティブキャレット列への重ね塗りを防いでぼやけを抑える
            if (!nativeCaretMasked && sameLineAsTarget && screenX >= targetColStart && screenX <= targetColEnd)
            {
                rowPixels[col] = 0;
                capPixels[col] = 0;
                continue;
            }

            var left = MathF.Max(fracX, col);
            var right = MathF.Min(fracX + width, col + 1f);
            var coverage = Math.Clamp(right - left, 0f, 1f);
            if (coverage <= 0.01f)
            {
                rowPixels[col] = 0;
                capPixels[col] = 0;
                continue;
            }

            // サブピクセル補間中でも細線のコントラストが薄まってぼやけないようガンマ補正をかける
            var boosted = MathF.Pow(coverage, 0.78f);
            var a = (int)MathF.Round(255f * opacity * boosted);
            if (a <= 1)
            {
                rowPixels[col] = 0;
                capPixels[col] = 0;
                continue;
            }

            anyVisible = true;
            rowPixels[col] = PackPremultipliedArgb(a, r, g, b);
            capPixels[col] = PackPremultipliedArgb((a * 3) / 4, r, g, b);
        }

        if (!anyVisible)
        {
            ClearLayered();
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var src = (y == 0 || y == height - 1) && height >= 6 ? capPixels : rowPixels;
            var offset = y * StripWidth;
            _pixelScratch[offset] = src[0];
            _pixelScratch[offset + 1] = src[1];
            _pixelScratch[offset + 2] = src[2];
            _pixelScratch[offset + 3] = src[3];
        }

        Marshal.Copy(_pixelScratch, 0, _dibBits, StripWidth * height);

        var dstPt = new Native.POINT { X = baseX, Y = baseY };
        var size = new SIZE { cx = StripWidth, cy = height };
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

    private void ClearOverlayAndMask()
    {
        _maskWindow.ClearMask();
        ClearLayered();
    }

    private void ClearLayered()
    {
        if (_layeredCleared || !IsHandleCreated) return;
        EnsureGdiBuffer();
        if (_dib == IntPtr.Zero || _dibBits == IntPtr.Zero) return;

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
        lock (_vsyncLock)
        {
            _vsyncActive = false;
            _vsyncStop = true;
            Monitor.PulseAll(_vsyncLock);
        }
        UninstallHooks();
        if (disposing)
        {
            _pollTimer.Dispose();
            _maskWindow.Dispose();
        }
        ReleaseGdiResources();
        base.Dispose(disposing);
    }

    /// <summary>
    /// 移動先のネイティブキャレットを単色背景で一時的に覆い、アニメーション中の二重表示を防ぐ極小レイヤードウィンドウ。
    /// </summary>
    private sealed class CaretMaskWindow : PopupForm
    {
        private readonly int[] _pixels = new int[StripWidth * MaxBitmapHeight];
        private IntPtr _memDc;
        private IntPtr _dib;
        private IntPtr _oldBitmap;
        private IntPtr _dibBits;
        private bool _cleared = true;

        public CaretMaskWindow()
        {
            MinimumSize = new Size(1, 1);
            Size = new Size(StripWidth, 18);
        }

        public bool IsMaskVisible => !_cleared;

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

        public void EnsureReady()
        {
            EnsureGdiBuffer();
            if (!Visible)
            {
                ClearMask();
                ShowOnTop();
            }
        }

        public void ShowMask(int x, int y, int width, int height, int backgroundRgb)
        {
            EnsureReady();
            if (_dib == IntPtr.Zero || _dibBits == IntPtr.Zero || !IsHandleCreated) return;

            width = Math.Clamp(width, 1, StripWidth);
            height = Math.Clamp(height, 10, MaxBitmapHeight);
            var opaqueBg = unchecked((int)0xFF000000) | (backgroundRgb & 0x00FFFFFF);

            for (var row = 0; row < height; row++)
            {
                var offset = row * StripWidth;
                for (var col = 0; col < StripWidth; col++)
                    _pixels[offset + col] = col < width ? opaqueBg : 0;
            }

            Marshal.Copy(_pixels, 0, _dibBits, StripWidth * height);

            var dstPt = new Native.POINT { X = x, Y = y };
            var size = new SIZE { cx = width, cy = height };
            var srcPt = new Native.POINT { X = 0, Y = 0 };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA,
            };
            _ = UpdateLayeredWindow(Handle, IntPtr.Zero, ref dstPt, ref size, _memDc, ref srcPt, 0, ref blend, ULW_ALPHA);
            _cleared = false;
        }

        public void ClearMask()
        {
            if (_cleared || !IsHandleCreated) return;
            EnsureGdiBuffer();
            if (_dib == IntPtr.Zero || _dibBits == IntPtr.Zero) return;

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
            _cleared = true;
        }

        private void EnsureGdiBuffer()
        {
            if (_dib != IntPtr.Zero) return;
            _memDc = CreateCompatibleDC(IntPtr.Zero);
            var bmi = new BITMAPINFO
            {
                biSize = Marshal.SizeOf<BITMAPINFO>(),
                biWidth = StripWidth,
                biHeight = -MaxBitmapHeight,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            _dib = CreateDIBSection(_memDc, ref bmi, 0, out _dibBits, IntPtr.Zero, 0);
            if (_dib != IntPtr.Zero)
                _oldBitmap = SelectObject(_memDc, _dib);
        }

        protected override void Dispose(bool disposing)
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
            base.Dispose(disposing);
        }
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
