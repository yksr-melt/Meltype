// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype.Composition;

/// <summary>
/// Windows に入っている Microsoft IME の変換エンジンを借りて変換する。Meltype 自身は辞書を持たない。
///   全体の変換・文節の区切り: IFELanguage (ProgID "MSIME.Japan") の GetConversion / GetJMorphResult
///   (同じ読みの別の漢字の一覧を返す API は、TSF 版 Microsoft IME では入力欄にフォーカスがないと使えないため使わない)
/// COM オブジェクトなので、作成したスレッド (UI スレッド, STA) からだけ使う。
/// </summary>
public sealed class MsImeKanjiConverter : IKanjiConverter, IDisposable
{
    private const uint FELANG_REQ_CONV = 0x00010000;
    private const uint FELANG_CMODE_NOINVISIBLECHAR = 0x40000000;
    private const uint FELANG_CLMN_FIXR = 0x00000010;

    private IFELanguage? _language;
    private bool _unavailable;

    public bool IsAvailable => !_unavailable;

    /// <summary>
    /// 1 回に渡せる読みの長さ (文脈の文字を含む)。これより長いと GetJMorphResult が E_LARGEINPUT を返して変換できない (issue #221)。
    /// 上限は資料に無いが、100 文字を超えると失敗するのを確かめている。
    /// </summary>
    internal const int MaxInputLength = 100;

