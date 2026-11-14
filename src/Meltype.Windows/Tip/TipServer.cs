// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
// Copyright (C) 2026 aster-void

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Diagnostics;
using Meltype.Ime;
using Meltype.Input;

namespace Meltype.Tip;

/// <summary>
/// Meltype IME (TSF の DLL, native/tip) とつなぐ名前付きパイプのサーバー。
/// DLL はアプリのプロセスに読み込まれるので、入力の本体 (辞書・学習・変換) はここ (Meltype.exe) に 1 つだけ置き、
/// DLL からは出来事 (<see cref="ImeEvent"/>) を 1 つずつ受け取って <see cref="ImeHost"/> の応答 (<see cref="ImeOutput"/>) を返す。
/// やり取りは 1 メッセージ 1 JSON (UTF-8、<see cref="TipRequest"/>)。入力の本体はつながり (パイプ) ごとの ImeHost に持ち、すべて UI スレッドで動かす
/// (つなぎ直した DLL が古い入力の続きを拾わないように。ほかのアプリの入力を読めないように)。
///
/// ほかのプロセスが同じ名前のパイプを先に作ると、DLL はそちらにつなごうとする。パイプの名前はセッションをまたいで共通で、名前の SID も
/// ほかのユーザーから分かるので、同じ PC のほかのユーザーのプロセスも先に作れる。同じユーザーのプロセスなら打鍵を横取りできてしまう。
/// ほかのユーザーのプロセスなら DLL がつながないので打鍵は渡らないが、このユーザーは Meltype IME を使えなくなる。最初のパイプは
/// FirstPipeInstance で作り、先に作られていたら始めない (ログに書く)。Meltype.exe より高い権限で動くアプリ (管理者として動くアプリなど) とサインインの画面には、
/// DLL の側でつながない (Meltype.exe の権限より上の入力を扱わない)。
/// </summary>
internal sealed class TipServer : IDisposable
{
    private readonly Control _invoker;
    private readonly CompositionService _composition;
    private readonly Func<IImeEnvironment, ImeHost> _createHost;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>1 つの要求の大きさの上限 (キー 1 つ分なら 1KB もない)。</summary>
    private const int MaxRequestBytes = 64 * 1024;

    /// <param name="createHost">つながり 1 つの入力の本体を作る (設定は本体に渡す。TipServer は読まない)。</param>
    public TipServer(Control invoker, CompositionService composition, Func<IImeEnvironment, ImeHost> createHost)
    {
        _invoker = invoker;
        _composition = composition;
        _createHost = createHost;
        composition.ConversionReady += _conversion.Notify;
    }

    private readonly ConversionSignal _conversion = new();

    /// <summary>
    /// wait の要求を返さずに持つ時間。DLL が IME のスレッドを止めて終わりを待つことがある (終わるとき) ので短くする。
    /// DLL は入力中だけ、返ったらすぐ次の wait を送る。
    /// </summary>
    private const int ConversionWaitMs = 250;

    /// <summary>Meltype IME が登録されていないと確かに分かるか (レジストリを読めないときは false)。</summary>
    public static bool IsKnownUnregistered => Registration() == false;
    /// <summary>Meltype IME (native/tip の DLL) が Windows に登録されているか。</summary>
    public static bool IsRegistered => Registration() == true;

    /// <summary>
    /// 動作モードに Meltype IME を選べるか (ARM64 の Windows でなく、登録されていて、登録済みの impl がこの Meltype.exe と同じビルドで、
    /// このユーザーのキーボードの一覧にある)。
    /// </summary>
    public static bool IsUsable =>
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.Arm64 &&
        InstalledBuild() == true && IsInUserLanguageList;

    /// <summary>
    /// 登録されているが、登録済みの impl がこの Meltype.exe と別ビルドか。Meltype IME の再登録 (UAC) をせずに Meltype.exe だけを更新すると、こうなる
    /// (setup.exe のサイレント実行・前に UAC を断ったあとの自動更新・Meltype IME の再登録後に失敗して前の版に戻した更新)。
    /// </summary>
    public static bool IsOtherBuildRegistered => InstalledBuild() == false;

    // 登録済みの impl がこの Meltype.exe と同じビルドか。登録されていなければ null
    private static bool? InstalledBuild() =>
        RegisteredStub() is { Length: > 0 } stub && Path.GetDirectoryName(Path.GetDirectoryName(stub)) is { } folder ? TipBuild.Shipped.IsInstalled(folder) : null;

