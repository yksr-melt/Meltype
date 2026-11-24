// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Tests;

/// <summary>テスト用の土台。設定・学習データは一時フォルダーに置き (使っている人のデータに書かない)、プロセスを終えたら消す。</summary>
internal static class TestRuntime
{
    public static Ime.MeltypeRuntime Create(Config.Settings? settings = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meltype-tests-{Guid.NewGuid():N}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
        };
        var runtime = new Ime.MeltypeRuntime(directory);
        if (settings is not null) runtime.Settings.Edit(_ => settings);
        return runtime;
    }
}
