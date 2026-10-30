// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Meltype.Composition;

/// <summary>
/// Zenzai (azooKey のニューラルかな漢字変換モデル zenz) で変換する。モデルは llama.cpp の llama-server を別のプロセスで動かして使う
/// (どちらも同梱しない。ユーザーが用意した llama-server の実行ファイルと zenz の GGUF ファイルの場所を設定する)。
///
/// 読み (ひらがな) をカタカナにして、zenz の入力の形 (U+EE00 読み U+EE02 文脈 U+EE01) で llama-server の /completion に頼み、
/// 貪欲に生成した文字列を変換結果にする。サーバーが無い・起動できない・応答しないときは null を返すので、呼び出し側で別の変換エンジンを使う。
/// 起動はバックグラウンドで行い、準備できるまでの変換は待たずに null を返す (変換は UI スレッドで動く)。
/// 結果は文節に区切らず、読み全体 1 つの文節で返す。
/// </summary>
public sealed class ZenzaiConverter : IKanjiConverter, IDisposable
{
    private const char InputMark = '';
    private const char OutputMark = '';
    private const char ContextMark = '';
    private const int StartTimeoutMs = 30000;
    private const int MaxReading = 80;
    private const int MaxContext = 100;
    private const int MaxCache = 256;

    /// <summary>
    /// 前の文脈が無いときに代わりに渡す文脈。文脈なしの入力はモデルの学習データにほとんど無く、迷う (きょう → 強 / 今 / 鏡 / 京 がほぼ同じ確率 0.13、
    /// 答えは 強)。文の始まりの合図の「。」を前に置くと、ふつうの読みに寄る (きょう → 今日 0.65、あした → 明日 0.88、しごと → 仕事 0.97。実測)。
    /// 「「」「これは」「私は」も試したが、「。」がいちばん偏らない (これは だと はし → 箸 のように名詞に寄る)。
    /// </summary>
    internal const string NeutralContext = "。";

    private readonly string _serverPath;
    private readonly string _modelPath;
    private readonly int _timeoutMs;
    private readonly Uri? _fixedEndpoint;
    private readonly object _gate = new();
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private Process? _process;
    private Uri? _endpoint;
    private Task? _starting;
    private bool _ready;
    private volatile bool _disposed;
    private int _failures;

    // 応答が遅かった (CPU の負荷が高いなど) ときは、しばらく使わず今までの変換エンジンに任せる。
    // ライブ変換は 1 打鍵ごとに UI スレッドで変換を頼むので、遅いままだと入力が止まる
    private const int BackoffMs = 5000;
    private long _pausedUntil;

    // 1 回ずつは間に合っても (0.4 秒の少し手前で答える)、続けて打つと待つ時間がたまって、入力が数秒止まってから一気に入る (issue #1209)。
    // 直近 BusyWindowMs のあいだに答えを待った合計が 1 回の上限 (_timeoutMs) を超えたら、遅いときと同じく、しばらく使わない
    private const int BusyWindowMs = 1000;
    private readonly Queue<(long End, long Took)> _recent = new();

    /// <param name="serverPath">llama-server の実行ファイル。</param>
    /// <param name="modelPath">zenz の GGUF ファイル。</param>
    /// <param name="timeoutMs">1 回の変換を待つ時間。</param>
    public ZenzaiConverter(string serverPath, string modelPath, int timeoutMs = 400)
    {
        _serverPath = serverPath;
        _modelPath = modelPath;
        _timeoutMs = timeoutMs;
    }

    /// <summary>すでに動いている llama-server を使う (テスト用)。</summary>
    internal ZenzaiConverter(Uri endpoint, int timeoutMs = 400) : this("", "", timeoutMs)
    {
        _fixedEndpoint = endpoint;
        _endpoint = endpoint;
        _ready = true;
    }

    /// <summary>llama-server と GGUF ファイルが指定の場所にあるか (起動できるかは使ってみるまで分からない)。</summary>
    public bool IsInstalled => _fixedEndpoint is not null || File.Exists(_serverPath) && File.Exists(_modelPath);

    /// <summary>使えるか。何度も失敗したら諦める (毎回起動し直して入力が遅くなるのを避ける)。</summary>
    public bool IsAvailable => IsInstalled && _failures < 3;

    /// <summary>何度も失敗して諦めたか (起動しない・答えない)。ファイルは見ない (ZenzaiHolder が変換のたびに見る)。</summary>
    public bool GaveUp => _failures >= 3;