    // 登録されているか。レジストリを読めなければ null。
    // TIP のキーだけ残り、DLL の登録 (InprocServer32) や DLL が無いことがある (セキュリティソフトが登録を途中で止めたとき)。
    // そのときは登録されていないとみなす (Meltype キーボードで動かす)
    private static bool? Registration() => RegisteredStub() is { } stub ? stub.Length > 0 : null;

    // 登録済みの stub (MeltypeTip.dll) のパス。登録されていなければ空、レジストリを読めなければ null
    private static string? RegisteredStub()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\CTF\TIP\{417D801B-A9BD-4C26-BD16-356A825A6998}");
            if (key is null) return "";
            using var server = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\CLSID\{417D801B-A9BD-4C26-BD16-356A825A6998}\InprocServer32");
            return server?.GetValue(null) is string dll && File.Exists(dll) ? dll : "";
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

    private static PipeSecurity CreateSecurity()
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
    /// 応答に時間がかかった要求をログに残す。DLL はキーの応答を 800ms まで待ち、来なければ打った文字のまま入れるので、
    /// その原因を調べられるように、UI スレッドが空くのを待った時間と処理の時間を分けて書く。打った文字は書かない。
    /// 遅いことが続いてもほかのログを押し出さないように、書くのは 20 回まで。
    /// </summary>
    private void LogSlow(TipRequest request, long waitedMs, long handledMs)
    {
        if (waitedMs + handledMs < SlowRequestMs || Interlocked.Increment(ref _slowRequests) > 20) return;
        Log.Warn($"Meltype IME: 要求 ({request.Type}) の応答に {waitedMs + handledMs}ms かかりました (UI スレッドが空くのを待った時間 {waitedMs}ms・処理 {handledMs}ms)。");
    }

    private const int SlowRequestMs = 50;
    private int _slowRequests;

