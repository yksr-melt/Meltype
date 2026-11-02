// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 haru0416-dev

namespace Meltype.Composition;

/// <summary>
/// 学習データの保存を、変わってから少し後にまとめて行う (確定のたびにファイルを丸ごと書き直さない)。
/// アプリを起動するときに Deferred を true にする。false (既定、テスト) ならすぐに保存する。
/// </summary>
public static class LearningSaves
{
    public static bool Deferred { get; set; }

    /// <summary>変わってから保存するまでの最長の時間。</summary>
    public static TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>まだ保存していない学習データを、すぐに保存する (終了するときにも呼ばれる)。</summary>
    public static void FlushAll() => DeferredSaver.FlushAll();
}

internal sealed class DeferredSaver
{
    private static readonly List<WeakReference<DeferredSaver>> All = [];
    private readonly Func<bool> _write;
    private readonly object _gate = new();
    private readonly object _writing = new();
    private Timer? _timer;
    private bool _pending;

    static DeferredSaver() => AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushAll();

    public DeferredSaver(Func<bool> write)
    {
        _write = write;
        lock (All)
        {
            All.RemoveAll(saver => !saver.TryGetTarget(out _));
            All.Add(new WeakReference<DeferredSaver>(this));
        }
    }

    public void Request()
    {
        if (!LearningSaves.Deferred)
        {
            _write();
            return;
        }
        lock (_gate)
        {
            _pending = true;
            _timer ??= new Timer(_ => Flush(), null, LearningSaves.Delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Flush() => Write(onlyPending: true);

    /// <summary>すぐに書く (リセット・削除)。</summary>
    public void WriteNow() => Write(onlyPending: false);

    private void Write(bool onlyPending)
    {
        // タイマーが書いている途中なら、書き終わるまで待つ (終了・バックアップの前に呼ぶので)
        lock (_writing)
        {
            lock (_gate)
            {
                if (onlyPending && !_pending) return;
                _pending = false;
                _timer?.Dispose();
                _timer = null;
            }
            // 書けなかったら、次に変わったとき・終了のときにもう一度書く
            if (!_write()) lock (_gate) _pending = true;
        }
    }

    public static void FlushAll()
    {
        List<DeferredSaver> savers;
        lock (All) savers = All.Select(saver => saver.TryGetTarget(out var target) ? target : null).OfType<DeferredSaver>().ToList();
        foreach (var saver in savers) saver.Flush();
    }
}
