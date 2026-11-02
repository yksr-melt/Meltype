// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype.Composition;

/// <summary>
/// 今再生している曲 (曲名・アーティスト) を、Windows のメディアの操作
/// (Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager) から読む (issue #1400)。
/// 「なう」の変換の候補に出すときだけ、その場で読む。覚えない・送らない。
///
/// WinRT の C# 投影が使えないので、WinRtCandidates と同じく COM の関数表 (vtable) を直接呼ぶ。
/// 番号は Windows.Foundation.UniversalApiContract.winmd の定義に合わせてある:
///   IGlobalSystemMediaTransportControlsSessionManagerStatics : { RequestAsync }                  (6)
///   IGlobalSystemMediaTransportControlsSessionManager        : { GetCurrentSession, GetSessions } (6, 7)
///   IGlobalSystemMediaTransportControlsSession               : { SourceAppUserModelId, TryGetMediaPropertiesAsync, GetTimelineProperties, GetPlaybackInfo } (6..9)
///   IGlobalSystemMediaTransportControlsSessionMediaProperties: { Title, Subtitle, AlbumArtist, Artist } (6..9)
///   IGlobalSystemMediaTransportControlsSessionPlaybackInfo   : { Controls, PlaybackStatus }       (6, 7)
///   IVectorView&lt;T&gt;: { GetAt, Size } (6, 7)、IAsyncOperation&lt;T&gt;: { GetResults } (8)、IAsyncInfo: { Status (7), Cancel (9) }
/// 使えない環境・再生していないときは null。
/// </summary>
public sealed unsafe class NowPlaying : IDisposable
{
    private static readonly Guid IidStatics = new("2050c4ee-11a0-57de-aed7-c97c70338245");
    private static readonly Guid IidAsyncInfo = new("00000036-0000-0000-C000-000000000046");
    private const int Playing = 4;
    // キーを打っている UI スレッドで読むので、長くても待たない
    private const int TimeoutMs = 250;

    private IntPtr _manager;
    private bool _unavailable;

    /// <summary>今再生している曲。再生中のものがなければ、一時停止中のものも見ない (null)。</summary>
    public (string Title, string Artist)? Read()
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
        if (_manager == IntPtr.Zero) return;
        Marshal.Release(_manager);
        _manager = IntPtr.Zero;
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, uint length, out IntPtr handle);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr handle);
    [DllImport("combase.dll")] private static extern IntPtr WindowsGetStringRawBuffer(IntPtr handle, out uint length);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
}
