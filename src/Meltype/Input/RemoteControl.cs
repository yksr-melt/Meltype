// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;

namespace Meltype.Input;

/// <summary>
/// 遠隔操作 (Chrome リモート デスクトップ・Parsec) で、この PC を操作されているか (issue #372)。
/// 遠隔操作のキーは、ほかのソフトが送ったキー (LLKHF_INJECTED) として届くので、既定ではマクロ・自動入力と同じく変換せずに通していた。
/// 接続中だけ、手で打ったキーとして扱う (設定「遠隔操作などの入力も処理する」を ON にしなくても使えるように)。
/// プロセスの一覧を見るのは重いので、フックの中ではなく、裏で 5 秒ごとに調べた結果を使う。
/// </summary>
internal sealed class RemoteControl : IDisposable
{
    /// <summary>
    /// 遠隔操作のプロセス。Chrome リモート デスクトップは、接続されると remoting_desktop.exe を起動する
    /// (常駐する remoting_host.exe は待ち受けなので含めない)。Parsec は接続中かどうかをプロセス名では区別できないので、
    /// parsecd.exe が動いている間は処理する。
    /// </summary>
    internal static readonly string[] SessionProcesses = ["remoting_desktop", "parsecd"];

    private readonly System.Threading.Timer _timer;
    private volatile string? _active;

    public RemoteControl()
    {
        _timer = new System.Threading.Timer(_ => Refresh(), null, 0, 5000);
    }

    /// <summary>遠隔操作の接続中なら、そのプロセス名 (無ければ null)。</summary>
    public string? Active => _active;

    private void Refresh()
    {
        try
        {
            string? found = null;
            foreach (var name in SessionProcesses)
            {
                var processes = Process.GetProcessesByName(name);
                foreach (var process in processes) process.Dispose();
                if (processes.Length == 0) continue;
                found = name;
                break;
            }
            if (found != _active) Diagnostics.Log.Info(found is null ? "遠隔操作の接続が終わりました。" : $"遠隔操作の接続中 ({found}.exe): 遠隔操作のキーも、手で打ったキーと同じように処理します。");
            _active = found;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"遠隔操作の接続を確かめられませんでした: {ex.Message}");
        }
    }

    public void Dispose() => _timer.Dispose();
}
