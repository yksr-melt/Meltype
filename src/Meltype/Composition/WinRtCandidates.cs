// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype.Composition;

/// <summary>
/// Windows 標準の変換候補 API (Windows.Data.Text.TextConversionGenerator, 言語 "ja") で、
/// 読みに対する変換候補の一覧 (はし → 橋 箸 端 …) を取る。
///
/// この PC には WinRT の C# 投影 (Windows SDK) が無く NuGet も使えないので、COM の関数表 (vtable) を直接呼ぶ。
/// 呼ぶ順番は Windows.Data.winmd の定義に合わせてある:
///   ITextConversionGeneratorFactory : IInspectable { Create }                               (6)
///   ITextConversionGenerator        : IInspectable { ResolvedLanguage, LanguageAvailableButNotInstalled,
///                                                    GetCandidatesAsync(input), GetCandidatesAsync(input, max) } (6..9)
///   IAsyncOperation&lt;T&gt;             : IInspectable { put_Completed, get_Completed, GetResults }   (6..8)
///   IAsyncInfo                      : IInspectable { Id, Status, ErrorCode, Cancel, Close }     (6..10)
///   IVectorView&lt;HSTRING&gt;           : IInspectable { GetAt, Size, IndexOf, GetMany }            (6..9)
/// 使えない環境では空の一覧を返すだけにする。
/// </summary>
public sealed unsafe class WinRtCandidates : IDisposable
{
    private static readonly Guid IidFactory = new("fcaa3781-3083-49ab-be15-56dfbbb74d6f");
    private static readonly Guid IidAsyncInfo = new("00000036-0000-0000-C000-000000000046");
    private const int TimeoutMs = 500;

    private IntPtr _generator;
    private bool _unavailable;

    public bool IsAvailable => !_unavailable;

    /// <summary>読み (ひらがな) の変換候補。多い順。取れなければ空。</summary>
    public IReadOnlyList<string> Get(string reading, int max = 20)
    {
        if (string.IsNullOrEmpty(reading) || _unavailable) return [];
        try
        {
            var generator = Generator();
            if (generator == IntPtr.Zero) return [];

            var input = CreateString(reading);
            IntPtr operation;
            try
            {
                // GetCandidatesAsync(HSTRING input, UInt32 maxCandidates, out IAsyncOperation) は 9 番目。
                var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr*, int>)VTable(generator, 9))(generator, input, (uint)max, &operation);
                if (hr < 0 || operation == IntPtr.Zero) return [];
            }
            finally
            {
                _ = WindowsDeleteString(input); // 解放の戻り値は常に S_OK。
            }

            try
            {
                if (!Wait(operation)) return [];
                IntPtr vector;
                var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(operation, 8))(operation, &vector); // GetResults
                if (hr < 0 || vector == IntPtr.Zero) return [];
                try
                {
                    uint size;
                    ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)VTable(vector, 7))(vector, &size); // get_Size
                    var result = new List<string>((int)Math.Min(size, 100));
                    for (uint i = 0; i < size && i < 100; i++)
                    {
                        IntPtr item;
                        if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)VTable(vector, 6))(vector, i, &item) < 0) continue; // GetAt
                        var text = ReadString(item);
                        _ = WindowsDeleteString(item); // 解放の戻り値は常に S_OK。
                        if (!string.IsNullOrEmpty(text) && !result.Contains(text)) result.Add(text);
                    }
                    return result;
                }
                finally
                {
                    Marshal.Release(vector);
                }
            }
            finally
            {
                Marshal.Release(operation);
            }
        }
        catch (Exception ex)
        {
            _unavailable = true;
            Diagnostics.Log.Warn($"Windows の変換候補 API を使えません: {ex.Message}");
            return [];
        }
    }

    private IntPtr Generator()
    {
        if (_generator != IntPtr.Zero) return _generator;
        var className = CreateString("Windows.Data.Text.TextConversionGenerator");
        try
        {
            var iid = IidFactory;
            if (RoGetActivationFactory(className, ref iid, out var factory) < 0 || factory == IntPtr.Zero)
            {
                _unavailable = true;
                Diagnostics.Log.Warn("Windows の変換候補 API (TextConversionGenerator) が見つかりません。");
                return IntPtr.Zero;
            }
            try
            {
                var language = CreateString("ja");
                try
                {
                    IntPtr generator;
                    var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)VTable(factory, 6))(factory, language, &generator); // Create
                    if (hr < 0 || generator == IntPtr.Zero)
                    {
                        _unavailable = true;
                        return IntPtr.Zero;
                    }
                    byte notInstalled;
                    ((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)VTable(generator, 7))(generator, &notInstalled);
                    if (notInstalled != 0) Diagnostics.Log.Warn("日本語の変換候補データがインストールされていません (Windows の言語設定で日本語を追加してください)。");
                    _generator = generator;
                    return generator;
                }
                finally
                {
                    _ = WindowsDeleteString(language); // 解放の戻り値は常に S_OK。
                }
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            _ = WindowsDeleteString(className); // 解放の戻り値は常に S_OK。
        }
    }

    /// <summary>非同期処理の完了を待つ (UI スレッドで呼ぶので、長くても TimeoutMs で諦める)。</summary>
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
                if (status == 1) return true;   // Completed
                if (status != 0) return false;  // Canceled / Error
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

    private static IntPtr VTable(IntPtr instance, int slot) => (*(IntPtr**)instance)[slot];

    private static IntPtr CreateString(string text)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(text, (uint)text.Length, out var handle));
        return handle;
    }

    private static string? ReadString(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return "";
        var buffer = WindowsGetStringRawBuffer(handle, out var length);
        return Marshal.PtrToStringUni(buffer, (int)length);
    }

    public void Dispose()
    {
        if (_generator == IntPtr.Zero) return;
        Marshal.Release(_generator);
        _generator = IntPtr.Zero;
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, uint length, out IntPtr handle);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr handle);
    [DllImport("combase.dll")] private static extern IntPtr WindowsGetStringRawBuffer(IntPtr handle, out uint length);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
}