    public string? Convert(string hiragana)
    {
        if (string.IsNullOrEmpty(hiragana)) return null;
        if (hiragana.Length > MaxInputLength)
        {
            // 長い読みは区切って変換し、つなげる (変換できなかった部分は読みのまま)
            return string.Concat(SplitForEngine(hiragana, MaxInputLength).Select(part => Convert(part) ?? part));
        }
        var language = Open();
        if (language is null) return null;
        try
        {
            // start は 1 始まり、length -1 で全体。
            var hr = language.GetConversion(hiragana, 1, -1, out var result);
            return hr >= 0 && !string.IsNullOrEmpty(result) ? result : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"漢字変換に失敗しました: {ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
    {
        if (string.IsNullOrEmpty(hiragana)) return null;
        if ((context?.Length ?? 0) + hiragana.Length > MaxInputLength)
        {
            // 長い読み (未確定のまま 100 文字以上: issue #221) は、句読点・助詞の後ろで区切って変換し、文節をつなげる。
            // 2 つ目からは、前の部分の変換結果の終わりを文脈にする
            var clauses = new List<ConversionClause>();
            foreach (var part in SplitForEngine(hiragana, MaxInputLength - MaxContextLength))
            {
                var previous = clauses.Count > 0 ? string.Concat(clauses.Select(c => c.Text)) : context ?? "";
                var partContext = previous.Length > MaxContextLength ? previous[^MaxContextLength..] : previous;
                clauses.AddRange(ConvertClauses(part, partContext) ?? [new ConversionClause(part, Convert(part) ?? part)]);
            }
            return clauses;
        }
        // 文脈付きで区切れなければ (エンジンが文脈の文字を読み直したなど)、文脈なしでやり直す。
        if (!string.IsNullOrEmpty(context) && Morph(hiragana, context) is { } withContext) return withContext;
        return Morph(hiragana, null);
    }

    /// <summary>長い読みを区切るときに、前の部分から文脈として渡す文字数。</summary>
    private const int MaxContextLength = 10;

    /// <summary>
    /// 読みを max 文字以下の部分に区切る。なるべく文の切れ目 (。！？ の後ろ)、次に読点 (、) の後ろ、次に助詞らしいかな
    /// (は が を に で と も へ や の) の後ろで区切り、見つからなければ max 文字で切る。
    /// </summary>
    internal static List<string> SplitForEngine(string text, int max)
    {
        var parts = new List<string>();
        var start = 0;
        while (text.Length - start > max)
        {
            var cut = Find("。．！？!?") ?? Find("、，,") ?? Find("はがをにでともへやの") ?? start + max;
            parts.Add(text[start..cut]);
            start = cut;
        }
        parts.Add(text[start..]);
        return parts;

        // start から max 文字の中で、いちばん後ろの区切りの文字の直後 (前半が短くなりすぎないよう、半分より後ろだけ)
        int? Find(string marks)
        {
            for (var i = start + max; i > start + max / 2; i--)
                if (marks.Contains(text[i - 1])) return i;
            return null;
        }
    }

    /// <summary>
    /// 文脈 (直前の確定済みの文字) を、読みを固定した文字 (FELANG_CLMN_FIXR) として読みの前に付けて渡す。
    /// エンジンはその文字を変換し直さず、続く読みの変換の手がかりにする (この本は + あつい → 厚い)。
    /// </summary>
    private List<ConversionClause>? Morph(string hiragana, string? context)
    {
        var language = Open();
        if (language is null) return null;
        context ??= "";
        var input = context + hiragana;
        var info = IntPtr.Zero;
        var result = IntPtr.Zero;
        try
        {
            if (context.Length > 0)
            {
                info = Marshal.AllocHGlobal(input.Length * 4);
                for (var i = 0; i < input.Length; i++) Marshal.WriteInt32(info, i * 4, i < context.Length ? (int)FELANG_CLMN_FIXR : 0);
            }
            var hr = language.GetJMorphResult(FELANG_REQ_CONV, FELANG_CMODE_NOINVISIBLECHAR, input.Length, input, info, out result);
            if (hr < 0 || result == IntPtr.Zero) return null;
            return ParseClauses(result, input, context.Length);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"文節の解析に失敗しました: {ex.Message}");
            return null;
        }
        finally
        {
            if (info != IntPtr.Zero) Marshal.FreeHGlobal(info);
            if (result != IntPtr.Zero) Marshal.FreeCoTaskMem(result);
        }
    }

    /// <summary>調査用: 前の文字列を「表示を固定した文字」として渡し、読みを文脈付きで変換した結果全体を返す。</summary>
    internal string? ProbeWithContext(string before, string reading, uint flag)
    {
        var language = Open();
        if (language is null) return null;
        var input = before + reading;
        var info = Marshal.AllocHGlobal(input.Length * 4);
        var result = IntPtr.Zero;
        try
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteInt32(info, i * 4, i < before.Length ? (int)flag : 0);
            var hr = language.GetJMorphResult(FELANG_REQ_CONV, FELANG_CMODE_NOINVISIBLECHAR, input.Length, input, info, out result);
            if (hr < 0 || result == IntPtr.Zero) return $"(失敗 0x{hr:X8})";
            var morph = Marshal.PtrToStructure<MORRSLT>(result);
            var output = Marshal.PtrToStringUni(morph.pwchOutput, morph.cchOutput);
            var morphReading = morph.pwchRead != IntPtr.Zero ? Marshal.PtrToStringUni(morph.pwchRead, morph.cchRead) : "(null)";
            var words = new List<string>();
            for (var i = 0; i < morph.cWDD; i++)
            {
                var w = Marshal.PtrToStructure<WDD>(morph.pWDD + i * Marshal.SizeOf<WDD>());
                words.Add($"[r{w.wReadPos}+{w.cchRead} d{w.wDispPos}+{w.cchDisp} p{w.nPos:X}]");
            }
            return $"{output} (読み={morphReading}) {string.Join("", words)}";
        }
        finally
        {
            Marshal.FreeHGlobal(info);
            if (result != IntPtr.Zero) Marshal.FreeCoTaskMem(result);
        }
    }

