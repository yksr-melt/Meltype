// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Buffers;
using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meltype.Composition;

/// <summary>
/// Zenzai (azooKey のニューラルかな漢字変換モデル zenz) で変換する。モデルは llama.cpp の llama-server を別のプロセスで動かして使う。
///
/// 読み (ひらがな) をカタカナにして、zenz v3 系の入力の形 (U+EE02 文脈 U+EE00 読み U+EE01) で llama-server の /completion に頼み、
/// 貪欲に生成した文字列を変換結果にする。サーバーが無い・起動できない・応答しないときは null を返すので、呼び出し側で別の変換エンジンを使う。
/// 起動はバックグラウンドで行い、準備できるまでの変換は待たずに null を返す。
/// 結果は文節に区切らず、読み全体 1 つの文節で返す。
/// </summary>
public sealed class ZenzaiConverter : IKanjiConverter, IDisposable
{
    private const char InputMark = '';
    private const char OutputMark = '';
    private const char ContextMark = '';
    private static readonly SearchValues<char> OutputBoundaries = SearchValues.Create("\uEE00\uEE01\uEE02\uEE03\uEE04\uEE05\uEE06\uEE07\uEE08\n");
    private const int StartTimeoutMs = 30000;
    private const int MaxReading = 80;
    private const int MaxContext = 100;

    // ワーカーで待つ時間。入力は止めないので、CPU だけで計算する PC でも答えが届く長さにする (Space で待つのは ConversionBudget の間だけ)
    private const int DefaultTimeoutMs = 1500;
    private const int MaxCache = 256;

    /// <summary>
    /// 前の文脈が無いときに代わりに渡す文脈。文脈なしの入力はモデルの学習データにほとんど無く、迷う (きょう → 強 / 今 / 鏡 / 京 がほぼ同じ確率 0.13、
    /// 答えは 強)。文の始まりの合図の「。」を前に置くと、ふつうの読みに寄る (きょう → 今日 0.65、あした → 明日 0.88、しごと → 仕事 0.97。実測)。
    /// 「「」「これは」「私は」も試したが、「。」がいちばん偏らない (これは だと はし → 箸 のように名詞に寄る)。
    /// </summary>
    internal const string NeutralContext = "。";

    private readonly string _serverPath;
    private readonly string _modelPath;
    private readonly string _device;
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
    private volatile int _failures;
    private volatile bool _hasAnswered;
    private readonly LatestRequestWorker<LiveRequest> _worker;

    // 打鍵が続く間は最後の読みだけを推論する (CPU のみのときに推論が積み重ならないように)
    private const int SettleMs = 60;

    // 応答が遅かった (CPU の負荷が高いなど) ときは、しばらく使わず Mozc に任せる。
    private const int BackoffMs = 5000;
    private long _pausedUntil;

    /// <param name="serverPath">llama-server の実行ファイル。</param>
    /// <param name="modelPath">zenz の GGUF ファイル。</param>
    /// <param name="timeoutMs">1 回の変換を待つ時間。</param>
    /// <param name="device">使う GPU (llama-server の --device)。空なら CPU だけで計算する。</param>
    public ZenzaiConverter(string serverPath, string modelPath, int timeoutMs = DefaultTimeoutMs, string device = "")
    {
        _serverPath = serverPath;
        _modelPath = modelPath;
        _timeoutMs = timeoutMs;
        _device = device;
        _worker = new LatestRequestWorker<LiveRequest>(ProcessBackground, SettleMs);
    }

    /// <summary>
    /// llama-server に渡す引数 (issue #1307: 使う GPU)。
    /// 使う GPU が空なら、今までどおり -ngl 0 で CPU だけで計算する (--device none を受け付けない古い llama-server でも動くように)。
    /// 使う GPU を書いたら --device だけを渡し、GPU に載せる層の数は llama-server に任せる (-ngl の既定は auto で、空いている VRAM に合わせる)。
    /// </summary>
    internal static List<string> ServerArguments(string modelPath, int port, string device)
    {
        var arguments = new List<string> { "-m", modelPath, "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture), "-c", "1024", "--parallel", "1",
            // zenz の前処理 (gpt2-small-japanese-char) は本家の llama.cpp に無く、読み込めない。gpt-2 に差し替えると本家でも動く (#849 で Serugu さんが確かめた)
            "--override-kv", "tokenizer.ggml.pre=str:gpt-2" };
        if (device.Trim() is { Length: > 0 } name) arguments.AddRange(["--device", name]);
        else arguments.AddRange(["-ngl", "0"]);
        return arguments;
    }

