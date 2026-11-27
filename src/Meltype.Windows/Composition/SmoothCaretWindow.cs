// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

using System.Diagnostics;
using System.Runtime.InteropServices;
using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// システム全体の入力欄でテキストカーソルが動くとき、Word のようにふわっと滑らかに追従する
/// クリックスルー・最前面のオーバーレイカーソル。動きが止まると元のカーソルへ自然に溶け込んで非表示になる。
/// </summary>
internal sealed class SmoothCaretWindow : PopupForm
{
    private const int PollIntervalMs = 30;
    private const int FrameIntervalMs = 16;

    private readonly Func<Rectangle?> _uiaCaretProvider;
    private readonly SmoothCaretMotion _motion = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = PollIntervalMs };
    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = FrameIntervalMs };
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private bool _enabled;
    private bool _suppressed;
    private long _lastFrameMs;
    private IntPtr _lastForeground;
    private int _uiaQueryRunning;

    public SmoothCaretWindow(Control invoker, Func<Rectangle?> uiaCaretProvider)
    {
        _invoker = invoker;
        _uiaCaretProvider = uiaCaretProvider;
        MinimumSize = new Size(1, 1);
        BackColor = Color.FromArgb(24, 24, 24);
        Size = new Size(2, 18);
        Opacity = 0.0;

        _pollTimer.Tick += (_, _) => PollCaret();
        _animTimer.Tick += (_, _) => OnAnimationTick();
    }

    private readonly Control _invoker;

    protected override bool ClickThrough => true;

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        UpdateTimers();
    }

    /// <summary>変換ボックスが表示されている間はシステム側オーバーレイを隠す (変換ボックス内のスムースカーソルを使うため)。</summary>
    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed) return;
        _suppressed = suppressed;
        UpdateTimers();
    }

    /// <summary>キー入力・確定・矢印移動の直後にキャレット位置を即座に追従する (TSF / UI Automation 対応)。</summary>
    public void NotifyActivity(Point? anchor = null)
    {
        if (!_enabled || _suppressed || IsDisposed) return;
        if (anchor is { X: var ax, Y: var ay } && (ax != 0 || ay != 0))
        {
            ApplyCaretTarget(new Rectangle(ax, ay, 2, 20));
            return;
        }
        PollCaret();
    }

    private void PollCaret()
    {
        if (!_enabled || _suppressed || IsDisposed) return;
        if (PollWin32Caret()) return;
        if (Interlocked.CompareExchange(ref _uiaQueryRunning, 1, 0) != 0) return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var bounds = _uiaCaretProvider();
                if (bounds is { Width: >= 0, Height: > 0 and < 200 } rect && (rect.X != 0 || rect.Y != 0) &&
                    !IsDisposed && !_invoker.IsDisposed && _invoker.IsHandleCreated)
                {
                    _invoker.BeginInvoke(() =>
                    {
                        if (_enabled && !_suppressed && !IsDisposed)
                            ApplyCaretTarget(rect);
                    });
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _uiaQueryRunning, 0);
            }
        });
    }

    private void UpdateTimers()
    {
        if (_enabled && !_suppressed)
        {
            _pollTimer.Start();
            return;
        }
        _pollTimer.Stop();
        _animTimer.Stop();
        _motion.Reset();
        if (Visible) Hide();
    }

    private bool PollWin32Caret()
    {
        if (!_enabled || _suppressed || IsDisposed) return false;
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        if (foreground != _lastForeground)
        {
            _lastForeground = foreground;
            _motion.Reset();
            if (Visible) Hide();
        }
        var thread = Native.GetWindowThreadProcessId(foreground, out _);
        var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(thread, ref info) || info.hwndCaret == IntPtr.Zero)
            return false;

        var top = new Native.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
        if (!Native.ClientToScreen(info.hwndCaret, ref top) || (top.X == 0 && top.Y == 0))
            return false;

        var height = Math.Max(10, info.rcCaret.Bottom - info.rcCaret.Top);
        ApplyCaretTarget(new Rectangle(top.X, top.Y, 3, height));
        return true;
    }

    private void ApplyCaretTarget(Rectangle rect)
    {
        if (!Screen.AllScreens.Any(s => s.Bounds.Contains(rect.Location))) return;
        UpdateCaretColor(rect.Left + 4, rect.Top + rect.Height / 2);
        if (_motion.SetTarget(rect.Left, rect.Top, rect.Height))
        {
            if (!_animTimer.Enabled)
            {
                _lastFrameMs = _clock.ElapsedMilliseconds;
                _animTimer.Start();
            }
            RenderMotion();
        }
    }

    /// <summary>カーソル付近の画面色を見て、明るい背景なら黒・暗い背景なら白のカーソル色にする。</summary>
    private void UpdateCaretColor(int sampleX, int sampleY)
    {
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return;
        try
        {
            var pixel = GetPixel(hdc, sampleX, sampleY);
            if (pixel == uint.MaxValue) return;
            var r = (int)(pixel & 0xFF);
            var g = (int)((pixel >> 8) & 0xFF);
            var b = (int)((pixel >> 16) & 0xFF);
            var luminance = (r * 299 + g * 587 + b * 114) / 1000;
            var color = luminance >= 128 ? Color.FromArgb(24, 24, 24) : Color.FromArgb(245, 245, 245);
            if (BackColor != color) BackColor = color;
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hdc, int x, int y);

    private void OnAnimationTick()
    {
        if (!_enabled || _suppressed || IsDisposed)
        {
            _animTimer.Stop();
            if (Visible) Hide();
            return;
        }
        var now = _clock.ElapsedMilliseconds;
        var dt = Math.Clamp(now - _lastFrameMs, 1, 64);
        _lastFrameMs = now;

        if (_motion.Step(dt))
        {
            RenderMotion();
        }
        else
        {
            _animTimer.Stop();
            if (Visible) Hide();
        }
    }

    private void RenderMotion()
    {
        var x = (int)MathF.Round(_motion.CurrentX);
        var y = (int)MathF.Round(_motion.CurrentY);
        var h = Math.Max(10, (int)MathF.Round(_motion.CurrentHeight));
        SetBounds(x, y, 2, h);
        Opacity = Math.Clamp(_motion.Opacity, 0.0, 1.0);
        if (!Visible && _motion.Opacity > 0.01f) ShowOnTop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Dispose();
            _animTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
