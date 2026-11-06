// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition;

/// <summary>
/// 1 つのスレッドで、処理中の 1 件と最後に頼まれた 1 件だけを処理する。古い予約は処理せずに捨てる。
/// 結果は process が変換エンジンのキャッシュに入れる (入力スレッドは次の問い合わせでキャッシュから読む)。
/// </summary>
/// <param name="process">1 件を処理する。ワーカーのスレッドで呼ぶ。</param>
/// <param name="settleMs">打鍵が続く間の要求をまとめるために、処理を始める前に待つ時間。Wait で待たれている要求は待たない。</param>
/// <param name="start">ワーカーの処理を動かす先。null なら thread pool。</param>
internal sealed class LatestRequestWorker<TRequest>(Action<TRequest> process, int settleMs = 0, Action<Action>? start = null) : IDisposable
    where TRequest : struct, IEquatable<TRequest>
{
    private readonly object _gate = new();
    private TRequest? _pending;
    private TRequest? _active;
    private bool _running;
    private bool _urgent;
    private bool _disposed;

    /// <summary>request を最後の要求にする。処理中の要求と同じなら、古い予約を捨てるだけ。</summary>
    public void Submit(TRequest request)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending = Same(_active, request) ? null : request;
            if (_running || _pending is null) return;
            _running = true;
            if (start is not null) start(Run);
            else _ = Task.Run(Run);
        }
    }

    /// <summary>処理中も予約も無いときだけ、request を頼む。答えを待っている要求の予約を上書きしない。</summary>
    public void SubmitIfIdle(TRequest request)
    {
        lock (_gate)
        {
            if (!_running) Submit(request);
        }
    }

    /// <summary>request の処理が終わるまで最大 timeoutMs 待つ。終わった (または処理の予定が無い) なら true。</summary>
    public bool Wait(TRequest request, int timeoutMs)
    {
        lock (_gate)
        {
            if (!IsQueued(request)) return true;
            if (timeoutMs <= 0) return false;
            _urgent = true;
            Monitor.PulseAll(_gate);
            var until = Environment.TickCount64 + timeoutMs;
            while (IsQueued(request))
            {
                var left = until - Environment.TickCount64;
                if (left <= 0 || !Monitor.Wait(_gate, (int)left)) return !IsQueued(request);
            }
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending = null;
            Monitor.PulseAll(_gate);
        }
    }

    private bool IsQueued(TRequest request) => !_disposed && (Same(_active, request) || Same(_pending, request));

    private static bool Same(TRequest? a, TRequest b) => a is { } value && value.Equals(b);

    private void Run()
    {
        while (true)
        {
            TRequest request;
            lock (_gate)
            {
                if (settleMs > 0 && !_urgent && !_disposed) Monitor.Wait(_gate, settleMs);
                _urgent = false;
                if (_disposed || _pending is not { } pending)
                {
                    _running = false;
                    return;
                }
                request = pending;
                _pending = null;
                _active = request;
            }
            try
            {
                process(request);
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error($"背景の変換に失敗しました: {ex}");
            }
            lock (_gate)
            {
                _active = null;
                Monitor.PulseAll(_gate);
            }
        }
    }
}
