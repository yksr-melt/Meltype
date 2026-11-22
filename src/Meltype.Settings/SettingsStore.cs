// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia.Threading;
using Meltype.Config;

namespace Meltype.SettingsApp;

/// <summary>
/// config.json。画面で値を変えたら、その場で保存する (入力する Meltype は 1 秒ほどで読み直す)。
/// 変えるたびに、そのときの config.json に変えた項目だけを重ねるので、開いている間にトレイ・エディターで変えた項目を戻さない。
/// </summary>
internal sealed class SettingsStore
{
    private readonly SettingsFile _file = new(AppPaths.ConfigFile);
    private readonly DispatcherTimer _watch;
    private Settings _shown;

    public SettingsStore()
    {
        _shown = _file.Current;
        // ほかのプロセス (Meltype のトレイ・エディター) が書いたら、画面を出し直す
        _watch = new DispatcherTimer(TimeSpan.FromMilliseconds(SettingsFile.CheckIntervalMs), DispatcherPriority.Background, (_, _) =>
        {
            if (ReferenceEquals(_file.Current, _shown)) return;
            _shown = _file.Current;
            Changed?.Invoke();
        });
        _watch.Start();
    }

    public Settings Current => _file.Current;

    /// <summary>ほかのプロセスが config.json を書いた (画面の値を読み直す)。</summary>
    public event Action? Changed;

    /// <summary>1 つの項目を変えて保存する。</summary>
    public void Edit(Action<Settings> change)
    {
        var next = _file.Current.Clone();
        change(next);
        _shown = _file.Apply(next);
    }

    /// <summary>プロファイルの切り替えなど、設定全体を作り直す操作をして保存する。operation が null を返したら何もせず false。</summary>
    public bool Replace(Func<Settings, Settings?> operation)
    {
        if (operation(_file.Current) is not { } next) return false;
        _shown = _file.Apply(next);
        Changed?.Invoke();
        return true;
    }
}
