// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi

using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Meltype.Engine;
using Meltype.Ime;

namespace Meltype.Web;

/// <summary>
/// ブラウザーの入力欄 (web-ime/meltype-web.js) から呼ぶ関数。出来事は <see cref="ImeEvent"/> の JSON、答えは <see cref="ImeOutput"/> の JSON。
/// 設定・学習データはブラウザーのメモリー上のファイル (/meltype) に置く。保存はページの側が IndexedDB に写す。
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class WebExports
{
    public static void Main()
    {
    }

    private static MeltypeRuntime? s_runtime;
    private static HybridConverter? s_converter;
    private static readonly List<ImeHost> s_hosts = [];

    private static MeltypeRuntime Runtime => s_runtime ??= new MeltypeRuntime("/meltype");

    /// <summary>Mozc の WebAssembly に要求 1 行を渡し、答えの 1 行を受け取る (失敗なら null)。</summary>
    [JSImport("mozcRequest", "meltype")]
    private static partial string? MozcRequest(string line);

    /// <summary>変換の結果が後から届いた (ページは入力欄に refresh の出来事を送る)。</summary>
    [JSImport("conversionReady", "meltype")]
    private static partial void ConversionReady();

    /// <summary>最初に 1 回呼ぶ。mozc が false なら漢字変換はせず、かなのまま確定する。</summary>
    [JSExport]
    public static void Init(bool mozc)
    {
        s_converter = mozc ? Runtime.CreateConverter(new JsMozcHelper()) : Runtime.CreateConverter("/nonexistent");
        s_converter.ResultReady += ConversionReady;
    }

    /// <summary>入力欄 1 つの本体を作り、番号を返す。</summary>
    [JSExport]
    public static int Create()
    {
        var converter = s_converter ?? throw new InvalidOperationException("Init を先に呼んでください");
        var parts = Runtime.SessionParts(converter, converter.Candidates, converter.Reading) with { LateResults = () => true };
        s_hosts.Add(new ImeHost(new WebEnvironment(), parts, () => Runtime.Settings.Current));
        return s_hosts.Count - 1;
    }

    [JSExport]
    public static string Handle(int host, string json) => s_hosts[host].Handle(json);

    [JSExport]
    public static string Version() => Config.ProjectInfo.CoreVersion;

    private sealed class WebEnvironment : IImeEnvironment
    {
        public Config.InputMode? InputMethod => null;

        public Dictionary.SnippetStore Snippets => Runtime.Snippets;

        public string? ClipboardText => null;

        public string? OtherTipVersion => null;
    }

    /// <summary>Mozc の WebAssembly。動かすのはページなので、Start・Reset・Dispose では何もしない。</summary>
    private sealed class JsMozcHelper : IMozcHelper
    {
        public bool IsInstalled => true;

        public long WorkingSet => 0;

        public void Start()
        {
        }

        public string? Send(string line) => MozcRequest(line);

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}
