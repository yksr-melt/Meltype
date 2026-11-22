// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Net.Sockets;
using System.Text;
using Meltype.Config;

namespace Meltype.SettingsApp;

/// <summary>
/// 設定アプリを 1 つだけ動かす。2 つ目に起動したら、起動の引数を 1 つ目に渡して終わる (1 つ目が窓を前に出し、引数のページを出す)。
/// 受け渡しは、データフォルダーの Unix ドメインソケット (Windows 10 1803 以降も使える)。データフォルダーごとに 1 つ (ポータブル版は別々に動く)。
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string SocketPath = Path.Combine(AppPaths.DataDirectory, "settings.sock");
    private readonly Socket _listener;

    private SingleInstance(Socket listener) => _listener = listener;

    /// <summary>ほかの設定アプリが動いていれば args を渡して true。</summary>
    public static bool SendToRunning(string[] args)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
        }
        catch (SocketException)
        {
            // 動いていない (前に落ちた設定アプリのソケットのファイルが残っていても、つながらない)
            return false;
        }
        // 引数は改行を含まない (語は Windows のトレイが 1 行のものだけを渡す) ので、改行で区切る
        socket.Send(Encoding.UTF8.GetBytes(string.Join('\n', args)));
        return true;
    }

    /// <summary>2 つ目の設定アプリからの引数を待ち、受け取るたびに received を (裏のスレッドから) 呼ぶ。待てなければ null (1 つだけにはできないが、動かす)。</summary>
    public static SingleInstance? Listen(Action<string[]> received)
    {
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            // つながらなかったので、残っているファイルは前に落ちた設定アプリのもの
            File.Delete(SocketPath);
            listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
            listener.Listen();
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log.Warn($"設定アプリの多重起動を防げません ({SocketPath}): {ex.Message}");
            listener.Dispose();
            return null;
        }
        var instance = new SingleInstance(listener);
        new Thread(() => instance.Accept(received)) { IsBackground = true, Name = "設定アプリの多重起動" }.Start();
        return instance;
    }

    private void Accept(Action<string[]> received)
    {
        while (true)
        {
            Socket client;
            try
            {
                client = _listener.Accept();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                return;
            }
            using (client)
            using (var stream = new NetworkStream(client))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var text = reader.ReadToEnd();
                received(text.Length == 0 ? [] : text.Split('\n'));
            }
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        try { File.Delete(SocketPath); }
        catch (IOException) { }
    }
}
