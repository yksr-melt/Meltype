// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

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

    /// <summary>画面の値を読み直す (ほかのプロセスが config.json を書いた・設定全体を変えた・保存できなかった)。</summary>
    public event Action? Changed;

    /// <summary>保存できなかった (理由)。画面は保存した値 (変える前の値) で出し直す。</summary>
    public event Action<string>? Failed;

    /// <summary>
    /// 1 つの項目 (と、それに合わせて変える項目) を変えて保存する。profile は、変える部品を作ったときのプロファイル。
    /// 保存するまでにほかのプロセス (トレイ) がプロファイルを切り替えていたら、保存しない
    /// (外の書き換えで画面を作り直すと、外される入力欄に LostFocus が来て、前のプロファイルの打ちかけを保存しようとする)。
    /// showAll なら、保存したあとにページを出し直す (変えた部品のほかの値も変わったとき)。
    /// </summary>
    public void Edit(Action<Settings> change, string profile, bool showAll = false) => Replace(settings =>
    {
        if (settings.ActiveProfile != profile) return null;
        var next = settings.Clone();
        change(next);
        return next;
    }, showAll);

    /// <summary>プロファイルの切り替えなど、設定全体を作り直す操作をして保存する。operation が null を返したら何もせず false。</summary>
    public bool Replace(Func<Settings, Settings?> operation) => Replace(operation, showAll: true);

    // showAll: 保存したあとに画面を出し直す (1 項目の変更は、変えた部品がもう新しい値を出している)
    private bool Replace(Func<Settings, Settings?> operation, bool showAll)
    {
        try
        {
            if (_file.Edit(operation) is not { } next) return false;
            _shown = next;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _shown = _file.Current;
            Failed?.Invoke(ex.Message);
            Changed?.Invoke();
            return false;
        }
        if (showAll) Changed?.Invoke();
        return true;
    }
}
