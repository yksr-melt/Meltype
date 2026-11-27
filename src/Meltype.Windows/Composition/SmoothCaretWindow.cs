// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

using System.Diagnostics;
using System.Runtime.InteropServices;
using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// システム全体の入力欄でテキストカーソルが動くとき、ふわっと滑らかに追従するクリックスルー・レイヤードオーバーレイ。
/// 移動先の単色背景マスクと動くサブピクセルカーソルを 1 枚の DIB バッファへ合成して 1 回の UpdateLayeredWindow で
/// アトミック描画し、DwmFlush 垂直同期駆動でチラつき・二重見え・ブレを最小限に抑える。
/// </summary>
internal sealed class SmoothCaretWindow : PopupForm
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint EVENT_OBJECT_TEXTSELECTIONCHANGED = 0x8014;
    private const int IdlePollIntervalMs = 45;
    private const int BurstPollIntervalMs = 12;
    private const int BurstDurationMs = 140;
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
    private readonly Func<Rectangle?> _uiaCaretProvider;
    private readonly SmoothCaretMotion _motion = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = IdlePollIntervalMs };
    private readonly Native.WinEventProc _winEventCallback;
    private readonly Action _onVSyncFrameDelegate;
    private readonly object _vsyncLock = new();
    private readonly Thread _vsyncThread;
    private readonly int[] _pixelScratch = new int[MaxBitmapWidth * MaxBitmapHeight];

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
    private bool _foregroundExcluded;
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
        Size = new Size(4, 18);

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

    /// <summary>変換ボックスが表示されている間はシステム側オーバーレイを隠す。</summary>
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
        if (IsForegroundExcluded()) return;
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

    private bool IsForegroundExcluded()
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return true;
        UpdateForegroundState(foreground);
        return _foregroundExcluded;
    }

    private void UpdateForegroundState(IntPtr foreground)
    {
        if (foreground == _lastForeground) return;
        _lastForeground = foreground;
        _foregroundExcluded = Settings.IsSmoothCaretExcludedApp(GetProcessName(foreground));
        _hasUniformBackground = false;
        _motion.Reset();
        SetVSyncActive(false);
        ClearLayered();
    }

    private static string? GetProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || Native.GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0)
            return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private void PollCaret()
    {
        if (!_enabled || _suppressed || IsDisposed) return;
        if (IsForegroundExcluded()) return;
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
                if (foreground == IntPtr.Zero || Settings.IsSmoothCaretExcludedApp(GetProcessName(foreground)))
                    continue;

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
            EnsureWindowReady();
            InstallHooks();
            _pollTimer.Interval = IdlePollIntervalMs;
            _pollTimer.Start();
            return;
        }

        _pollTimer.Stop();
        UninstallHooks();
        SetVSyncActive(false);
        _motion.Reset();
        ClearLayered();
        if (!_enabled && Visible) Hide();
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
        UpdateForegroundState(foreground);
        if (_foregroundExcluded) return true;

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
        if (foreground != IntPtr.Zero)
            UpdateForegroundState(foreground);
        if (_foregroundExcluded) return;
        if (!Screen.AllScreens.Any(s => s.Bounds.Contains(rect.Location))) return;

        var caretWidth = rect.Width is >= 1 and <= 3 ? rect.Width : Math.Clamp(SystemInformation.CaretWidth, 1, 2);
        var useMask = _hasUniformBackground && foreground == _sampledForeground;
        var wasActive = _motion.IsActive;
        if (_motion.SetTarget(rect.Left, rect.Top, rect.Height, caretWidth, nativeCaretMasked: useMask))
        {
            EnsureWindowReady();
            if (!wasActive)
            {
                _ = SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
                _lastFrameTicks = Stopwatch.GetTimestamp();
                // 動き始めの 1 フレームだけ即時描画し、以降はすべて VSync 等間隔で描画してマイクロスタッターを防ぐ
                RenderLayeredAtomic(nativeCaretMasked: useMask);
            }
            SetVSyncActive(true);
        }
        else if (wasActive)
        {
            SetVSyncActive(false);
            ClearLayered();
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
    /// UI スレッドを止めないようワーカースレッド側で行の上下余白 6 点をサンプルし、
    /// 4 点以上が同じ単色背景ならマスク有効化＋背景色キャッシュと明暗に応じたキャレット色を更新する。
    /// </summary>
    private void SampleBackgroundAndCaretColorOnWorker(IntPtr foreground, Rectangle rect)
    {
        var now = Environment.TickCount64;
        if (foreground == _sampledForeground && now - Interlocked.Read(ref _lastColorSampleMs) < 1500)
            return;
        if (_motion.IsActive && foreground == _sampledForeground)
            return;

        var sameWindow = foreground == _sampledForeground;
        Interlocked.Exchange(ref _lastColorSampleMs, now);
        _sampledForeground = foreground;

        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return;
        try
        {
            // 行の上端・下端マージン上の 6 点をサンプルし、文字インクを踏んでも単色判定がブレないようにする
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

            if (count == 0)
            {
                if (!sameWindow) _hasUniformBackground = false;
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

            if (bestMatches >= 4)
            {
                _backgroundRgb = bestRgb;
                _hasUniformBackground = true;
            }
            else if (!sameWindow || bestMatches <= 2)
            {
                // 同一ウィンドウで一時的に文字が重なった程度 (3/6 一致) ではマスクをパタパタ切らない
                _hasUniformBackground = false;
            }
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
        if (!_enabled || _suppressed || _foregroundExcluded || IsDisposed)
        {
            SetVSyncActive(false);
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
            SetVSyncActive(false);
            ClearLayered();
        }
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
        _memDc = CreateCompatibleDC(IntPtr.Zero);
        var bmi = new BITMAPINFO
        {
            biSize = Marshal.SizeOf<BITMAPINFO>(),
            biWidth = MaxBitmapWidth,
            biHeight = -MaxBitmapHeight, // 上端原点のトップダウン DIB
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };
        _dib = CreateDIBSection(_memDc, ref bmi, 0, out _dibBits, IntPtr.Zero, 0);
        if (_dib != IntPtr.Zero)
            _oldBitmap = SelectObject(_memDc, _dib);
    }

    /// <summary>
    /// 移動先の背景マスクと動くサブピクセルカーソルを単一 DIB バッファへ合成し、
    /// 1 回の UpdateLayeredWindow で完全アトミックに描画する。
    /// </summary>
    private void RenderLayeredAtomic(bool nativeCaretMasked)
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
        var caretBaseX = (int)MathF.Floor(currentX);
        var caretY = (int)MathF.Round(_motion.CurrentY);
        var caretHeight = Math.Clamp((int)MathF.Round(_motion.CurrentHeight), 10, MaxBitmapHeight);
        var caretWidth = Math.Clamp(_motion.CurrentWidth, 1f, 2.5f);
        var caretRightX = caretBaseX + 4;

        var targetX = (int)MathF.Round(_motion.TargetX);
        var targetY = (int)MathF.Round(_motion.TargetY);
        var targetHeight = Math.Clamp((int)MathF.Round(_motion.TargetHeight), 10, MaxBitmapHeight);
        var nativeWidth = Math.Clamp((int)MathF.Round(_motion.TargetWidth), 1, 2);
        // 右方向への入力中は移動先の右側が空き背景なので +1px 余裕を持って元カーソルのはみ出しを完全に隠す
        var movingRight = _motion.TargetX >= _motion.CurrentX;
        var maskWidth = movingRight ? nativeWidth + 1 : nativeWidth;
        var maskRightX = targetX + maskWidth;

        // 1 枚の DIB に収まるバウンディングボックスを計算する (収まらないほど離れていればマスクを省く)
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

        // 1) 単色背景マスクを移動先のネイティブカーソル位置へ敷く
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

        // 2) 動くサブピクセルカーソルを上から合成する (マスク領域と重なる列は背景色と綺麗にブレンド)
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
                    // 下に不透明の背景マスクがある場合は SrcOver で完全不透明に合成する
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
