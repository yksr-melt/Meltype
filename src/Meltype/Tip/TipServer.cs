// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Diagnostics;
using Meltype.Input;

namespace Meltype.Tip;

/// <summary>
/// Meltype IME (TSF の DLL, native/tip) とつなぐ名前付きパイプのサーバー。
/// DLL はアプリのプロセスに読み込まれるので、入力の本体 (辞書・学習・変換) はここ (Meltype.exe) に 1 つだけ置き、
/// DLL からはキーを 1 つずつ受け取って <see cref="SessionResult"/> を JSON で返す。
///
/// やり取りは 1 メッセージ 1 JSON (UTF-8)。要求:
///   {"op":"key","sid":"…","vk":65,"ch":97,"mods":1,"before":"…","after":"…","process":"notepad","moved":true,"composing":true}
///   {"op":"commit","sid":"…","moved":true} / {"op":"select","sid":"…","index":2} / {"op":"close","sid":"…"} / {"op":"hello","process":"…"}
///   {"op":"toggle","sid":"…","process":"…","before":"…","japanese":true} (半角/全角。consumed が true なら、コードの行を日本語にした / 英数に戻した)
///   {"op":"mode","sid":"…","process":"…","before":"…","passed":37,"ctrl":true} (キャレットが動いた・文字が変わった。入力モードの表示を合わせる)
/// passed は、DLL がアプリに通したキー (0 はキーなしでキャレットが動いた、-1 は文字が変わっただけ)。key・toggle にも、まだ伝えていなければ付ける。
/// 応答: {"active":true, "consumed":…, "commits":[…], "view":{…}, "code":…, "english":…} (active が false なら DLL は何もせずにキーを通す)
/// code はコードの入力欄か、english は次に打つキーがコードの行なので英数のまま通るか (DLL はタスクバーに「A」を出す)。
/// moved は、前に確定してからキャレットが動いたかもしれないこと (前の語を確定し直さない)。
/// commits の確定し直し (deleteBefore) には、消す文字 (expect) を付ける。DLL は入力欄の文字が同じときだけ消す。
/// sid は DLL が入力欄 (スレッド) ごとに振る ID。入力の本体はつながり (パイプ) ごとに持ち、すべて UI スレッドで動かす。
/// 別のつながりの sid には触れない (つなぎ直した DLL が古い入力の続きを拾わないように。ほかのアプリの入力を読めないように)。
///
/// 同じユーザーのほかのプロセスが同じ名前のパイプを先に作ると、打鍵を横取りできてしまう。最初のパイプは FirstPipeInstance で作り、
/// 先に作られていたら始めない。Meltype.exe より高い権限で動くアプリ (管理者として動くアプリなど) とサインインの画面には、
/// DLL の側でつながない (Meltype.exe の権限より上の入力を扱わない)。
/// </summary>
internal sealed class TipServer : IDisposable
{
    private readonly Control _invoker;
    private readonly CompositionService _composition;
    private readonly Func<Settings> _settings;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>1 つの要求の大きさの上限 (キー 1 つ分なら 1KB もない)。</summary>
    private const int MaxRequestBytes = 64 * 1024;

    /// <summary>1 つのつながりで持つ入力の本体の数の上限 (1 つのスレッドに 1 つなので、ふつうは 1 つ)。</summary>
    private const int MaxSessionsPerConnection = 16;

    public TipServer(Control invoker, CompositionService composition, Func<Settings> settings)
    {
        _invoker = invoker;
        _composition = composition;
        _settings = settings;
    }

    /// <summary>Meltype IME が登録されていないと確かに分かるか (レジストリを読めないときは false)。</summary>
    public static bool IsKnownUnregistered => Registration() == false;

    /// <summary>Meltype IME (native/tip の DLL) が Windows に登録されているか。</summary>
    public static bool IsRegistered => Registration() == true;

    // 登録されているか。レジストリを読めなければ null
    private static bool? Registration()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\CTF\TIP\{417D801B-A9BD-4C26-BD16-356A825A6998}");
            return key is not null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>今のユーザーの「言語と地域」のキーボードの一覧に Meltype IME が入っているか (Win + Space で選べるか)。</summary>
    public static bool IsInUserLanguageList
    {
        get
        {
            try
            {
                using var profiles = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\International\User Profile");
                if (profiles is null) return false;
                foreach (var language in profiles.GetSubKeyNames())
                {
                    using var key = profiles.OpenSubKey(language);
                    if (key?.GetValueNames().Any(name => name.Contains("{417D801B-A9BD-4C26-BD16-356A825A6998}", StringComparison.OrdinalIgnoreCase)) == true) return true;
                }
            }
            catch
            {
            }
            return false;
        }
    }

