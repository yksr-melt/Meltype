// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition;

/// <summary>
/// STA の COM オブジェクト (Microsoft IME の IFELanguage・WinRT の変換候補) を入力スレッドの外で使うための、メッセージループ付きのスレッド。
/// COM オブジェクトは、作ったこのスレッドからだけ使う。
/// </summary>
internal sealed class StaThread : IDisposable
{
    private readonly Thread _thread;
    private readonly SynchronizationContext _context;

    public StaThread(string name)
    {
        SynchronizationContext? context = null;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            ready.Set();
            Application.Run();
        })
        {
            IsBackground = true,
            Name = name,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        _context = context!;
    }

    public void Post(Action action) => _context.Post(_ => action(), null);

    /// <summary>このスレッドで func を実行して、終わるまで待つ。</summary>
    public T Invoke<T>(Func<T> func)
    {
        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId) return func();
        T result = default!;
        _context.Send(_ => result = func(), null);
        return result;
    }

    public void Dispose()
    {
        _context.Post(_ => Application.ExitThread(), null);
    }
}
