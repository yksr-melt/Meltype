// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;

namespace Meltype.Composition;

/// <summary>マウスボタンの押下/解放 (変換中にクリックされたとき、確定してからクリックを再生するため)。</summary>
public readonly record struct MouseButtonEvent(int Message, int X, int Y, uint MouseData);

/// <summary>フックから変換ボックスへ渡す入力。キー、マウスボタン、または入力モード切り替え要求。</summary>
public readonly record struct CapturedInput(KeyEvent? Key, MouseButtonEvent? Mouse, nint? ToggleTarget = null);

/// <summary>
/// フック側の関所。変換ボックスが開いている間 (captured) は、届いたキーとマウスボタンを
/// すべて握りつぶして順番どおりキューに積み、UI スレッドの CompositionController に処理させる。
/// こうすることで、確定前の文字を後続の打鍵やクリックが追い越すことがない。
///
/// フックのスレッドから呼ばれるので、ロック内ではキューへの出し入れしかしない。
/// OnKey と OnMouseButton は 1 つのスレッド (フックのスレッド) から呼ぶ。
/// </summary>
public sealed class CaptureGate
{
    private readonly object _gate = new();
    private readonly Queue<CapturedInput> _queue = new();
    private readonly Action _signal;
    private bool _captured;

    /// <param name="signal">キューに積んだことを UI スレッドへ知らせる (BeginInvoke など、ブロックしないもの)。</param>
    public CaptureGate(Action signal) => _signal = signal;

    public bool IsCaptured { get { lock (_gate) return _captured; } }

    /// <summary>true ならフックでその打鍵を握りつぶす。</summary>
    public bool OnKey(KeyEvent e, Func<KeyEvent, bool> startsComposition)
    {
        bool captured;
        lock (_gate)
        {
            captured = _captured;
            if (captured) _queue.Enqueue(new CapturedInput(e, null));
        }
        if (!captured)
        {
            // 打ち始めの判定はフォーカスを調べ終わるのを待つことがあるので、ロックの外で行う。
            // 関所を閉める (_captured を true にする) のも、キューに積むのもフックのスレッドだけで、関所が開いている間はキューは空。
            // 判定の間に UI スレッドがしうるのは、開いた関所を開いたままにすること (TryRelease・Abort) だけなので、判定の後に閉めても順番は崩れない。
            if (!startsComposition(e)) return false;
            lock (_gate)
            {
                _captured = true;
                _queue.Enqueue(new CapturedInput(e, null));
            }
        }
        _signal();
        return true;
    }

    public bool OnMouseButton(MouseButtonEvent e)
    {
        lock (_gate)
        {
            if (!_captured) return false;
            _queue.Enqueue(new CapturedInput(null, e));
        }
        _signal();
        return true;
    }

    /// <summary>登録キーの切り替え要求を、前後の入力と同じ順序で処理する。</summary>
    public void OnToggleMode(nint target)
    {
        lock (_gate)
        {
            _captured = true;
            _queue.Enqueue(new CapturedInput(null, null, target));
        }
        _signal();
    }

    public bool TryDequeue(out CapturedInput input)
    {
        lock (_gate) return _queue.TryDequeue(out input);
    }

    /// <summary>変換ボックスが空になったら呼ぶ。キューも空なら関所を開けて true を返す。</summary>
    public bool TryRelease()
    {
        lock (_gate)
        {
            if (_queue.Count > 0) return false;
            _captured = false;
            return true;
        }
    }

    /// <summary>無効化・終了時。切り替え要求を破棄し、残っているキー・マウス入力を返す (呼び出し側で再生する)。</summary>
    public List<CapturedInput> Abort()
    {
        lock (_gate)
        {
            var rest = _queue.Where(input => input.ToggleTarget is null).ToList();
            _queue.Clear();
            _captured = false;
            return rest;
        }
    }
}