    private async Task Serve(NamedPipeServerStream pipe)
    {
        var ownClient = GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientProcess) && clientProcess == (uint)Environment.ProcessId;
        // このつながりの入力の本体 (UI スレッドからだけ触る。Meltype.exe 自身の UI スレッドのつながりは、UI スレッドが止まっている間に触る)
        var host = ownClient ? Connect(null, 0) : Connect(ProcessName(clientProcess), clientProcess);
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        // この接続で、別ビルドの impl に初めて応答した時刻 (0 = まだ)
        long staleSince = 0;
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
                    var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    if (!_build.Matches(json))
                    {
                        // stub は、この応答を受けて impl を差し替え、新しい impl は別の接続でつなぐ。
                        // 登録済みの impl が同じビルドになってからも、同じ接続から別ビルドの要求が届き続けるなら、差し替えられなかった
                        // (登録済みの impl の方が別ビルドの間は、アプリを再起動しても変わらないので数えない)
                        var now = Environment.TickCount64;
                        if (InstalledBuild() != true)
                        {
                            staleSince = 0;
                        }
                        else if (staleSince == 0)
                        {
                            staleSince = now;
                        }
                        else if (now - staleSince >= StaleNoticeMs)
                        {
                            staleSince = now;
                            if (host.Process is { } process) ReportOutdated(process);
                        }
                        reply = Stale;
                    }
                    else if (TipRequest.Parse(json) is not { } request)
                    {
                        reply = Inactive;
                    }
                    else if (request.WaitSince is { } since)
                    {
                        reply = $"{{\"generation\":{await _conversion.WaitAsync(since, ConversionWaitMs, _stop.Token).ConfigureAwait(false)}}}";
                    }
                    else
                    {
                        var started = Environment.TickCount64;
                        long began = started, ended = started;
                        string Run()
                        {
                            began = Environment.TickCount64;
                            try { return Handle(host, request); }
                            finally { ended = Environment.TickCount64; }
                        }
                        // Meltype.exe 自身の UI スレッドから来た要求: UI スレッドは DLL の中でこの応答を待って止まっているので、
                        // UI スレッドに頼むとお互いに待ち合って固まる。代わりにこのスレッドで処理する (ほかの処理と同時には動かない)
                        reply = ownClient && _uiThread != 0 && request.Thread == _uiThread ? Run() : await InvokeAsync(Run).ConfigureAwait(false);
                        LogSlow(request, began - started, ended - began);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
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
            if (host.Host.SessionCount > 0 && !_stop.IsCancellationRequested)
            {
                try { await InvokeAsync(() => { host.Host.CloseAll(); return ""; }).ConfigureAwait(false); } catch { }
            }
        }
    }

    /// <summary>プロセスの名前 (.exe なし)。調べられなければ null。</summary>
    private static string? ProcessName(uint processId)
    {
        if (processId == 0) return null;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 1 つのつながりの入力の本体を作る。process は .exe を除いたプロセスの名前 (Meltype.exe 自身なら null)、processId はアプリ別の判定に使う
    /// (調べないなら 0)。DLL は自分のプロセスの名前を送らないので、つないできたプロセスから調べる。
    /// </summary>
    internal TipConnection Connect(string? process, uint processId) => new(this, process, processId);

    /// <summary>
    /// 出来事を ImeHost で処理する。DLL が読めない入力欄の事実 (アプリ・ウィンドウのタイトル・UI Automation の要素・ゲームらしさ) は、ここで足す。
    /// 明示的にモードが変わったら、DLL が送ってきた位置に入力モードを一瞬表示する。
    /// </summary>
    internal string Handle(TipConnection connection, TipRequest request)
    {
        // 終わる途中 (変換エンジンなどを片付けた後) には何もしない
        if (_stop.IsCancellationRequested) return Inactive;
        var e = request.Event;
        if (e.Field is { } field) e = e with { Field = connection.WithFacts(field) };
        var output = connection.Host.Handle(e);
        if (request.Thread != 0) _altKeys[request.Thread] = output.State.HandlesInput && output.State.Capture.AltKeys;
        if (output.State.HandlesInput && connection.Process is { } process) Greet(process);
        if (output.State.ModeIndicatorMs > 0 && request.Thread != 0)
            _composition.ShowMode(output.State.LanguageMode == ImeLanguageMode.Japanese, output.State.ModeIndicatorMs, request.Thread, request.Anchor);
        return output.ToJson();
    }

    // ---- 左右の Alt (ImeCapture.AltKeys) ----
    // TSF のキーのシンクには Alt の押下・解放 (WM_SYSKEYDOWN・WM_SYSKEYUP) が届かないので、Meltype.exe の低レベルフックで見て、
    // DLL の入力スレッドのメッセージ用のウィンドウに知らせる (DLL が本体に送り、答えを入力欄に反映する)。単独押しかは本体が決める

    // DLL の入力スレッド → 最後の状態で、左右の Alt を受け取るか
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, bool> _altKeys = new();
    // ここから下はフックのスレッドだけが使う
    private readonly HashSet<int> _held = [];
    private readonly KeyInjector _injector = new();
    private int _alt;
    private IntPtr _altWindow;
    private bool _altInterrupted;
    // 最後に Alt を本体に送らなかった理由 (ログ)
    private string? _altSkipped;

    /// <summary>DLL の TextService::kAltKey。wParam は仮想キーコード、lParam は AltFlags。</summary>
    private const uint AltKeyMessage = 0x8000 + 2;

    [Flags]
    private enum AltFlags
    {
        Shift = 1, Ctrl = 2, Command = 8, Released = 0x100, Interrupted = 0x200,
    }

    /// <summary>Meltype IME のときの低レベルフックのキー (true ならアプリに渡さない)。foreground は前面のアプリ。</summary>
    public bool OnKey(KeyEvent e, Input.AppInfo foreground)
    {
        var alt = e.Vk is VirtualKeys.LMenu or VirtualKeys.RMenu;
        if (e.IsDown)
        {
            var repeat = !_held.Add(e.PhysicalId);
            if (_alt != 0 && e.Vk != _alt) _altInterrupted = true;
            if (!alt || repeat || _alt != 0) return false;
            var thread = Native.GetWindowThreadProcessId(foreground.Window, out _);
            var window = MessageWindow(thread);
            var skipped = !_altKeys.TryGetValue(thread, out var wanted) ? $"前面のウィンドウのスレッド {thread} から、Meltype IME の要求が来ていない"
                : !wanted ? "本体が受け持っていないか、設定「左 Alt で英数 / 右 Alt で日本語」が OFF"
                : !ForegroundTracker.CanSendInputTo(foreground) ? "管理者として動くアプリには、止めた解放を送り直せない"
                : window is null ? $"スレッド {thread} に Meltype IME のメッセージ用のウィンドウが無い"
                : null;
            if (skipped is not null || window is not { } target)
            {
                // 同じ理由を Alt を押すたびに書かない
                if (skipped != _altSkipped) Log.Info($"左右の Alt を本体に送りません ({foreground.ProcessName}): {skipped}");
                _altSkipped = skipped;
                return false;
            }
            _altSkipped = null;
            _alt = e.Vk;
            _altWindow = target;
            var flags = (IsDown(VirtualKeys.Shift) ? AltFlags.Shift : 0) | (IsDown(VirtualKeys.Control) ? AltFlags.Ctrl : 0) |
                (IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin) ? AltFlags.Command : 0);
            _altInterrupted = flags != 0;
            Native.PostMessageW(target, AltKeyMessage, e.Vk, (int)flags);
            return false;
        }
        _held.Remove(e.PhysicalId);
        if (!alt || e.Vk != _alt) return false;
        _alt = 0;
        Native.PostMessageW(_altWindow, AltKeyMessage, e.Vk, (int)(AltFlags.Released | (_altInterrupted ? AltFlags.Interrupted : 0)));
        // 本体の答えは DLL が受け取るので待てない。単独押しでないとここで分かる (ほかのキー・修飾キー・マウスのボタンと一緒) なら、解放をそのまま通す。
        // それ以外は何もしないキー (0xE8) のあとに送り直す。
        // 入力モードの切り替えより先に解放がアプリに届いても、DLL への知らせはアプリの入力より先に処理される (送ったメッセージは入力より先)
        return !_altInterrupted && _injector.Inject(Keyboard.KeyboardFront.MaskedAltRelease(e));
    }

    /// <summary>Meltype IME のときの低レベルフックのマウスのボタン。</summary>
    public void OnMouseButton(MouseButtonEvent e)
    {
        if (_alt != 0 && e.Message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN) _altInterrupted = true;
    }

    /// <summary>DLL がそのスレッドに作ったメッセージ用のウィンドウ (TextService::CreateMessageWindow)。</summary>
    private static IntPtr? MessageWindow(uint thread)
    {
        for (var window = Native.FindWindowExW(Native.HWND_MESSAGE, IntPtr.Zero, "Meltype.Tip.Messages", null); window != IntPtr.Zero;
             window = Native.FindWindowExW(Native.HWND_MESSAGE, window, "Meltype.Tip.Messages", null))
        {
            if (Native.GetWindowThreadProcessId(window, out _) == thread) return window;
        }
        return null;
    }

    private static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    // UI スレッドの処理が終わるまでプールのスレッドをふさがない。Mozc や Zenzai の出力の読み取りもプールのスレッドで動くので、
    // アプリの数だけ同期で待つと空きがなくなり、Mozc の応答を読めなくなる (#1506)
    private async Task<string> InvokeAsync(Func<string> action)
    {
        if (!_invoker.IsHandleCreated || _invoker.IsDisposed) return Inactive;
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _invoker.BeginInvoke(() =>
        {
            try { done.SetResult(action()); }
            catch (Exception ex) { done.SetException(ex); }
        });
        // 実行される前にウィンドウが破棄されると done は完了しないので、終了の合図でも抜ける
        return await done.Task.WaitAsync(_stop.Token).ConfigureAwait(false);
    }

    /// <summary>パースできない要求への応答。DLL はキーを消費しない。</summary>
    private const string Inactive = "{\"active\":false,\"consumed\":false,\"commits\":[],\"view\":null}";

    /// <summary>
    /// 別ビルドの impl への応答。impl はキーを消費せず、stub に差し替えを要求する (native/tip の kStubImplStale)。
    /// stub と impl に分割する前の DLL (更新前から動いているプロセスに残る) も、キーを消費しない。
    /// </summary>
    private const string Stale = "{\"stale\":true}";

    /// <summary>stub による impl の差し替えを待つ時間。これを過ぎても同じ接続から別ビルドの要求が届いたら、<see cref="ClientOutdated"/> で通知する。</summary>
    private const int StaleNoticeMs = 3000;

    private readonly TipBuild _build = TipBuild.Shipped;

    /// <summary>
    /// アプリ (.exe を除いたプロセスの名前) を再起動するまで、そのアプリでは Meltype IME で入力できない
    /// (stub と impl に分割する前の DLL か、今の impl をロードできない古い stub が、プロセスに残っている)。
    /// UI スレッドで呼ぶ。同じアプリについて、接続ごとに <see cref="StaleNoticeMs"/> おきに呼ぶ。
    /// </summary>
    public event Action<string>? ClientOutdated;

    private void ReportOutdated(string process)
    {
        process = Shown(process);
        if (_invoker.IsHandleCreated && !_invoker.IsDisposed) _invoker.BeginInvoke(() => ClientOutdated?.Invoke(process));
    }

    /// <summary>同時につながる数の上限 (アプリのスレッドごとに 1 つなので、ふつうは数十まで)。</summary>
    private const int MaxConnections = 1024;

    // つながってきたアプリ (ログに 1 回だけ書く)
    private readonly HashSet<string> _greeted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ログに書くアプリの名前の数の上限。</summary>
    private const int MaxGreeted = 256;

    /// <summary>
    /// 1 つのつながり (= 1 つのアプリ) の入力の本体と環境。process は .exe を除いたプロセスの名前 (Meltype.exe 自身なら null)。
    /// </summary>
    internal sealed class TipConnection : IImeEnvironment
    {
        private readonly TipServer _server;
        private readonly uint _processId;
        // つながったときにはまだウィンドウが無いことがあるので、Java のアプリ (Minecraft など) は要求のたびに前面のウィンドウを見る
        private readonly bool _java;
        private bool _looksLikeGame;

        public TipConnection(TipServer server, string? process, uint processId)
        {
            _server = server;
            _processId = processId;
            Process = process;
            _java = processId != 0 && ForegroundTracker.IsJava(processId);
            _looksLikeGame = processId != 0 && ForegroundTracker.LooksLikeGame(processId);
            Host = server._createHost(this);
        }

        public ImeHost Host { get; }

        public string? Process { get; }

        /// <summary>DLL が読めない事実を足す。</summary>
        public ImeField WithFacts(ImeField field)
        {
            if (_java && !_looksLikeGame) _looksLikeGame = ForegroundTracker.IsForegroundJavaGame(_processId);
            var focus = _server._composition.Focus.Current;
            return field with
            {
                App = Process is null ? null : Process + ".exe",
                Title = KeyText.WindowTitle(Native.GetForegroundWindow()),
                Element = new ImeElement(focus.Name, focus.ClassName),
                Traits = _looksLikeGame ? [.. field.Traits, ImeTrait.Game] : field.Traits,
            };
        }

        public InputMode? InputMethod => InputMode.Tsf;

        public MeltypeSession CreateSession(Func<Settings> settings) => _server._composition.CreateSession(settings, directModeDetection: true);

        public SnippetStore Snippets => _server._composition.Snippets;

        public string? ClipboardText => CompositionService.ClipboardText;

    }

    // Meltype IME のモードでないときも DLL はつなぐので、ログはアプリごとに 1 回だけ
    private void Greet(string process)
    {
        var shown = Shown(process);
        if (_greeted.Count < MaxGreeted && _greeted.Add(shown)) Log.Info($"Meltype IME: {shown} からつながりました。");
    }

    // 名前はアプリの側から来るので、ログと通知を崩さないように短くして、改行などは消す
    private static string Shown(string process) =>
        new(process.Where(c => !char.IsControl(c) && !char.IsSurrogate(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).Take(64).ToArray());

    public void Dispose()
    {
        // _stop は Dispose しない (受け付けの待ち・つながりの処理が、止まる途中でまだ Token を読むため)
        _stop.Cancel();
    }
}

