// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Meltype.Detection;

/// <summary>
/// Windows のスペルチェッカー (英語, en-US) で、英単語として正しい綴りかを調べる。
/// 同梱の英語辞書 (english.txt) は「ローマ字としても読めてしまう語」などに絞った小さなものなので、
/// 普通の英単語 (meeting, name, tomorrow …) はこちらで補う。結果は語ごとに覚えておく。
/// 使えない環境 (英語のスペルチェッカーが無い) では常に false を返す。
/// </summary>
public sealed class WindowsSpellChecker : IWordChecker
{
    private static readonly Guid ClsidSpellCheckerFactory = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");

    private readonly ConcurrentDictionary<string, bool> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private ISpellChecker? _checker;
    private bool _unavailable;

    public static WindowsSpellChecker Shared { get; } = new();

    /// <summary>スペルチェッカーを使えるか (初回の呼び出しで作る)。</summary>
    public bool IsAvailable => Checker() is not null;

    public int MaxWordLength => 30;

    /// <summary>小文字の英単語 (a-z だけ) が、英語として正しい綴りか。</summary>
    public bool IsWord(string lower)
    {
        if (lower.Length < 2 || lower.Length > 30 || !lower.All(char.IsAsciiLetterLower)) return false;
        if (_cache.TryGetValue(lower, out var known)) return known;
        var result = Check(lower);
        if (_cache.Count > 20000) _cache.Clear();
        _cache[lower] = result;
        return result;
    }

    private readonly ConcurrentDictionary<string, string?> _corrections = new(StringComparer.Ordinal);

    /// <summary>
    /// Windows の自動修正の一覧にある打ち間違いなら、正しい綴り (teh → the、recieve → receive)。
    /// スペルチェッカーが「置き換える」と言うものだけで、候補が複数あるもの (提案だけのもの) は null。
    /// </summary>
    public string? AutoCorrection(string lower)
    {
        if (lower.Length < 2 || lower.Length > 30 || !lower.All(char.IsAsciiLetterLower)) return null;
        if (_corrections.TryGetValue(lower, out var known)) return known;
        string? result = null;
        lock (_gate)
        {
            var checker = Checker();
            if (checker is null) return null;
            try
            {
                if (checker.Check(lower, out var errors) >= 0 && errors is not null)
                {
                    try
                    {
                        if (errors.Next(out var item) == 0 && item is ISpellingError error)
                        {
                            // CORRECTIVE_ACTION_REPLACE = 2
                            if (error.GetCorrectiveAction(out var action) >= 0 && action == 2 && error.GetReplacement(out var text) >= 0 && text != IntPtr.Zero)
                            {
                                result = Marshal.PtrToStringUni(text);
                                Marshal.FreeCoTaskMem(text);
                            }
                            Marshal.ReleaseComObject(error);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(errors);
                    }
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"スペルチェッカーの自動修正でエラー ({ex.Message})");
            }
        }
        if (_corrections.Count > 20000) _corrections.Clear();
        _corrections[lower] = result;
        return result;
    }

    private bool Check(string word)
    {
        lock (_gate)
        {
            var checker = Checker();
            if (checker is null) return false;
            try
            {
                if (checker.Check(word, out var errors) < 0 || errors is null) return false;
                try
                {
                    // 誤りが 1 つも無ければ S_FALSE (1) が返る。
                    var hr = errors.Next(out var error);
                    if (error is not null) Marshal.ReleaseComObject(error);
                    return hr == 1;
                }
                finally
                {
                    Marshal.ReleaseComObject(errors);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"スペルチェッカーでエラー ({ex.Message})。以後は使いません。");
                _checker = null;
                _unavailable = true;
                return false;
            }
        }
    }

    private ISpellChecker? Checker()
    {
        if (_checker is not null || _unavailable) return _checker;
        lock (_gate)
        {
            if (_checker is not null || _unavailable) return _checker;
            try
            {
                var type = Type.GetTypeFromCLSID(ClsidSpellCheckerFactory, throwOnError: true)!;
                var factory = (ISpellCheckerFactory)Activator.CreateInstance(type)!;
                foreach (var tag in new[] { "en-US", "en-GB" })
                {
                    if (factory.IsSupported(tag, out var supported) >= 0 && supported != 0 &&
                        factory.CreateSpellChecker(tag, out var checker) >= 0 && checker is not null)
                    {
                        _checker = checker;
                        return checker;
                    }
                }
                Diagnostics.Log.Info("英語のスペルチェッカーが無いため、英単語の判定は同梱の辞書だけで行います。");
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Info($"スペルチェッカーを使えません ({ex.Message})。英単語の判定は同梱の辞書だけで行います。");
            }
            _unavailable = true;
            return null;
        }
    }

    // ---- COM (spellcheck.h) ----

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        [PreserveSig] int GetSupportedLanguages(out IntPtr value);
        [PreserveSig] int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);
        [PreserveSig] int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker? value);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        [PreserveSig] int GetLanguageTag(out IntPtr value);
        [PreserveSig] int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError? value);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [PreserveSig] int Next([MarshalAs(UnmanagedType.IUnknown)] out object? value);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
        [PreserveSig] int GetStartIndex(out uint value);
        [PreserveSig] int GetLength(out uint value);
        [PreserveSig] int GetCorrectiveAction(out int value);
        [PreserveSig] int GetReplacement(out IntPtr value);
    }
}