    /// <summary>すでに動いている llama-server を使う (テスト用)。</summary>
    internal ZenzaiConverter(Uri endpoint, int timeoutMs = DefaultTimeoutMs) : this("", "", timeoutMs)
    {
        _fixedEndpoint = endpoint;
        _endpoint = endpoint;
        _ready = true;
    }

    /// <summary>llama-server と GGUF ファイルが指定の場所にあるか (起動できるかは使ってみるまで分からない)。</summary>
    public bool IsInstalled => _fixedEndpoint is not null || File.Exists(_serverPath) && File.Exists(_modelPath);

    /// <summary>使えるか。何度も失敗したら諦める。</summary>
    public bool IsAvailable => IsInstalled && _failures < 3;

    /// <summary>何度も失敗して諦めたか (起動しない・答えない)。ファイルは見ない (ZenzaiHolder が変換のたびに見る)。</summary>
    public bool GaveUp => _failures >= 3;

    /// <summary>llama-server が起動して、変換を頼めるか (起動中は false)。</summary>
    public bool IsReady { get { lock (_gate) return _ready; } }

    /// <summary>応答が遅かったので、しばらく使わずに休んでいるか。</summary>
    public bool IsResting => Environment.TickCount64 < Interlocked.Read(ref _pausedUntil);

    /// <summary>一度でも答えたか (ZenzaiHolder が、使えていたあとの失敗を一時的なものと見るのに使う)。</summary>
    public bool HasAnswered => _hasAnswered;

    public ConversionBudget Budget { get; internal set; } = new();

