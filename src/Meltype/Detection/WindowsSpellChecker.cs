// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
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

    private const int CacheLimit = 50000;
    private ConcurrentDictionary<string, Answer> _cache = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, Answer> _older = new(StringComparer.Ordinal);
    private int _cached;
    private readonly object _gate = new();
    private volatile ISpellChecker? _checker;
    private volatile bool _unavailable;
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _comThread;
    private Action? _job;
    private volatile bool _done;

    // 1 回の問い合わせで両方の答えが分かる (Known)。IsWord・AutoCorrection で聞いた側は Asked にする。
    // 聞けなかったときは聞いた側の答え (false・null) だけを覚え、スペルチェッカーが止まった後は Asked の答えだけを使う
    private enum Side : byte { Unknown, Known, Asked }

    private readonly record struct Answer(bool IsWord, string? Correction, Side Word, Side Fix)
    {
        public Side Of(bool forWord) => forWord ? Word : Fix;
        public static Answer Found(bool isWord, string? correction, bool forWord) => new(isWord, correction, forWord ? Side.Asked : Side.Known, forWord ? Side.Known : Side.Asked);
        public static Answer Failed(bool forWord) => new(false, null, forWord ? Side.Asked : Side.Unknown, forWord ? Side.Unknown : Side.Asked);

        // 聞いた答えは後から聞いたものを優先し (main は聞くたびに上書きする)、聞いた答えを「分かっただけ」の答えで変えない
        public Answer Merge(Answer later)
        {
            var word = later.Word == Side.Asked || (Word != Side.Asked && later.Word == Side.Known);
            var fix = later.Fix == Side.Asked || (Fix != Side.Asked && later.Fix == Side.Known);
            return new(word ? later.IsWord : IsWord, fix ? later.Correction : Correction, word ? later.Word : Word, fix ? later.Fix : Fix);
        }
    }

    public static WindowsSpellChecker Shared { get; } = new();

    /// <summary>スペルチェッカーを使えるか (初回の呼び出しで作る)。</summary>
    public bool IsAvailable
    {
        get
        {
            if (_checker is not null || _unavailable) return _checker is not null;
            lock (_gate) return OnComThread(Checker) is not null;
        }
    }

    /// <summary>小文字の英単語 (a-z だけ) が、英語として正しい綴りか。</summary>
    public bool IsWord(string lower) => Lookup(lower, forWord: true) is { IsWord: true };

    /// <summary>
    /// Windows の自動修正の一覧にある打ち間違いなら、正しい綴り (teh → the、recieve → receive)。
    /// スペルチェッカーが「置き換える」と言うものだけで、候補が複数あるもの (提案だけのもの) は null。
    /// </summary>
    public string? AutoCorrection(string lower) => Lookup(lower, forWord: false)?.Correction;

    // 1 回の問い合わせで両方の答えが分かるので、まとめて覚える (問い合わせは重い)
    private Answer? Lookup(string lower, bool forWord)
    {
        if (lower.Length < 2 || lower.Length > 30 || !lower.All(char.IsAsciiLetterLower)) return null;
        if (Usable(_cache, lower, forWord, out var known) || Usable(_older, lower, forWord, out known))
        {
            // 反対側は Unknown で渡し、その間に聞かれた新しい答えを読んだ時の答えで戻さない
            return known.Of(forWord) == Side.Asked && _cache.ContainsKey(lower) ? known : Remember(lower, known with
            {
                Word = forWord ? Side.Asked : Side.Unknown,
                Fix = forWord ? Side.Unknown : Side.Asked,
            });
        }
        lock (_gate)
        {
            var (answer, keep) = OnComThread(() => Ask(lower, forWord));
            return keep ? Remember(lower, answer!.Value) : answer;
        }
    }

    private bool Usable(ConcurrentDictionary<string, Answer> cache, string lower, bool forWord, out Answer answer) =>
        cache.TryGetValue(lower, out answer) && answer.Of(forWord) switch { Side.Asked => true, Side.Known => !_unavailable, _ => false };

    private (Answer? Answer, bool Keep) Ask(string lower, bool forWord)
    {
        var checker = Checker();
        if (checker is null) return (null, false);
        try
        {
            if (checker.Check(lower, out var errors) < 0 || errors is null) return (Answer.Failed(forWord), true);
            try
            {
                // 誤りが 1 つも無ければ S_FALSE (1) が返る。
                var hr = errors.Next(out var item);
                if (hr < 0)
                {
                    if (item is not null) Marshal.ReleaseComObject(item);
                    return (Answer.Failed(forWord), true);
                }
                string? correction = null;
                var fixRead = true;
                if (hr == 0 && item is ISpellingError error)
                {
                    // 直し方の読み取りの失敗では止めない (main の IsWord はここを通らない)
                    try
                    {
                        // CORRECTIVE_ACTION_REPLACE = 2
                        if (error.GetCorrectiveAction(out var action) < 0) fixRead = false;
                        else if (action == 2)
                        {
                            if (error.GetReplacement(out var text) < 0) fixRead = false;
                            else if (text != IntPtr.Zero)
                            {
                                correction = Marshal.PtrToStringUni(text);
                                Marshal.FreeCoTaskMem(text);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log.Warn($"スペルチェッカーの自動修正でエラー ({ex.Message})");
                        (fixRead, correction) = (false, null);
                    }
                    Marshal.ReleaseComObject(error);
                }
                else if (item is not null) Marshal.ReleaseComObject(item);
                var answer = Answer.Found(hr == 1, correction, forWord);
                // 直し方を読めなかったときは、IsWord で聞いたなら自動修正の答えは分からないままにする
                return (fixRead || !forWord ? answer : answer with { Fix = Side.Unknown }, true);
            }
            finally
            {
                Marshal.ReleaseComObject(errors);
            }
        }
        catch (Exception ex)
        {
            if (!forWord)
            {
                Diagnostics.Log.Warn($"スペルチェッカーの自動修正でエラー ({ex.Message})");
                return (Answer.Failed(forWord), true);
            }
            Diagnostics.Log.Warn($"スペルチェッカーでエラー ({ex.Message})。以後は使いません。");
            _checker = null;
            _unavailable = true;
            return (null, false);
        }
    }

    // STA のスレッドから聞くと 1 回が数倍重い (Windows で 240µs と 36µs) ので、MTA の専用のスレッドで聞く。
    // 呼ぶ側は _gate を持ったまま待つ。STA で止まって待つとメッセージを処理してしまうので、回って待つ。
    private T OnComThread<T>(Func<T> work)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) return work();
        _comThread ??= StartComThread();
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        _job = () =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        };
        _done = false;
        _wake.Set();
        var spin = new SpinWait();
        while (!_done) spin.SpinOnce(sleep1Threshold: -1);
        failure?.Throw();
        return result;
    }

    private Thread StartComThread()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                _wake.WaitOne();
                _job!();
                _done = true;
            }
        }) { IsBackground = true, Name = "Meltype spell checker" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return thread;
    }

    private Answer Remember(string lower, Answer answer)
    {
        var added = false;
        var merged = _cache.AddOrUpdate(lower, _ =>
        {
            added = true;
            return _older.TryGetValue(lower, out var older) ? older.Merge(answer) : answer;
        }, (_, earlier) => earlier.Merge(answer));
        if (added && Interlocked.Increment(ref _cached) > CacheLimit)
        {
            lock (_gate)
            {
                if (_cached > CacheLimit)
                {
                    _older = _cache;
                    _cache = new ConcurrentDictionary<string, Answer>(StringComparer.Ordinal);
                    _cached = 0;
                }
            }
        }
        return merged;
    }

    // _gate を持って、OnComThread の中で呼ぶ
    private ISpellChecker? Checker()
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