    /// <summary>
    /// 漢字かな交じりの文字列の読み (ひらがな) を変換エンジンで求める (逆変換)。辞書を作るとき (絵文字の名前の読み) に使う。
    /// </summary>
    internal string? Reading(string text)
    {
        const uint FELANG_REQ_REV = 0x00030000;
        var language = Open();
        if (language is null) return null;
        var hr = language.GetJMorphResult(FELANG_REQ_REV, FELANG_CMODE_NOINVISIBLECHAR, text.Length, text, IntPtr.Zero, out var pointer);
        if (hr < 0 || pointer == IntPtr.Zero) return null;
        try
        {
            var result = Marshal.PtrToStructure<MORRSLT>(pointer);
            if (result.pwchOutput == IntPtr.Zero || result.cchOutput <= 0 || result.cchOutput > text.Length * 8) return null;
            return Marshal.PtrToStringUni(result.pwchOutput, result.cchOutput);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>調査用: 形態素解析の結果を単語ごとに文字列にする。</summary>
    internal string DescribeMorph(string hiragana)
    {
        var language = Open();
        if (language is null) return "(使えない)";
        var hr = language.GetJMorphResult(FELANG_REQ_CONV, FELANG_CMODE_NOINVISIBLECHAR, hiragana.Length, hiragana, IntPtr.Zero, out var pointer);
        if (hr < 0 || pointer == IntPtr.Zero) return $"(失敗 0x{hr:X8})";
        try
        {
            var result = Marshal.PtrToStructure<MORRSLT>(pointer);
            var output = Marshal.PtrToStringUni(result.pwchOutput, result.cchOutput);
            var size = Marshal.SizeOf<WDD>();
            var words = new List<string>();
            for (var i = 0; i < result.cWDD; i++)
            {
                var w = Marshal.PtrToStructure<WDD>(result.pWDD + i * size);
                words.Add($"{output.Substring(w.wDispPos, Math.Min(w.cchDisp, output.Length - w.wDispPos))}(read {w.wReadPos}+{w.cchRead} pos=0x{w.nPos:X} flags=0x{w.flags:X} r1=0x{w.WDD_nReserve1:X})");
            }
            return $"size={result.dwSize} " + string.Join(" ", words);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>
    /// MORRSLT の単語 (WDD) を文節にまとめる。文節の先頭フラグ (fPhrase) はすべての単語に立っていて使えないので、
    /// 品詞番号 nPos で判断する: 内容語 (nPos ≠ 0) から新しい文節を始め、助詞・助動詞・活用語尾 (nPos = 0) は
    /// 前の文節にくっつける (今日|は|いい|天気|です → 今日は | いい | 天気です)。
    /// </summary>
    private static List<ConversionClause>? ParseClauses(IntPtr resultPointer, string input, int contextLength)
    {
        var result = Marshal.PtrToStructure<MORRSLT>(resultPointer);
        // 構造体の解釈を誤っていたときに不正なメモリを読まないよう、値の範囲を確かめてから使う。
        if (result.pwchOutput == IntPtr.Zero || result.pWDD == IntPtr.Zero || result.cWDD <= 0 || result.cWDD > input.Length * 2 ||
            result.cchOutput == 0 || result.cchOutput > input.Length * 4 || result.cchRead > input.Length * 4 ||
            result.dwSize < Marshal.SizeOf<MORRSLT>() || result.dwSize > 1 << 20)
        {
            return null;
        }
        var output = Marshal.PtrToStringUni(result.pwchOutput, result.cchOutput);
        var reading = result.pwchRead != IntPtr.Zero ? Marshal.PtrToStringUni(result.pwchRead, result.cchRead) : input;
        // エンジンは文脈の文字 (この本は) を読み (このほんは) に直して返し、位置もその読みで数える。
        // 読みの末尾が変換したいかなと一致していることを確かめ、文脈部分の長さを読みの側で数え直す。
        var hiragana = input[contextLength..];
        if (!reading.EndsWith(hiragana, StringComparison.Ordinal)) return null;
        contextLength = reading.Length - hiragana.Length;
        var size = Marshal.SizeOf<WDD>();

        var clauses = new List<(int ReadStart, int ReadEnd, int DispStart, int DispEnd)>();
        for (var i = 0; i < result.cWDD; i++)
        {
            var word = Marshal.PtrToStructure<WDD>(result.pWDD + i * size);
            var readEnd = word.wReadPos + word.cchRead;
            var dispEnd = word.wDispPos + word.cchDisp;
            // 文脈として渡した部分の単語は結果に含めない。文脈と読みにまたがる単語があれば、区切りが信用できないので諦める。
            if (readEnd <= contextLength) continue;
            if (word.wReadPos < contextLength) return null;
            var isPhraseStart = word.nPos != 0;
            if (clauses.Count == 0 || isPhraseStart) clauses.Add((word.wReadPos, readEnd, word.wDispPos, dispEnd));
            else
            {
                var last = clauses[^1];
                clauses[^1] = (last.ReadStart, Math.Max(last.ReadEnd, readEnd), last.DispStart, Math.Max(last.DispEnd, dispEnd));
            }
        }

        // 読みの位置が入力と食い違う (エンジンが読みを書き換えた) 場合は、文節変換は諦めて全体変換に任せる。
        if (clauses.Count == 0 || clauses[^1].ReadEnd != reading.Length || clauses[0].ReadStart != contextLength) return null;
        var list = new List<ConversionClause>();
        for (var i = 0; i < clauses.Count; i++)
        {
            var (readStart, readEnd, dispStart, dispEnd) = clauses[i];
            if (readEnd > reading.Length || dispEnd > output.Length || readEnd <= readStart) return null;
            list.Add(new ConversionClause(reading[readStart..readEnd], output[dispStart..dispEnd]));
        }
        return list;
    }

    private IFELanguage? Open()
    {
        if (_language is not null) return _language;
        if (_unavailable) return null;
        try
        {
            var type = Type.GetTypeFromProgID("MSIME.Japan", throwOnError: true)!;
            var language = (IFELanguage)Activator.CreateInstance(type)!;
            var hr = language.Open();
            if (hr < 0) throw new COMException("IFELanguage.Open が失敗しました。", hr);
            _language = language;
            return language;
        }
        catch (Exception ex)
        {
            _unavailable = true;
            Diagnostics.Log.Warn($"Microsoft IME の変換エンジン (MSIME.Japan) を使えません。変換候補はひらがな・カタカナ・英字だけになります: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_language is null) return;
        try
        {
            _language.Close();
            Marshal.ReleaseComObject(_language);
        }
        catch
        {
        }
        _language = null;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)] // msime.h は 1 バイト境界でパックしている
    private struct MORRSLT
    {
        public uint dwSize;
        public IntPtr pwchOutput;
        public ushort cchOutput;
        public IntPtr pwchRead;
        public ushort cchRead;
        public IntPtr pchInputPos;
        public IntPtr pchOutputIdxWDD;
        public IntPtr pchReadIdxWDD;
        public IntPtr paMonoRubyPos;
        public IntPtr pWDD;
        public int cWDD;
        public IntPtr pPrivate;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)] // msime.h は 1 バイト境界でパックしている
    private struct WDD
    {
        public ushort wDispPos;
        public ushort wReadPos;
        public ushort cchDisp;
        public ushort cchRead;
        public uint WDD_nReserve1;
        public ushort nPos;
        public ushort flags; // bit0: fPhrase (文節の先頭)
        public IntPtr pReserved;
    }

    [ComImport, Guid("019F7152-E6DB-11d0-83C3-00C04FDDB82E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFELanguage
    {
        [PreserveSig] int Open();
        [PreserveSig] int Close();
        [PreserveSig] int GetJMorphResult(uint request, uint conversionMode, int length, [MarshalAs(UnmanagedType.LPWStr)] string input, IntPtr info, out IntPtr result);
        [PreserveSig] int GetConversionModeCaps(out uint caps);
        [PreserveSig] int GetPhonetic([MarshalAs(UnmanagedType.BStr)] string text, int start, int length, [MarshalAs(UnmanagedType.BStr)] out string result);
        [PreserveSig] int GetConversion([MarshalAs(UnmanagedType.BStr)] string text, int start, int length, [MarshalAs(UnmanagedType.BStr)] out string result);
    }
}
