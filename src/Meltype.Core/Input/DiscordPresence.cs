// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Meltype.Config;

namespace Meltype.Input;

/// <summary>
/// Discord のステータス (Rich Presence) に打鍵数を出す (打鍵数の「Discord に表示する」。既定は OFF)。
/// Discord のローカル IPC (Windows は名前付きパイプ discord-ipc-N、macOS・Linux は Unix ソケット) で送り、外のサーバーは使わない。
/// 送るのは今日と合計の打鍵数・直近の 1 分あたりの打鍵数・使い始めた時刻だけ (打った文字・変換候補・入力先のアプリ名は送らない)。
/// 入力とは別のスレッドで UpdateInterval ごとに送り、Discord が起動していない・切れたときは次の回につなぎ直す (入力には影響しない)。
/// </summary>
internal sealed class DiscordPresence : IDisposable
{
    /// <summary>Discord の開発者ポータルで作ったアプリの ID (Application ID)。空なら何も送らない。</summary>
    public const string ClientId = "";

    public static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(5);

    /// <summary>出す打鍵数。</summary>
    public readonly record struct Stats(long Today, long Total);

    private readonly Func<bool> _enabled;
    private readonly Func<Stats> _stats;
    private readonly string _clientId;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly DateTimeOffset _started = DateTimeOffset.Now;
    private Stream? _stream;
    private string? _sent;
    private (long Total, DateTimeOffset At)? _last;

    public DiscordPresence(Func<bool> enabled, Func<Stats> stats, string clientId = ClientId)
    {
        _enabled = enabled;
        _stats = stats;
        _clientId = clientId;
        _loop = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await TickAsync(_stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Discord が起動していない・閉じた・応答しないときは、次の回につなぎ直す
                if (_stream is not null) Diagnostics.Log.Info($"Discord との接続が切れました: {ex.Message}");
                Disconnect();
            }
            try { await Task.Delay(UpdateInterval, _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
        await ClearAsync();
        Disconnect();
    }

    private async Task TickAsync(CancellationToken token)
    {
        if (_clientId.Length == 0) return;
        if (!_enabled())
        {
            // OFF にしたら表示を消して切る
            await ClearAsync();
            Disconnect();
            _last = null;
            return;
        }
        var stats = _stats();
        var now = DateTimeOffset.Now;
        long? kpm = _last is { } last && now > last.At ? KeysPerMinute(stats.Total - last.Total, now - last.At) : null;
        _last = (stats.Total, now);
        var activity = ActivityJson(stats, kpm, _started);
        if (_stream is null)
        {
            await ConnectAsync(token);
            _sent = null;
        }
        if (activity == _sent) return;
        await SendAsync(1, SetActivityJson(activity), token);
        _sent = activity;
    }

    /// <summary>OFF にした・終わるときに、出していた表示を消す (失敗してもかまわない)。</summary>
    private async Task ClearAsync()
    {
        if (_stream is null || _sent is null) return;
        try { await SendAsync(1, SetActivityJson(null), CancellationToken.None); }
        catch (Exception) { }
        _sent = null;
    }

    private async Task ConnectAsync(CancellationToken token)
    {
        for (var i = 0; i < 10; i++)
        {
            var stream = await OpenAsync(i, token);
            if (stream is null) continue;
            _stream = stream;
            try
            {
                await SendAsync(0, HandshakeJson(_clientId), token);
                Diagnostics.Log.Info("Discord につなぎました。");
                return;
            }
            catch (Exception)
            {
                Disconnect();
                throw;
            }
        }
        throw new IOException("Discord が起動していません。");
    }

    private static async Task<Stream?> OpenAsync(int index, CancellationToken token)
    {
        var name = $"discord-ipc-{index}";
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(200, token);
                return pipe;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                await pipe.DisposeAsync();
                return null;
            }
        }
        foreach (var dir in SocketDirectories())
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) continue;
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
        }
        return null;
    }

    /// <summary>macOS・Linux で Discord がソケットを置くところ (Flatpak・Snap 版も)。</summary>
    private static IEnumerable<string> SocketDirectories()
    {
        foreach (var variable in new[] { "XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP" })
        {
            var dir = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(dir)) continue;
            yield return dir;
            yield return Path.Combine(dir, "app", "com.discordapp.Discord");
            yield return Path.Combine(dir, "snap.discord");
        }
        yield return "/tmp";
    }

    /// <summary>1 通送って、返事を 1 通読む (読まずにおくと Discord 側が詰まる)。</summary>
    private async Task SendAsync(int opcode, string json, CancellationToken token)
    {
        var stream = _stream ?? throw new IOException("Discord につないでいません。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(IoTimeout);
        await stream.WriteAsync(Frame(opcode, json), timeout.Token);
        await stream.FlushAsync(timeout.Token);
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, timeout.Token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > 1 << 20) throw new IOException("Discord から読めない返事が来ました。");
        await stream.ReadExactlyAsync(new byte[length], timeout.Token);
        // opcode 2 は Discord が切ったこと (アプリの ID が違うなど)
        if (BinaryPrimitives.ReadInt32LittleEndian(header) == 2) throw new IOException("Discord が接続を閉じました。");
    }

    private void Disconnect()
    {
        _stream?.Dispose();
        _stream = null;
        _sent = null;
    }

    /// <summary>IPC の 1 通 (opcode と長さを little endian の 4 バイトずつ、続けて UTF-8 の JSON)。</summary>
    public static byte[] Frame(int opcode, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        return frame;
    }

    /// <summary>直近の 1 分あたりの打鍵数。</summary>
    public static long KeysPerMinute(long keys, TimeSpan elapsed) =>
        elapsed <= TimeSpan.Zero ? 0 : (long)Math.Round(Math.Max(0, keys) / elapsed.TotalMinutes);

    public static string HandshakeJson(string clientId) =>
        Write(w =>
        {
            w.WriteNumber("v", 1);
            w.WriteString("client_id", clientId);
        });

    /// <summary>表示する内容 (activity)。打った文字やアプリ名は入れない。</summary>
    public static string ActivityJson(Stats stats, long? kpm, DateTimeOffset started) =>
        Write(w =>
        {
            w.WriteString("details", Format($"今日 {stats.Today:N0} 打"));
            w.WriteString("state", kpm is { } k ? Format($"合計 {stats.Total:N0} 打 · {k:N0} 打/分") : Format($"合計 {stats.Total:N0} 打"));
            w.WriteStartObject("timestamps");
            w.WriteNumber("start", started.ToUnixTimeSeconds());
            w.WriteEndObject();
            w.WriteStartArray("buttons");
            w.WriteStartObject();
            w.WriteString("label", "Meltype");
            w.WriteString("url", ProjectInfo.SourceUrl);
            w.WriteEndObject();
            w.WriteEndArray();
        });

    private static string Format(FormattableString text) => FormattableString.Invariant(text);

    /// <summary>SET_ACTIVITY の命令。activity が null なら表示を消す。</summary>
    public static string SetActivityJson(string? activity) =>
        Write(w =>
        {
            w.WriteString("cmd", "SET_ACTIVITY");
            w.WriteStartObject("args");
            w.WriteNumber("pid", Environment.ProcessId);
            w.WritePropertyName("activity");
            if (activity is null) w.WriteNullValue();
            else w.WriteRawValue(activity);
            w.WriteEndObject();
            w.WriteString("nonce", Guid.NewGuid().ToString());
        });

    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        try { _loop.Wait(IoTimeout * 2); }
        catch (AggregateException) { }
        _stop.Dispose();
    }
}