/// <summary>
/// パイプの 1 メッセージ。{"thread":…,"x":…,"y":…,"event":{ImeEvent}} か、変換の結果を待つ {"wait":世代}。
/// thread は DLL の入力スレッド、x / y は入力モードを表示する画面の位置 (分からなければ省く)。どちらも Meltype.exe の側で使い、本体には渡さない。
/// </summary>
internal sealed record TipRequest(uint Thread, Point? Anchor, ImeEvent Event, long? WaitSince)
{
    public string Type => WaitSince is null ? Event.Type.ToString() : "wait";

    /// <summary>読めない・形の違う要求なら null。</summary>
    public static TipRequest? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("wait", out var wait)) return wait.TryGetInt64(out var since) ? new(0, null, Waiting, since) : null;
            if (!root.TryGetProperty("event", out var body) || ImeHost.Parse(body.GetRawText()) is not { } e) return null;
            // 数でない値 (null・文字列) は TryGet… が例外を投げるので、先に種類を見る
            static bool Number(JsonElement root, string name, out JsonElement value) =>
                root.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Number;
            var thread = Number(root, "thread", out var t) && t.TryGetUInt32(out var id) ? id : 0;
            var anchor = Number(root, "x", out var x) && x.TryGetInt32(out var left) && Number(root, "y", out var y) && y.TryGetInt32(out var bottom)
                ? new Point(left, bottom) : (Point?)null;
            return new(thread, anchor, e, null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly ImeEvent Waiting = new() { Version = ImeHost.Version, Type = ImeEventType.StateRequested };
}
