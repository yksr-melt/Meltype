// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition;

/// <summary>
/// 背景の変換の結果が届いた回数 (世代)。Meltype IME の DLL は応答でしか結果を受け取れないので、世代が進むまで返らない要求で待つ。
/// 待ち始める前に届いた結果を取りこぼさないように、DLL が知っている世代を渡す。
/// </summary>
public sealed class ConversionSignal
{
    private readonly object _gate = new();
    private long _generation;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Notify()
    {
        TaskCompletionSource changed;
        lock (_gate)
        {
            _generation++;
            changed = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        changed.TrySetResult();
    }

    /// <summary>世代が since から進むか timeoutMs たつまで待ち、今の世代を返す。</summary>
    public async Task<long> WaitAsync(long since, int timeoutMs, CancellationToken cancel)
    {
        Task changed;
        lock (_gate)
        {
            if (_generation != since) return _generation;
            changed = _changed.Task;
        }
        try
        {
            await changed.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancel).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        lock (_gate) return _generation;
    }
}