    public event Action? ResultReady;

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
        ThrowWorkerFailure();
        if (string.IsNullOrEmpty(hiragana) || hiragana.Length > MaxReading || !IsAvailable || Environment.TickCount64 < Interlocked.Read(ref _pausedUntil)) return null;
        context = string.IsNullOrEmpty(context) ? NeutralContext : context.Length > MaxContext ? context[^MaxContext..] : context;
        var key = context + "\u0000" + hiragana;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached is null ? null : [new ConversionClause(hiragana, cached)];
            EnsureStarting();
            if (!_ready || _endpoint is null) return null;
        }
        var request = new LiveRequest(hiragana, context, key);
        _worker.Submit(request);
        if (!_worker.Wait(request, Budget.RemainingMs)) return null;
        ThrowWorkerFailure();
        lock (_gate) return _cache.TryGetValue(key, out var answered) && answered is not null ? [new ConversionClause(hiragana, answered)] : null;
    }

    // ワーカーの例外は入力スレッドで投げ直す (HybridConverter が受けて持ち主に知らせる: #973)
    private Exception? _workerFailure;

    private void ThrowWorkerFailure()
    {
        if (Interlocked.Exchange(ref _workerFailure, null) is { } failure) throw new InvalidOperationException("Zenzai の変換で例外になりました", failure);
    }

    private readonly record struct LiveRequest(string Hiragana, string Context, string Key);

    private void ProcessBackground(LiveRequest request)
    {
        try
        {
            // 変換しなかった・失敗した答えは知らせない (表示が変わらない。失敗では再描画がまた同じ要求を出す)。
            // 壊れた答えは知らせる (使うかは HybridConverter が Mozc の候補と比べて決めるので、ここでは分からない。使わなければ表示は変わらない)
            if (ConvertNow(request.Hiragana, request.Context, request.Key) is not null) ResultReady?.Invoke();
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _workerFailure, ex);
        }
    }

    private IReadOnlyList<ConversionClause>? ConvertNow(string hiragana, string context, string key)
    {
        var started = Environment.TickCount64;
        var answer = Complete(Prompt(hiragana, context));
        // 読みのまま (変換していない: かんたん → かんたん) の答えは使わない。Mozc に任せる。
        // 壊れた答え (はーどうぇ → ハードウェアウェア…) かは、Mozc の候補と比べられる HybridConverter が決める
        var text = answer == hiragana ? null : answer;
        var slow = Environment.TickCount64 - started >= _timeoutMs;
        lock (_gate)
        {
            if (slow) Interlocked.Exchange(ref _pausedUntil, Environment.TickCount64 + BackoffMs);
            if (answer is null && !slow) _failures++;
            else if (answer is not null) { _failures = 0; _hasAnswered = true; }
            if (_cache.Count >= MaxCache) _cache.Clear();
            // 使わないと決めた答えも覚える (打鍵のたびに聞き直さない)。失敗・遅いときは覚えない
            if (answer is not null && !slow) _cache[key] = text;
        }
        return text is null ? null : [new ConversionClause(hiragana, text)];
    }

    /// <summary>zenz v3 系に渡す文は、文脈を先に置く。v2 の順序では前の文章が変換結果に混ざることがある。</summary>
    internal static string Prompt(string hiragana, string? context)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(context)) sb.Append(ContextMark).Append(context);
        return sb.Append(InputMark).Append(ToKatakana(hiragana)).Append(OutputMark).ToString();
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
            var body = new JsonObject
            {
                ["prompt"] = prompt,
                ["n_predict"] = 96,
                ["temperature"] = 0,
                ["top_k"] = 1,
                ["cache_prompt"] = true,
                ["parse_special"] = true,
            }.ToJsonString();
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or IOException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// 答えが読みと合っているか。答えのかな (カタカナはひらがなとして) と記号は、読みのその位置の文字と同じでないといけない。
    /// 漢字 1 文字と、英字・数字の並び 1 つは、読みの 1 文字以上に当たるとみなす (今日 → きょう、Microsoft → まいくろそふと)。
    /// モデルが読みに無いものを続けた答え (はーどうぇ → ハードウェアウェア: issue #1675、きょうはで → 今日はでで・直前の文節の混入: issue #1511) は合わない。
    /// </summary>
    internal static bool IsPlausible(string hiragana, string answer)
    {
        var tokens = Tokens(answer);
        var reading = hiragana.Select(Normalize).ToArray();
        var dead = new HashSet<(int, int)>();
        return Match(0, 0);

        // tokens[t] 以降で reading[r] 以降をちょうど作れるか (作れなかった組は覚えて、もう試さない)
        bool Match(int t, int r)
        {
            if (t == tokens.Count) return r == reading.Length;
            if (r == reading.Length || dead.Contains((t, r))) return false;
            var (text, wildcard) = tokens[t];
            if (!wildcard ? Normalize(text[0]) == reading[r] && Match(t + 1, r + 1)
                : (text.Length == 1 && Normalize(text[0]) == reading[r] && Match(t + 1, r + 1)) || Enumerable.Range(r + 1, reading.Length - r).Any(end => Match(t + 1, end)))
                return true;
            dead.Add((t, r));
            return false;
        }
    }

    /// <summary>答えを、読みと 1 文字ずつ比べる文字 (かな・記号) と、読みの 1 文字以上に当たる字 (漢字 1 文字・英数字の並び) に分ける。</summary>
    private static List<(string Text, bool Wildcard)> Tokens(string answer)
    {
        var tokens = new List<(string, bool)>();
        for (var i = 0; i < answer.Length;)
        {
            var c = Normalize(answer[i]);
            if (char.IsAsciiLetterOrDigit(c))
            {
                var end = i;
                while (end < answer.Length && char.IsAsciiLetterOrDigit(Normalize(answer[end]))) end++;
                tokens.Add((answer[i..end], true));
                i = end;
                continue;
            }
            var length = char.IsSurrogatePair(answer, i) ? 2 : 1;
            var kana = c is >= 'ぁ' and <= 'ゖ' or 'ー' or 'ゝ' or 'ゞ';
            tokens.Add((answer.Substring(i, length), !kana && !char.IsAsciiLetterOrDigit(c) && !char.IsPunctuation(c) && !char.IsSymbol(c) && !char.IsWhiteSpace(c)));
            i += length;
        }
        return tokens;
    }

    /// <summary>比べるための文字: カタカナはひらがな (ヵ・ヶ は漢字と同じ扱いのまま)、全角の英数字・記号は半角。</summary>
    private static char Normalize(char c) => c switch
    {
        >= 'ァ' and <= 'ヴ' and not ('ヵ' or 'ヶ') => (char)(c - 0x60),
        >= '！' and <= '～' => (char)(c - 0xFEE0),
        _ => c,
    };

    /// <summary>生成された文字列から、終わりの記号 (EOS や U+EE0x) 以降と前後の空白を取り除く。空なら null。</summary>
    internal static string? Clean(string? content)
    {
        if (content is null) return null;
        var end = content.AsSpan().IndexOfAny(OutputBoundaries);
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
            foreach (var argument in ServerArguments(_modelPath, port, _device))
                info.ArgumentList.Add(argument);
            // Windows 版の llama.cpp は既定で全物理コアを使い、計算後も 200ms ほど CPU を使用し続ける。ライブ変換では打鍵のたびに変換するため、入力中はこの状態が続く。
            // そこでスレッド数を 4 までに制限し (zenz は小さいモデルなので、4 より増やしてもほとんど速くならない)、計算後に CPU を使い続ける時間を短くする。どの設定を読むかはビルドにより異なる:
            // LLVM / Intel の OpenMP でビルドしたもの (本家の Windows の CPU 版) は KMP_BLOCKTIME を読むので、5ms にする。
            // OpenMP なしのビルドは --poll を読むので、0 にする (--poll は、次の計算が来たかを CPU を使ってループで確かめ続ける長さ。0 にするとループせず、すぐに条件変数で待つ)。
            // MSVC の OpenMP でビルドしたものはどちらも読まず、スレッド数の制限だけになる (OMP_WAIT_POLICY=PASSIVE なら CPU 時間は減るが、変換が 20ms ほど遅くなる)
            // LLAMA_ARG_THREADS・KMP_BLOCKTIME が設定されている場合は、その値を優先する (-t は LLAMA_ARG_THREADS より優先されるため付けない)
            if (!info.Environment.ContainsKey("LLAMA_ARG_THREADS"))
            {
                info.ArgumentList.Add("-t");
                info.ArgumentList.Add(Math.Min(Environment.ProcessorCount, 4).ToString(CultureInfo.InvariantCulture));
            }
            info.ArgumentList.Add("--poll");
            info.ArgumentList.Add("0");
            info.Environment.TryAdd("KMP_BLOCKTIME", "5");
            EndStaleServers();
            var process = Process.Start(info);
            if (process is null) { Fail(); return; }
            // Meltype が強制終了されても、llama-server が残らないようにする (Windows: ジョブに入れる。入れておくと、親が消えたとき OS が一緒に終わらせる)
            BindToThisProcess(process);
            using (var self = Process.GetCurrentProcess())
            {
                _pidFile = RecordServer(process.Id, Path.GetFileNameWithoutExtension(_serverPath), process.StartTime.ToUniversalTime().Ticks,
                    self.Id, self.StartTime.ToUniversalTime().Ticks);
            }
            // 出力を読み捨てる (読まないとパイプが詰まってサーバーが止まる)
            process.OutputDataReceived += static (_, _) => { };
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (_gate)
            {
                if (_disposed)
                {
                    Kill(process);
                    DeletePidFile();
                    return;
                }
                _process = process;
            }
            var endpoint = new Uri($"http://127.0.0.1:{port}/");
            var deadline = Environment.TickCount64 + StartTimeoutMs;
            while (Environment.TickCount64 < deadline && !process.HasExited && !_disposed)
            {
                if (Ping(endpoint))
                {
                    lock (_gate) { _endpoint = endpoint; _ready = true; }
                    ResultReady?.Invoke();
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

    // 起動した llama-server 1 つにつき 1 ファイル。同じ PC で Meltype が複数動く (IME と自己診断など) ので、持ち主の Meltype も書く
    internal static string PidDirectory => Path.Combine(Path.GetTempPath(), "meltype-zenzai");

    private string? _pidFile;

    /// <summary>起動した llama-server と、持ち主の Meltype を書いておく (<see cref="EndStaleServers"/> が読む)。書いたファイルを返す。</summary>
    internal static string? RecordServer(int pid, string name, long startTicks, int ownerPid, long ownerStartTicks)
    {
        try
        {
            Directory.CreateDirectory(PidDirectory);
            var file = Path.Combine(PidDirectory, $"{pid}.pid");
            File.WriteAllText(file, $"{pid}\n{name}\n{startTicks}\n{ownerPid}\n{ownerStartTicks}");
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// 持ち主の Meltype が終わったのに残っている llama-server (強制終了されたときなど) を終わらせる。持ち主が動いているものは止めない。
    /// PID は使い回されるので、プロセスは名前と起動した時刻も同じときだけ同じものとみなす (ユーザーが自分で動かしている別の llama-server を止めないように)。
    /// </summary>
    public static void EndStaleServers()
    {
        string[] files;
        try { files = Directory.Exists(PidDirectory) ? Directory.GetFiles(PidDirectory, "*.pid") : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        foreach (var file in files)
        {
            try
            {
                var lines = File.ReadAllLines(file);
                if (lines.Length < 5 || !int.TryParse(lines[0], out var pid) || !long.TryParse(lines[2], out var startTicks) ||
                    !int.TryParse(lines[3], out var ownerPid) || !long.TryParse(lines[4], out var ownerStartTicks))
                {
                    File.Delete(file);
                    continue;
                }
                if (IsRunning(ownerPid, null, ownerStartTicks)) continue;
                File.Delete(file);
                if (!IsRunning(pid, lines[1], startTicks)) continue;
                using var stale = Process.GetProcessById(pid);
                Kill(stale);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    private void DeletePidFile()
    {
        try { if (_pidFile is not null) File.Delete(_pidFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>pid のプロセスが動いていて、起動した時刻 (と name があれば名前) が同じか。</summary>
    private static bool IsRunning(int pid, string? name, long startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return (name is null || string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase)) &&
                process.StartTime.ToUniversalTime().Ticks == startTicks;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
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
        _worker.Dispose();
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
        DeletePidFile();
        _http.Dispose();
    }
}
