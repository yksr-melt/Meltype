// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Meltype.NowPlaying;

/// <summary>
/// 今再生している曲 (曲名・アーティスト) を、Windows のメディアの操作
/// (Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager) から読む (issue #1400)。
/// キーを処理する UI スレッドでは待たない: 設定が ON の間は、セッション・再生状態・曲が変わったときの event を受け取って
/// 裏のスレッドで読み直し、Read はその結果をすぐ返す (「なう」を時間をあけて 1 回だけ変換しても、その場で曲が出るように)。
/// 曲が変わらない間は何も読まない (polling しない)。覚えない (今の 1 曲だけ持つ)・送らない。
///
/// WinRT の C# 投影が使えないので、WinRtCandidates と同じく COM の関数表 (vtable) を直接呼ぶ。
/// 番号は Windows.Foundation.UniversalApiContract.winmd の定義に合わせてある:
///   IGlobalSystemMediaTransportControlsSessionManagerStatics : { RequestAsync }                  (6)
///   IGlobalSystemMediaTransportControlsSessionManager        : { GetCurrentSession, GetSessions, add/remove_CurrentSessionChanged, add/remove_SessionsChanged } (6..11)
///   IGlobalSystemMediaTransportControlsSession               : { SourceAppUserModelId, TryGetMediaPropertiesAsync, GetTimelineProperties, GetPlaybackInfo } (6..9)、
///                                                              { add/remove_PlaybackInfoChanged, add/remove_MediaPropertiesChanged } (27..30)
///   IGlobalSystemMediaTransportControlsSessionMediaProperties: { Title, Subtitle, AlbumArtist, Artist } (6..9)
///   IGlobalSystemMediaTransportControlsSessionPlaybackInfo   : { Controls, PlaybackStatus }       (6, 7)
///   IVectorView&lt;T&gt;: { GetAt, Size } (6, 7)、IAsyncOperation&lt;T&gt;: { GetResults } (8)、IAsyncInfo: { Status (7), Cancel (9) }
/// event の受け手 (TypedEventHandler&lt;TSender, TArgs&gt;) は IUnknown + Invoke (3) の COM オブジェクトを自分で作る (EventHandlers)。
/// 使えない環境・再生していないときは null。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsMediaSession : INowPlayingSource
{
    private static readonly Guid IidStatics = new("2050c4ee-11a0-57de-aed7-c97c70338245");
    private static readonly Guid IidAsyncInfo = new("00000036-0000-0000-C000-000000000046");
    private const int Playing = 4;
    // 裏のスレッドで読むので、UI は待たない。読めないまま待ち続けないための上限
    private const int TimeoutMs = 2000;
    // 設定の ON・OFF を見る間隔 (設定を見るだけで、メディアのセッションには触らない)
    private const int SettingCheckMs = 30000;

    private readonly object _lock = new();
    private readonly object _subscriptionLock = new();
    private readonly Func<bool> _enabled;
    private readonly System.Threading.Timer _settingCheck;
    private IntPtr _manager;
    private volatile bool _unavailable;
    private (string Title, string Artist)? _last;
    private bool _reading;
    private bool _dirty;
    // event の登録 (登録を消すときの印)
    private bool _subscribed;
    private long _currentSessionToken;
    private long _sessionsToken;
    private readonly List<(IntPtr Session, long Playback, long Media)> _sessionTokens = [];

    // event の受け手から呼ぶ (Meltype の中で 1 つだけ)
    private static volatile WindowsMediaSession? s_instance;

    /// <param name="enabled">設定「なうで再生中の曲を候補に出す」。OFF の間は読まない・event も受け取らない。</param>
    public WindowsMediaSession(Func<bool> enabled)
    {
        _enabled = enabled;
        s_instance = this;
        _settingCheck = new System.Threading.Timer(_ => CheckSetting(), null, 0, SettingCheckMs);
    }

    /// <summary>今再生している曲 (再生中のものがなければ null) を、裏で読んだ結果からすぐ返す。待たない。</summary>
    public (string Title, string Artist)? Read()
    {
        if (_unavailable || !_enabled()) return null;
        lock (_lock) return _last;
    }

    // 設定が ON になったら event を受け取り始めて 1 回読む。OFF になったら受け取るのをやめ、持っている曲を消す
    private void CheckSetting()
    {
        if (_unavailable) return;
        var enabled = _enabled();
        lock (_subscriptionLock)
        {
            if (enabled == _subscribed) return;
            if (enabled)
            {
                Subscribe();
            }
            else
            {
                Unsubscribe();
                lock (_lock) _last = null;
                return;
            }
        }
        Refresh();
    }

    // event から呼ぶ。セッションが増えた・減ったら、セッションごとの event を受け取り直す
    private void Changed(bool sessions)
    {
        _ = Task.Run(() =>
        {
            if (sessions)
            {
                lock (_subscriptionLock)
                {
                    if (!_subscribed) return;
                    SubscribeSessions();
                }
            }
            Refresh();
        });
    }

    // 読み直す。読んでいる途中に変わったら、読み終わってからもう 1 回読む
    private void Refresh()
    {
        lock (_lock)
        {
            if (_reading)
            {
                _dirty = true;
                return;
            }
            _reading = true;
        }
        while (true)
        {
            var song = _enabled() ? ReadNow() : null;
            lock (_lock)
            {
                _last = song;
                if (!_dirty)
                {
                    _reading = false;
                    return;
                }
                _dirty = false;
            }
        }
    }

    // _subscriptionLock の中で呼ぶ
    private void Subscribe()
    {
        try
        {
            var manager = Manager();
            if (manager == IntPtr.Zero) return;
            long token;
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)VTable(manager, 8))(manager, EventHandlers.Manager, &token) >= 0) _currentSessionToken = token; // add_CurrentSessionChanged
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)VTable(manager, 10))(manager, EventHandlers.Manager, &token) >= 0) _sessionsToken = token; // add_SessionsChanged
            _subscribed = true;
            SubscribeSessions();
        }
        catch (Exception ex)
        {
            _unavailable = true;
            Diagnostics.Log.Warn($"再生中の曲の変化を受け取れません: {ex.Message}");
        }
    }

    // _subscriptionLock の中で呼ぶ。今のセッションすべての、再生状態と曲の変化を受け取る (前の登録は消す)
    private void SubscribeSessions()
    {
        UnsubscribeSessions();
        var manager = _manager;
        if (manager == IntPtr.Zero) return;
        IntPtr sessions;
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(manager, 7))(manager, &sessions) < 0 || sessions == IntPtr.Zero) return;
        try
        {
            uint size;
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)VTable(sessions, 7))(sessions, &size) < 0) return;
            for (uint i = 0; i < size && i < 16; i++)
            {
                IntPtr session;
                if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)VTable(sessions, 6))(sessions, i, &session) < 0 || session == IntPtr.Zero) continue;
                long playback = 0, media = 0;
                _ = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)VTable(session, 27))(session, EventHandlers.Session, &playback); // add_PlaybackInfoChanged
                _ = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)VTable(session, 29))(session, EventHandlers.Session, &media); // add_MediaPropertiesChanged
                _sessionTokens.Add((session, playback, media)); // 登録を消すまで session を持つ
            }
        }
        finally
        {
            Marshal.Release(sessions);
        }
    }

    // _subscriptionLock の中で呼ぶ
    private void UnsubscribeSessions()
    {
        foreach (var (session, playback, media) in _sessionTokens)
        {
            if (playback != 0) _ = ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)VTable(session, 28))(session, playback); // remove_PlaybackInfoChanged
            if (media != 0) _ = ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)VTable(session, 30))(session, media); // remove_MediaPropertiesChanged
            Marshal.Release(session);
        }
        _sessionTokens.Clear();
    }

    // _subscriptionLock の中で呼ぶ
    private void Unsubscribe()
    {
        UnsubscribeSessions();
        var manager = _manager;
        if (manager != IntPtr.Zero)
        {
            if (_currentSessionToken != 0) _ = ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)VTable(manager, 9))(manager, _currentSessionToken); // remove_CurrentSessionChanged
            if (_sessionsToken != 0) _ = ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)VTable(manager, 11))(manager, _sessionsToken); // remove_SessionsChanged
        }
        _currentSessionToken = _sessionsToken = 0;
        _subscribed = false;
    }

    /// <summary>
    /// event の受け手 (TypedEventHandler) の COM オブジェクト。IUnknown (0..2) + Invoke (3)。
    /// どのスレッドから呼ばれてもよいので IAgileObject も名乗る。Meltype が終わるまで使うので解放しない (AddRef・Release は数えない)。
    /// IID は TypedEventHandler の pinterface から求めたもの (Windows.Foundation の型の IID の決め方: RFC 4122 の v5)。
    /// </summary>
    private static class EventHandlers
    {
        private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
        private static readonly Guid IidAgile = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");
        // TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs / SessionsChangedEventArgs>
        private static readonly Guid IidCurrentSessionChanged = new("228bd0ed-1fa2-5e9b-a6ec-42566173103b");
        private static readonly Guid IidSessionsChanged = new("2e2a8630-dc8c-530a-9746-bc984d4b029e");
        // TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs / PlaybackInfoChangedEventArgs>
        private static readonly Guid IidMediaPropertiesChanged = new("0f2ce2b7-afa7-5ed0-8cb6-8c40cf9b3a5f");
        private static readonly Guid IidPlaybackInfoChanged = new("2bdf1426-d41f-5896-897f-efc0b0fa7392");

        /// <summary>セッションが増えた・減った・今のセッションが変わった。</summary>
        public static readonly IntPtr Manager = Create(1);
        /// <summary>再生状態・曲が変わった。</summary>
        public static readonly IntPtr Session = Create(0);

        private static IntPtr Create(int kind)
        {
            var vtable = (IntPtr*)NativeMemory.Alloc((nuint)(4 * sizeof(IntPtr)));
            vtable[0] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&QueryInterface;
            vtable[1] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&AddRef;
            vtable[2] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&Release;
            vtable[3] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)&Invoke;
            // { 関数表, 種類 }
            var instance = (IntPtr*)NativeMemory.Alloc((nuint)(2 * sizeof(IntPtr)));
            instance[0] = (IntPtr)vtable;
            instance[1] = kind;
            return (IntPtr)instance;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
        private static int QueryInterface(IntPtr self, Guid* iid, IntPtr* result)
        {
            var id = *iid;
            var manager = ((IntPtr*)self)[1] == 1;
            if (id == IidUnknown || id == IidAgile ||
                (manager ? id == IidCurrentSessionChanged || id == IidSessionsChanged : id == IidMediaPropertiesChanged || id == IidPlaybackInfoChanged))
            {
                *result = self;
                return 0;
            }
            *result = IntPtr.Zero;
            return unchecked((int)0x80004002); // E_NOINTERFACE
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
        private static uint AddRef(IntPtr self) => 1;

        [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
        private static uint Release(IntPtr self) => 1;

        [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
        private static int Invoke(IntPtr self, IntPtr sender, IntPtr args)
        {
            try
            {
                s_instance?.Changed(sessions: ((IntPtr*)self)[1] == 1);
            }
            catch
            {
                // Windows に例外を返さない
            }
            return 0;
        }
    }

    // 裏のスレッド (MTA) で呼ぶ。同時に 1 つだけ (_reading)
    private (string Title, string Artist)? ReadNow()
    {
        if (_unavailable) return null;
        try
        {
            var manager = Manager();
            if (manager == IntPtr.Zero) return null;
            IntPtr sessions;
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(manager, 7))(manager, &sessions) < 0 || sessions == IntPtr.Zero) return null;
            try
            {
                uint size;
                if (((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)VTable(sessions, 7))(sessions, &size) < 0) return null;
                // 再生しているセッションを優先する (複数あれば最初のもの)
                for (uint i = 0; i < size && i < 16; i++)
                {
                    IntPtr session;
                    if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)VTable(sessions, 6))(sessions, i, &session) < 0 || session == IntPtr.Zero) continue;
                    try
                    {
                        if (Status(session) != Playing) continue;
                        if (Properties(session) is { } song) return song;
                    }
                    finally
                    {
                        Marshal.Release(session);
                    }
                }
                return null;
            }
            finally
            {
                Marshal.Release(sessions);
            }
        }
        catch (Exception ex)
        {
            _unavailable = true;
            Diagnostics.Log.Warn($"再生中の曲を読めません: {ex.Message}");
            return null;
        }
    }

    private static int Status(IntPtr session)
    {
        IntPtr info;
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(session, 9))(session, &info) < 0 || info == IntPtr.Zero) return -1; // GetPlaybackInfo
        try
        {
            int status;
            return ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)VTable(info, 7))(info, &status) < 0 ? -1 : status; // get_PlaybackStatus
        }
        finally
        {
            Marshal.Release(info);
        }
    }

    private static (string Title, string Artist)? Properties(IntPtr session)
    {
        IntPtr operation;
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(session, 7))(session, &operation) < 0 || operation == IntPtr.Zero) return null; // TryGetMediaPropertiesAsync
        try
        {
            if (!Wait(operation)) return null;
            IntPtr properties;
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(operation, 8))(operation, &properties) < 0 || properties == IntPtr.Zero) return null; // GetResults
            try
            {
                var title = StringAt(properties, 6)?.Trim() ?? "";
                var artist = StringAt(properties, 9)?.Trim() ?? "";
                if (artist.Length == 0) artist = StringAt(properties, 8)?.Trim() ?? ""; // AlbumArtist
                return title.Length == 0 ? null : (title, artist);
            }
            finally
            {
                Marshal.Release(properties);
            }
        }
        finally
        {
            Marshal.Release(operation);
        }
    }

    private IntPtr Manager()
    {
        if (_manager != IntPtr.Zero) return _manager;
        var className = CreateString("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager");
        try
        {
            var iid = IidStatics;
            if (RoGetActivationFactory(className, ref iid, out var statics) < 0 || statics == IntPtr.Zero)
            {
                _unavailable = true;
                return IntPtr.Zero;
            }
            try
            {
                IntPtr operation;
                if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(statics, 6))(statics, &operation) < 0 || operation == IntPtr.Zero) return IntPtr.Zero; // RequestAsync
                try
                {
                    if (!Wait(operation)) return IntPtr.Zero;
                    IntPtr manager;
                    if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(operation, 8))(operation, &manager) < 0) return IntPtr.Zero;
                    _manager = manager;
                    return manager;
                }
                finally
                {
                    Marshal.Release(operation);
                }
            }
            finally
            {
                Marshal.Release(statics);
            }
        }
        finally
        {
            _ = WindowsDeleteString(className); // 解放の戻り値は常に S_OK。
        }
    }

    private static bool Wait(IntPtr operation)
    {
        var iid = IidAsyncInfo;
        if (Marshal.QueryInterface(operation, in iid, out var info) < 0) return false;
        try
        {
            var deadline = Environment.TickCount64 + TimeoutMs;
            while (true)
            {
                int status;
                if (((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)VTable(info, 7))(info, &status) < 0) return false; // get_Status
                if (status == 1) return true;
                if (status != 0) return false;
                if (Environment.TickCount64 > deadline)
                {
                    ((delegate* unmanaged[Stdcall]<IntPtr, int>)VTable(info, 9))(info); // Cancel
                    return false;
                }
                Thread.Sleep(1);
            }
        }
        finally
        {
            Marshal.Release(info);
        }
    }

    private static string? StringAt(IntPtr instance, int slot)
    {
        IntPtr handle;
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(instance, slot))(instance, &handle) < 0) return null;
        if (handle == IntPtr.Zero) return "";
        try
        {
            var buffer = WindowsGetStringRawBuffer(handle, out var length);
            return Marshal.PtrToStringUni(buffer, (int)length);
        }
        finally
        {
            _ = WindowsDeleteString(handle);
        }
    }

    private static IntPtr VTable(IntPtr instance, int slot) => (*(IntPtr**)instance)[slot];

    private static IntPtr CreateString(string text)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(text, (uint)text.Length, out var handle));
        return handle;
    }

    public void Dispose()
    {
        _unavailable = true;
        _settingCheck.Dispose();
        if (s_instance == this) s_instance = null;
        lock (_subscriptionLock)
        {
            try { if (_subscribed) Unsubscribe(); }
            catch (Exception ex) { Diagnostics.Log.Warn($"再生中の曲の event の登録を消せません: {ex.Message}"); }
        }
        lock (_lock) if (_reading) return; // 読んでいる途中なら、終了時の解放は OS に任せる
        if (_manager == IntPtr.Zero) return;
        Marshal.Release(_manager);
        _manager = IntPtr.Zero;
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, uint length, out IntPtr handle);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr handle);
    [DllImport("combase.dll")] private static extern IntPtr WindowsGetStringRawBuffer(IntPtr handle, out uint length);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
}