    /// <summary>一度でも答えたか (ZenzaiHolder が、使えていたあとの失敗を一時的なものと見るのに使う)。</summary>
    public bool HasAnswered { get; private set; }

    /// <summary>サーバーの起動をバックグラウンドで始める (入力を始める前に呼ぶ)。</summary>
    public void WarmUp()
    {
        if (!IsAvailable) return;
        lock (_gate) EnsureStarting();
    }

    public string? Convert(string hiragana) =>
        ConvertClauses(hiragana) is { Count: > 0 } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
    {
        if (string.IsNullOrEmpty(hiragana) || hiragana.Length > MaxReading || !IsAvailable || Environment.TickCount64 < Interlocked.Read(ref _pausedUntil)) return null;
        context = string.IsNullOrEmpty(context) ? NeutralContext : context.Length > MaxContext ? context[^MaxContext..] : context;
        var key = context + "\u0000" + hiragana;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached is null ? null : [new ConversionClause(hiragana, cached)];
            EnsureStarting();
            if (!_ready || _endpoint is null) return null;
        }
        var started = Environment.TickCount64;
        var answer = Complete(Prompt(hiragana, context));
        // 読みのまま (変換していない: かんたん → かんたん) の答えは使わない。Mozc などに任せる
        var text = answer;
        var unchanged = answer is not null && answer == hiragana;
        if (unchanged) text = null;
        var finished = Environment.TickCount64;
        var slow = finished - started >= _timeoutMs;
        lock (_gate)
        {
            if (slow || Busy(finished, finished - started)) Interlocked.Exchange(ref _pausedUntil, finished + BackoffMs);
            if (answer is null && !slow) _failures++;
            else if (answer is not null) { _failures = 0; HasAnswered = true; }
            if (_cache.Count >= MaxCache) _cache.Clear();
            // 使わないと決めた答えも覚える (打鍵のたびに聞き直さない)。失敗・遅いときは覚えない
            if (answer is not null && !slow) _cache[key] = text;
        }
        return text is null ? null : [new ConversionClause(hiragana, text)];
    }

    /// <summary>直近 BusyWindowMs のあいだに答えを待った合計が、1 回の上限を超えたか (_gate の中で呼ぶ)。超えたら記録を消す (休んだあとは数え直す)。</summary>
    private bool Busy(long now, long took)
    {
        _recent.Enqueue((now, took));
        while (_recent.Count > 0 && _recent.Peek().End < now - BusyWindowMs) _recent.Dequeue();
        if (_recent.Sum(r => r.Took) < _timeoutMs) return false;
        _recent.Clear();
        return true;
    }

    /// <summary>zenz に渡す文: U+EE00 + カタカナの読み + (文脈があれば U+EE02 + 文脈) + U+EE01。</summary>
    internal static string Prompt(string hiragana, string? context)
    {
        var sb = new StringBuilder();
        sb.Append(InputMark).Append(ToKatakana(hiragana));
        if (!string.IsNullOrEmpty(context)) sb.Append(ContextMark).Append(context);
        return sb.Append(OutputMark).ToString();
    }

    internal static string ToKatakana(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = source[i] is >= 'ぁ' and <= 'ゖ' ? (char)(source[i] + 0x60) : source[i];
        });

    private string? Complete(string prompt)
    {
        try
        {
            var body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["prompt"] = prompt,
                ["n_predict"] = 96,
                ["temperature"] = 0,
                ["top_k"] = 1,
                ["cache_prompt"] = true,
                ["parse_special"] = true,
            });
            using var cts = new CancellationTokenSource(_timeoutMs);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint!, "completion"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using var response = _http.Send(request, cts.Token);
            if (!response.IsSuccessStatusCode) return null;
            using var reader = new StreamReader(response.Content.ReadAsStream(cts.Token), Encoding.UTF8);
            using var json = JsonDocument.Parse(reader.ReadToEnd());
            var content = json.RootElement.TryGetProperty("content", out var c) ? c.GetString() : null;
            return Clean(content);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>生成された文字列から、終わりの記号 (EOS や U+EE0x) 以降と前後の空白を取り除く。空なら null。</summary>
    internal static string? Clean(string? content)
    {
        if (content is null) return null;
        var end = content.IndexOfAny(['', '', '', '', '\n']);
        if (end >= 0) content = content[..end];
        content = content.Replace("</s>", "").Replace("<s>", "").Trim();
        return content.Length == 0 ? null : content;
    }

    private void EnsureStarting()
    {
        if (_ready || _starting is not null || _disposed) return;
        _starting = Task.Run(Start);
    }

    private void Start()
    {
        try
        {
            var port = FreePort();
            var info = new ProcessStartInfo(_serverPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "-m", _modelPath, "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture), "-c", "1024", "-ngl", "0", "--parallel", "1",
                // zenz の前処理 (gpt2-small-japanese-char) は本家の llama.cpp に無く、読み込めない。gpt-2 に差し替えると本家でも動く (#849 で Serugu さんが確かめた)
                "--override-kv", "tokenizer.ggml.pre=str:gpt-2" })
                info.ArgumentList.Add(argument);
            KillStaleServer();
            var process = Process.Start(info);
            if (process is null) { Fail(); return; }
            // Meltype が強制終了されても、llama-server が残らないようにする (Windows: ジョブに入れる。入れておくと、親が消えたとき OS が一緒に終わらせる)
            BindToThisProcess(process);
            WritePidFile(process);
            // 出力を読み捨てる (読まないとパイプが詰まってサーバーが止まる)
            process.OutputDataReceived += static (_, _) => { };
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (_gate)
            {
                if (_disposed) { Kill(process); return; }
                _process = process;
            }
            var endpoint = new Uri($"http://127.0.0.1:{port}/");
            var deadline = Environment.TickCount64 + StartTimeoutMs;
            while (Environment.TickCount64 < deadline && !process.HasExited && !_disposed)
            {
                if (Ping(endpoint))
                {
                    lock (_gate) { _endpoint = endpoint; _ready = true; }
                    return;
                }
                Thread.Sleep(200);
            }
            Fail();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or SocketException or ObjectDisposedException)
        {
            Fail();
        }
    }

    private bool Ping(Uri endpoint)
    {
        try
        {
            using var cts = new CancellationTokenSource(1000);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "health"));
            using var response = _http.Send(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }

    private void Fail()
    {
        lock (_gate) { _failures = 3; }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ---- llama-server を残さない ----

    private static string PidFile => Path.Combine(Path.GetTempPath(), "meltype-zenzai-server.pid");

    private void WritePidFile(Process process)
    {
        try { File.WriteAllText(PidFile, $"{process.Id}\n{Path.GetFileNameWithoutExtension(_serverPath)}\n{process.StartTime.ToUniversalTime().Ticks}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    /// <summary>
    /// 前の Meltype が強制終了されて残った llama-server (PID ファイルに書いてあるもの) を終わらせる。
    /// PID は使い回されるので、名前に加えて起動した時刻も同じときだけ (ユーザーが自分で動かしている別の llama-server を止めないように)。
    /// </summary>
    private static void KillStaleServer()
    {
        try
        {
            if (!File.Exists(PidFile)) return;
            var lines = File.ReadAllLines(PidFile);
            File.Delete(PidFile);
            if (lines.Length < 3 || !int.TryParse(lines[0], out var pid) || pid == Environment.ProcessId || !long.TryParse(lines[2], out var startTicks)) return;
            using var stale = Process.GetProcessById(pid);
            if (string.Equals(stale.ProcessName, lines[1], StringComparison.OrdinalIgnoreCase) && stale.StartTime.ToUniversalTime().Ticks == startTicks) Kill(stale);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static IntPtr s_job;

    /// <summary>Windows: Meltype が終わる (強制終了も含む) と、ジョブの中のプロセスも OS が終わらせる (KILL_ON_JOB_CLOSE)。</summary>
    private static void BindToThisProcess(Process process)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            lock (typeof(ZenzaiConverter))
            {
                if (s_job == IntPtr.Zero)
                {
                    var job = CreateJobObject(IntPtr.Zero, null);
                    if (job == IntPtr.Zero) return;
                    var limits = new JobExtendedLimits { BasicLimitInformation = new JobBasicLimits { LimitFlags = 0x2000 /* JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE */ } };
                    var size = Marshal.SizeOf<JobExtendedLimits>();
                    var buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(limits, buffer, false);
                        if (!SetInformationJobObject(job, 9 /* JobObjectExtendedLimitInformation */, buffer, (uint)size)) return;
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                    s_job = job;
                }
                AssignProcessToJobObject(s_job, process.Handle);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobIoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimits
    {
        public JobBasicLimits BasicLimitInformation;
        public JobIoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    public void Dispose()
    {
        Process? process;
        lock (_gate)
        {
            _disposed = true;
            process = _process;
            _process = null;
            _ready = false;
        }
        if (process is not null)
        {
            Kill(process);
            process.Dispose();
        }
        _http.Dispose();
    }
}