    [ComImport, Guid("71c6e74c-0f28-11d8-a82a-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfileMgr
    {
        [PreserveSig]
        int ActivateProfile(uint profileType, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
    }

    /// <summary>
    /// いま使う入力方式を Meltype IME にする (このサインインの間の、すべてのアプリで)。動作モードを Meltype IME にしたときに呼ぶ。
    /// Win + Space で選び直さなくても、すぐに入力欄に直接打てるように。UI スレッドから呼ぶ。
    /// </summary>
    public static void ActivateForSession()
    {
        if (!IsRegistered || !IsInUserLanguageList) return;
        try
        {
            var manager = (ITfInputProcessorProfileMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("33C53A50-F456-4884-B049-85FD643ECFED"))!)!;
            var clsid = new Guid("417D801B-A9BD-4C26-BD16-356A825A6998");
            var profile = new Guid("21F643F4-72D5-4946-BF70-0A126AB52D05");
            // 1 = TF_PROFILETYPE_INPUTPROCESSOR、0x20000000 = TF_IPPMF_FORSESSION、0x4 = TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE
            var hr = manager.ActivateProfile(1, 0x0411, ref clsid, ref profile, IntPtr.Zero, 0x20000000 | 0x4);
            if (hr < 0) Log.Warn($"Meltype IME に切り替えられませんでした (0x{hr:X8})。Win + Space で選んでください。");
            Marshal.ReleaseComObject(manager);
        }
        catch (Exception ex)
        {
            Log.Warn($"Meltype IME に切り替えられませんでした: {ex.Message}");
        }
    }

    /// <summary>パイプの名前。ユーザーごとに分ける (同じ PC の別のユーザーの入力を受け取らないように)。</summary>
    public static string PipeName => $"Meltype.Tip.{WindowsIdentity.GetCurrent().User?.Value ?? "user"}";

    public void Start()
    {
        // Start は UI スレッドから呼ぶ。Meltype.exe 自身の画面から来た要求を見分けるのに、このスレッドを覚えておく
        _uiThread = GetCurrentThreadId();
        _ = Task.Run(AcceptLoop);
        Log.Info($"Meltype IME のサーバーを開始しました (\\\\.\\pipe\\{PipeName})。");
    }

    private PipeSecurity CreateSecurity()
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("ユーザーの SID が分かりません");
        // 本人・SYSTEM・管理者と、ストアアプリ (AppContainer) からつなげるようにする。
        // より制限の強いサンドボックス (ALL RESTRICTED APP PACKAGES。ブラウザーの描画のプロセスなど) は入れない (入力欄を持たないので要らない)。
        // 整合性レベルの印 (Low まで書き込める) は、作るときに付けると特権が要ると断られるので、作った後に SetLowIntegrity で付ける。
        var security = new PipeSecurity();
        // GA (汎用の権限) はパイプの権限に置き換わらず、2 つ目のパイプを作るときに拒否されるので、ファイルの権限で書く。
        // ストアアプリには読み書きだけ (0x12019B = 読み取り・データの書き込み・属性の書き込み。パイプを増やす権限は渡さない)
        security.SetSecurityDescriptorSddlForm($"D:(A;;FA;;;{user})(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x12019b;;;AC)");
        return security;
    }

    private bool _labelWarned;

    /// <summary>
    /// パイプに「整合性レベル Low からも書き込める」印を付ける。ストアアプリ (AppContainer) やブラウザーのサンドボックスは整合性レベルが Low で、
    /// 印が無いと Medium のパイプに書き込めない (つなげない)。
    /// </summary>
    private void SetLowIntegrity(NamedPipeServerStream pipe)
    {
        IntPtr descriptor = IntPtr.Zero;
        try
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("S:(ML;;NW;;;LW)", 1, out descriptor, IntPtr.Zero) ||
                !GetSecurityDescriptorSacl(descriptor, out _, out var sacl, out _))
            {
                throw new System.ComponentModel.Win32Exception();
            }
            var error = SetSecurityInfo(pipe.SafePipeHandle, 6 /* SE_KERNEL_OBJECT */, 0x10 /* LABEL_SECURITY_INFORMATION */, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, sacl);
            if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
        }
        catch (Exception ex)
        {
            if (!_labelWarned) Log.Warn($"Meltype IME: パイプに整合性レベルの印を付けられませんでした (ストアアプリからはつながりません): {ex.Message}");
            _labelWarned = true;
        }
        finally
        {
            if (descriptor != IntPtr.Zero) LocalFree(descriptor);
        }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr descriptor, IntPtr size);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetSecurityDescriptorSacl(IntPtr descriptor, out bool present, out IntPtr sacl, out bool defaulted);

    [System.Runtime.InteropServices.DllImport("advapi32.dll")]
    private static extern uint SetSecurityInfo(SafeHandle handle, int objectType, uint securityInfo, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DisconnectNamedPipe(SafeHandle pipe);

    private async Task AcceptLoop()
    {
        PipeSecurity? security;
        try
        {
            security = CreateSecurity();
        }
        catch (Exception ex)
        {
            Log.Warn($"Meltype IME: パイプのアクセス権を作れませんでした (ストアアプリからはつながりません): {ex.Message}");
            security = null;
        }
        var first = true;
        // 待っているパイプを作り直したとき。自分のつながりが残っていれば、独占して作れなくてもそのまま作る
        var recreating = false;
        var recreated = 0;
        var recreateTries = 0;
        var createRetries = 0;
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            var exclusive = first;
            try
            {
                var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
                // 整合性レベルの印は、パイプ全体に 1 回だけ付ける (付けるのに WRITE_OWNER が要る。2 つ目からは WRITE_OWNER を求めると拒否される)
                pipe = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message, options, 0, 0, security, HandleInheritability.None, first ? PipeAccessRights.TakeOwnership : 0);
                if (first && security is not null) SetLowIntegrity(pipe);
                first = false;
            }
            catch (UnauthorizedAccessException) when (first && recreating && Volatile.Read(ref _connections) > 0)
            {
                // 作り直し: 自分のつながりのパイプが残っている (印もそのパイプに残っている) ので、独占せずに作る
                first = false;
                continue;
            }
            catch (UnauthorizedAccessException) when (first && recreating && ++recreateTries < 10)
            {
                // 作り直し: 閉じたパイプを DLL の側がまだ持っていて、パイプが残っていることがある。少し待ってやり直す
                try { await Task.Delay(500, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            catch (UnauthorizedAccessException ex) when (first && recreating)
            {
                Log.Error($"Meltype IME: パイプを作り直せませんでした (前のパイプがまだ残っているか、ほかのプログラムが同じ名前で作りました)。Meltype を再起動すると直ります: {ex.Message}");
                return;
            }
            catch (UnauthorizedAccessException ex) when (first)
            {
                Log.Error($"Meltype IME: 同じ名前のパイプがもうあるので、サーバーを始めません (Meltype がもう 1 つ動いているか、ほかのプログラムが使っています): {ex.Message}");
                return;
            }
            catch (Exception ex) when (first && security is not null)
            {
                // 最初のパイプがアクセス権付きで作れない: 作れないままよりは、ストアアプリからつながらなくても、ほかのアプリで使えるほうがよい
                Log.Warn($"Meltype IME: アクセス権付きのパイプを作れませんでした (ストアアプリからはつながりません): {ex.Message}");
                security = null;
                continue;
            }
            catch (Exception ex) when (!first)
            {
                // 2 つ目からのパイプ: 一時的な失敗 (資源不足など) なので、アクセス権はそのままで、少しずつ間を空けてやり直す
                // (アクセス権を外すと、ストアアプリからつながらないパイプが混ざる。止めると、それからつなぐアプリで使えなくなる)
                createRetries++;
                if (createRetries == 1) Log.Warn($"Meltype IME: パイプを作れませんでした (やり直します): {ex.Message}");
                try { await Task.Delay(Math.Min(5000, 500 * createRetries), _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            catch (Exception ex)
            {
                Log.Error($"Meltype IME: パイプを作れませんでした: {ex}");
                return;
            }
            // 作り直しで独占せずに作ったのに、その間に自分のつながりが全部切れていた: これが新しいパイプの最初の 1 つで、
            // 印と独占が付いていないかもしれないので、独占して作り直す
            if (recreating && !exclusive && Volatile.Read(ref _connections) == 0)
            {
                pipe.Dispose();
                first = true;
                continue;
            }
            // つなぎかけて切れた (ERROR_NO_DATA など) ときは、同じパイプを DisconnectNamedPipe で戻して待ち直す。
            // (.NET の Disconnect は、つながる前の状態では例外になって何もしない。閉じて作り直すと、ほかにつながりが無いときに
            // パイプが消え、Low の印と独占 (FirstPipeInstance) が外れる)。戻せなければ作り直す
            var connected = false;
            for (var failures = 0; ; failures++)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    connected = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    pipe.Dispose();
                    return;
                }
                catch (Exception ex)
                {
                    if (failures == 0) Log.Warn($"Meltype IME: パイプでつながりを待てませんでした (待ち直します): {ex.Message}");
                    if (!DisconnectNamedPipe(pipe.SafePipeHandle) || failures >= 5) break;
                    try { await Task.Delay(Math.Min(1000, 50 * (failures + 1)), _stop.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { pipe.Dispose(); return; }
                }
            }
            if (!connected)
            {
                if (recreated++ == 0) Log.Warn("Meltype IME: 待っているパイプを作り直します。");
                pipe.Dispose();
                first = recreating = true;
                // 同じ失敗が続いても空回りしないように
                try { await Task.Delay(500, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            recreating = false;
            recreateTries = 0;
            createRetries = 0;
            // つながりの数は、ここ (受け付けたとき) で増やす (Serve が動き出すのを待たずに、作り直しの判断に使えるように)
            if (Interlocked.Increment(ref _connections) > MaxConnections)
            {
                // つながりが多すぎる (アプリの数ではありえない): 断る
                Interlocked.Decrement(ref _connections);
                if (!_tooManyWarned) Log.Warn($"Meltype IME: つながりが多すぎる ({MaxConnections}) ので、新しいつながりを断りました。");
                _tooManyWarned = true;
                pipe.Dispose();
                // 断り続けて空回りしないように、少し待つ
                try { await Task.Delay(1000, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            if (Volatile.Read(ref _connections) < MaxConnections / 2) _tooManyWarned = false;
            _ = Task.Run(() => Serve(pipe));
        }
    }

    // 今つながっている数 (パイプを作り直すときに、自分のパイプが残っているかを見る)
    private int _connections;
    private bool _tooManyWarned;

    // 読めない要求のログ (続けて来ても、1 回目だけ詳しく書き、20 回で書くのをやめる)
    private int _requestErrors;

    // UI スレッド (Start を呼んだスレッド) の ID
    private uint _uiThread;

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafeHandle pipe, out uint processId);

    /// <summary>
    /// Meltype.exe 自身の UI スレッド (設定・はじめにの画面など) から来た要求か。
    /// そのとき UI スレッドは、DLL の中でこの応答を待って止まっているので、Invoke で UI スレッドに頼むとお互いに待ち合って固まる。
    /// 代わりにこのスレッドで処理する (UI スレッドは止まっているので、ほかの処理と同時には動かない)。
    /// </summary>
    private bool FromOwnUiThread(bool ownClient, string request)
    {
        if (!ownClient || _uiThread == 0) return false;
        try
        {
            using var document = JsonDocument.Parse(request);
            return document.RootElement.TryGetProperty("tid", out var tid) && tid.TryGetUInt32(out var id) && id == _uiThread;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task Serve(NamedPipeServerStream pipe)
    {
        // このつながりの入力の本体 (UI スレッドからだけ触る。Meltype.exe 自身の UI スレッドのつながりは、UI スレッドが止まっている間に触る)
        var sessions = new Dictionary<string, MeltypeSession>();
        var ownClient = GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientProcess) && clientProcess == (uint)Environment.ProcessId;
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (pipe.IsConnected && !_stop.IsCancellationRequested)
            {
                message.SetLength(0);
                do
                {
                    var read = await pipe.ReadAsync(buffer, _stop.Token).ConfigureAwait(false);
                    if (read == 0) return;
                    message.Write(buffer, 0, read);
                    if (message.Length > MaxRequestBytes) throw new IOException("要求が大きすぎます");
                }
                while (!pipe.IsMessageComplete);

                string reply;
                try
                {
                    var request = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    reply = FromOwnUiThread(ownClient, request) ? Handle(request, sessions) : Invoke(() => Handle(request, sessions));
                }
                catch (Exception ex)
                {
                    var count = Interlocked.Increment(ref _requestErrors);
                    if (count == 1) Log.Error($"Meltype IME: 要求を処理できませんでした: {ex}");
                    else if (count <= 20) Log.Error($"Meltype IME: 要求を処理できませんでした: {ex.Message}");
                    reply = Inactive;
                }
                var bytes = Encoding.UTF8.GetBytes(reply);
                await pipe.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
                await pipe.FlushAsync(_stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // アプリが終わった・DLL が待ちきれずに切った
        }
        finally
        {
            pipe.Dispose();
            Interlocked.Decrement(ref _connections);
            if (sessions.Count > 0 && !_stop.IsCancellationRequested)
            {
                try { Invoke(() => { sessions.Clear(); return ""; }); } catch { }
            }
        }
    }

    private string Invoke(Func<string> action)
    {
        if (!_invoker.IsHandleCreated || _invoker.IsDisposed) return Inactive;
        return (string)_invoker.Invoke(action);
    }

    private const string Inactive = "{\"active\":false,\"consumed\":false,\"commits\":[],\"view\":null}";

    /// <summary>Meltype IME として入力を受け持つか (Meltype が有効で、動作モードが Meltype IME)。</summary>
    private bool Active => _settings() is { Enabled: true, Mode: InputMode.Tsf };

    /// <summary>
    /// このアプリで入力を受け持つか。アプリ別設定で OFF にしたアプリと「ゲーム」のアプリでは、変換ボックスのときと同じく何もしない。
    /// process は DLL が送る、.exe を除いたプロセスの名前。
    /// </summary>
    private bool ActiveFor(string? process)
    {
        if (!Active) return false;
        if (string.IsNullOrEmpty(process)) return true;
        var settings = _settings();
        var name = process + ".exe";
        return settings.IsAppEnabled(name) && !settings.IsGame(name, looksLikeGame: false);
    }

    // つながってきたアプリ (ログに 1 回だけ書く)
    private readonly HashSet<string> _greeted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>同時につながる数の上限 (アプリのスレッドごとに 1 つなので、ふつうは数十まで)。</summary>
    private const int MaxConnections = 1024;

    /// <summary>ログに書くアプリの名前の数の上限。</summary>
    private const int MaxGreeted = 256;

    private string Handle(string request, Dictionary<string, MeltypeSession> sessions)
    {
        // 終わる途中 (変換エンジンなどを片付けた後) には何もしない
        if (_stop.IsCancellationRequested) return Inactive;
        using var document = JsonDocument.Parse(request);
        var root = document.RootElement;
        var op = root.GetProperty("op").GetString();
        var sid = root.TryGetProperty("sid", out var s) ? s.GetString() ?? "" : "";
        switch (op)
        {
            case "hello":
            {
                var process = String(root, "process") ?? "?";
                var greeting = ActiveFor(process);
                // Meltype IME のモードでないときも 2 秒ごとに問い合わせが来るので、ログはアプリごとに 1 回だけ。
                // 名前はアプリの側が送ってくるので、ログを崩さないように短くして、改行などは消す
                var shown = new string(process.Where(c => !char.IsControl(c) && !char.IsSurrogate(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).Take(64).ToArray());
                if (greeting && _greeted.Count < MaxGreeted && _greeted.Add(shown)) Log.Info($"Meltype IME: {shown} からつながりました。");
                if (!greeting) return Inactive;
                // コードエディター・ターミナルのアプリか (DLL は、キャレットが動くたびに入力モードの表示を問い合わせる)
                var codeApp = _settings().ProfileFor(process + ".exe") == AppProfile.Code;
                return $"{{\"active\":true,\"code\":{(codeApp ? "true" : "false")},\"consumed\":false,\"commits\":[],\"view\":null}}";
            }
            case "close":
                if (sessions.Remove(sid, out var closing) && closing.IsComposing) closing.CommitPending();
                return Inactive;
        }

        var active = ActiveFor(String(root, "process"));
        if (!sessions.TryGetValue(sid, out var session))
        {
            // DLL の側は変換中なのに、入力の続きが無い (つなぎ直した): 新しく始めると、変換中の文字を消したり置き換えたりするので、
            // 受け持たない (DLL は変換中の文字をそのまま確定し、キーはアプリに通す)
            if (!active || op is not ("key" or "toggle" or "mode") || IsTrue(root, "composing") || sessions.Count >= MaxSessionsPerConnection) return Inactive;
            session = _composition.CreateSession(_settings);
            sessions[sid] = session;
        }
        SessionResult result;
        switch (op)
        {
            case "key" when !active:
                // 途中で一時停止・モードを変えた: 変換中の内容は確定して、キーはアプリに通す
                if (!session.IsComposing) return Inactive;
                result = session.CommitPending() with { Consumed = false };
                break;
            case "key":
            {
                // 前に確定してから、キャレットが動いた (DLL がアプリに通したキー・クリック・別の入力欄): 前の語を確定し直さない
                if (IsTrue(root, "moved")) session.ForgetLastCommit();
                ApplyPassed(session, root);
                var vk = Int(root, "vk");
                var ch = Int(root, "ch");
                var mods = Int(root, "mods");
                var before = UpdateCodeInput(session, String(root, "process"), String(root, "before"));
                result = session.HandleKey(vk, ch is > 0 and < 0x10000 ? (char)ch : null,
                    (mods & 1) != 0, (mods & 2) != 0, (mods & 4) != 0, (mods & 8) != 0, before, String(root, "after"));
                break;
            }
            case "toggle":
                if (!active) return Inactive;
                ApplyPassed(session, root);
                result = new SessionResult(session.SetCodeLine(IsTrue(root, "japanese"), UpdateCodeInput(session, String(root, "process"), String(root, "before"))), [], null);
                break;
            case "mode":
            {
                if (!active) return Inactive;
                ApplyPassed(session, root);
                var english = session.CodeEnglishAt(UpdateCodeInput(session, String(root, "process"), String(root, "before")));
                return Reply(true, new SessionResult(false, [], null), session.CodeInput, english);
            }
            case "commit":
                // クリック・別の入力欄で確定した、アプリが確定した: 前の語を確定し直さない (確定する位置がキャレットと離れている)
                if (IsTrue(root, "moved")) session.ForgetLastCommit();
                result = session.CommitPending();
                break;
            case "select":
                result = session.SelectCandidate(Int(root, "index"));
                break;
            default:
                return Inactive;
        }
        return Reply(active, result, session.CodeInput, session.CodeEnglishAt(null));
    }

    private static string Reply(bool active, SessionResult result, bool code, bool english)
    {
        var json = result.ToJson();
        return $"{{\"active\":{(active ? "true" : "false")},\"code\":{(code ? "true" : "false")},\"english\":{(english ? "true" : "false")},{json[1..]}";
    }

    /// <summary>
    /// アプリの種類が「コード」で、フォーカスがコードエディター・ターミナルなら、コメント・文字列の外を英数のまま通す (Meltype キーボードと同じ判定)。
    /// README.md などの文章のファイルと、チャット・AI への入力欄 (UI Automation で調べた名前) は一般として扱う。
    /// DLL はキャレットの前を最大 4000 文字送ってくる (複数行のコメント・文字列を見分けるため)。コードでなければ、今までどおり 20 文字にして返す。
    /// </summary>
    private string? UpdateCodeInput(MeltypeSession session, string? process, string? before)
    {
        if (CodeFocusOf(process) is { } focus)
        {
            var code = focus != CodeFocus.None;
            if (session.CodeInput != code) session.CodeInput = code;
            session.CodeTerminal = focus == CodeFocus.Terminal;
        }
        return !session.CodeInput && before is { Length: > 20 } ? before[^20..] : before;
    }

    /// <summary>フォーカスのある所の種類。補完の一覧に移っただけなら null (今までの判定のまま)。</summary>
    private CodeFocus? CodeFocusOf(string? process)
    {
        if (string.IsNullOrEmpty(process)) return CodeFocus.None;
        var name = process + ".exe";
        if (_settings().ProfileFor(name) != AppProfile.Code || LineContext.IsDocumentTitle(KeyText.WindowTitle(Native.GetForegroundWindow()))) return CodeFocus.None;
        var info = _composition.Focus.Current;
        // 打つと開く補完の一覧 (VS Code など) では、UI Automation のフォーカスは一覧の行に移るが、打った文字はエディターに入る
        if (info.ClassName.Contains("monaco-list-row", StringComparison.Ordinal)) return null;
        return LineContext.ClassifyFocus(name, info.Name, info.ClassName);
    }

    /// <summary>DLL がアプリに通したキー (passed。-1 なら文字が変わっただけ) で、キャレットが別の場所に移ったかを伝える。</summary>
    private static void ApplyPassed(MeltypeSession session, JsonElement root)
    {
        if (root.TryGetProperty("passed", out var passed) && passed.TryGetInt32(out var vk) && vk >= 0) session.CaretMoved(vk, IsTrue(root, "ctrl"));
    }

    private static bool IsTrue(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public void Dispose()
    {
        // _stop は Dispose しない (受け付けの待ち・つながりの処理が、止まる途中でまだ Token を読むため)
        _stop.Cancel();
    }
}
