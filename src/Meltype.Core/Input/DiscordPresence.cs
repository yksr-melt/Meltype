// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Meltype.Config;

namespace Meltype.Input;

/// <summary>
/// Discord のステータス (Rich Presence) に打鍵数と起動からの経過時間を出す (設定「打鍵数」の「数えて Discord に表示する」。既定は OFF)。
/// Discord のローカル IPC (discord-ipc-N。Windows は名前付きパイプ、macOS・Linux は Unix ソケット) で送り、外のサーバーは使わない。
/// 送るのは今日と合計の打鍵数・直近の 1 分あたりの打鍵数・Meltype の起動時刻だけ (打った文字・変換候補・入力先のアプリ名は送らない)。
/// 入力とは別のスレッドで UpdateInterval ごとに送り、Discord が起動していない・切れたときは次の回につなぎ直す (入力には影響しない)。
/// </summary>
public sealed class DiscordPresence : IDisposable
{
    /// <summary>Discord の開発者ポータルで作ったアプリの ID (Application ID)。空なら何も送らない。</summary>
    public const string ClientId = "1567084278423093311";

    public static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(5);
    // 終わるときは、Discord が応答しなくても入力メソッドの終了を遅らせない
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(1);

    /// <summary>出す打鍵数。</summary>
    public readonly record struct Stats(long Today, long Total);

    private readonly Func<bool> _enabled;
    private readonly Func<Stats> _stats;
    private readonly string _clientId;
    // 再接続や表示の OFF/ON で経過時間がリセットされないよう、起動時に一度だけ記録する。
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
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
            catch (Exception ex) when (IsConnectionFailure(ex))
            {
                // Discord が起動していない・閉じた・応答しないときは、次の回につなぎ直す
                if (_stream is not null) Diagnostics.Log.Info($"Discord との接続が切れました: {ex.Message}");
                Disconnect();
            }
            catch (Exception ex)
            {
                // それ以外はコードの誤り。この Task の例外は誰も受け取らない (Dispose が待つだけ) ので、ここで中身ごとログに書いて止める
                Diagnostics.Log.Error($"Discord に表示できなくなりました: {ex}");
                break;
            }
            try { await Task.Delay(UpdateInterval, _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
        using (var exit = new CancellationTokenSource(ExitTimeout)) await ClearAsync(exit.Token);
        Disconnect();
    }

    private async Task TickAsync(CancellationToken token)
    {
        if (_clientId.Length == 0) return;
        if (!_enabled())
        {
            // OFF にしたら表示を消して切る
            await ClearAsync(token);
            Disconnect();
            _last = null;
            return;
        }
        var stats = _stats();
        var now = DateTimeOffset.Now;
        long? kpm = _last is { } last && now > last.At ? KeysPerMinute(stats.Total - last.Total, now - last.At) : null;
        _last = (stats.Total, now);
        var activity = ActivityJson(stats, kpm, _startedAt);
        if (_stream is null)
        {
            await ConnectAsync(token);
            _sent = null;
        }
        if (activity == _sent) return;
        await SendAsync(1, SetActivityJson(activity), token);
        _sent = activity;
    }

    /// <summary>Discord とのやり取りで、Discord のせいで起きる例外 (起動していない・閉じた・応答しない・答えが壊れている)。</summary>
    private static bool IsConnectionFailure(Exception ex) =>
        ex is IOException or System.ComponentModel.Win32Exception or TimeoutException or ObjectDisposedException or OperationCanceledException
            or UnauthorizedAccessException or System.Text.Json.JsonException;

    /// <summary>OFF にした・終わるときに、出していた表示を消す (失敗してもかまわない)。</summary>
    private async Task ClearAsync(CancellationToken token)
    {
        if (_stream is null || _sent is null) return;
        try { await SendAsync(1, SetActivityJson(null), token); }
        catch (Exception ex) when (IsConnectionFailure(ex)) { }
        _sent = null;
    }

    private async Task ConnectAsync(CancellationToken token)
    {
        foreach (var name in PipeNames())
        {
            var stream = await OpenAsync(name, token);
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

    private static readonly string[] RuntimeVariables = ["XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP"];

    /// <summary>Discord が待つパイプの名前。macOS・Linux では、Discord と同じく最初に設定されている環境変数のフォルダーにあるソケット (Flatpak・Snap 版も)。</summary>
    private static IEnumerable<string> PipeNames()
    {
        var names = Enumerable.Range(0, 10).Select(i => $"discord-ipc-{i}");
        if (OperatingSystem.IsWindows()) return names;
        var root = RuntimeVariables.Select(Environment.GetEnvironmentVariable).FirstOrDefault(dir => !string.IsNullOrEmpty(dir)) ?? "/tmp";
        string[] directories = [root, Path.Combine(root, "app", "com.discordapp.Discord"), Path.Combine(root, "snap.discord")];
        // NamedPipeClientStream は、絶対パスの名前をその場所の Unix ソケットとして開く。無いソケットは時間切れまで待つので、あるものだけ試す
        return directories.SelectMany(directory => names.Select(name => Path.Combine(directory, name))).Where(File.Exists);
    }

    private static async Task<Stream?> OpenAsync(string name, CancellationToken token)
    {
        // パイプの名前と /tmp はユーザー間で共有で、ほかのユーザーが先に同じ名前のパイプ・ソケットを作って待てる。
        // CurrentUserOnly は、つないだ相手のユーザーが自分でなければ、何も送る前に UnauthorizedAccessException にする
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(200, token);
            return pipe;
        }
        // ほかのユーザーが使っている番号なら、自分の Discord は次以降の番号で待っている
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or SocketException)
        {
            await pipe.DisposeAsync();
            return null;
        }
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
        var response = new byte[length];
        await stream.ReadExactlyAsync(response, timeout.Token);
        VerifyResponse(BinaryPrimitives.ReadInt32LittleEndian(header), Encoding.UTF8.GetString(response));
    }

    /// <summary>IPC 応答を検証する。Discord の ERROR イベントを成功扱いしない。</summary>
    public static void VerifyResponse(int opcode, string json)
    {
        if (opcode == 2) throw new IOException("Discord が接続を閉じました。");
        if (opcode is not 0 and not 1) throw new IOException($"Discord から不明な opcode が返されました: {opcode}");
        using var response = JsonDocument.Parse(json);
        var root = response.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new IOException("Discord の返事が JSON オブジェクトではありません。");
        if (root.TryGetProperty("evt", out var evt) && evt.ValueKind == JsonValueKind.String &&
            evt.GetString() == "ERROR")
        {
            var detail = "不明なエラー";
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                var code = data.TryGetProperty("code", out var codeValue) ? codeValue.ToString() : "";
                var message = data.TryGetProperty("message", out var messageValue) ? messageValue.ToString() : "";
                detail = $"code={code}, message={message}";
            }
            throw new IOException($"Discord Rich Presence の ERROR 応答: {detail}");
        }
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
    public static string ActivityJson(Stats stats, long? kpm, DateTimeOffset startedAt) =>
        Write(w =>
        {
            w.WriteString("details", Format($"今日 {stats.Today:N0} 打"));
            w.WriteString("state", kpm is { } k ? Format($"合計 {stats.Total:N0} 打 · {k:N0} 打/分") : Format($"合計 {stats.Total:N0} 打"));
            w.WriteStartObject("timestamps");
            w.WriteNumber("start", startedAt.ToUnixTimeSeconds());
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
        try { _loop.Wait(ExitTimeout * 2); }
        catch (AggregateException) { }
        _stop.Dispose();
    }
}
