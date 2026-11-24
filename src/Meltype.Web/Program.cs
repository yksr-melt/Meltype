// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meltype.Engine;
using Meltype.Ime;

namespace Meltype.Web;

/// <summary>
/// ブラウザーの入力欄 (web-ime/meltype-web.js・web-engine/src/meltype.js) から呼ぶ関数。出来事は <see cref="ImeEvent"/> の JSON、答えは <see cref="ImeOutput"/> の JSON。
/// 設定・学習データはブラウザーのメモリー上のファイル (<see cref="DataDirectory"/>) に置く。残すときはページの側が
/// <see cref="ExportData"/> で取り出して IndexedDB に保存し、次に開いたときに <see cref="ImportData"/> で戻す。
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

    private const string DataDirectory = "/meltype";

    private static MeltypeRuntime Runtime => s_runtime ??= new MeltypeRuntime(DataDirectory);

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

    /// <summary>
    /// 設定・学習データのファイルを {"相対パス": "中身"} の JSON で返す。ログは入れない。
    /// Core はファイルを少し遅れて書くので、ページは確定のたびではなく、間を空けて呼ぶ。
    /// </summary>
    [JSExport]
    public static string ExportData()
    {
        var files = new Dictionary<string, string>();
        if (Directory.Exists(DataDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(DataDirectory, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith(".log", StringComparison.Ordinal)) continue;
                files[Path.GetRelativePath(DataDirectory, path)] = File.ReadAllText(path);
            }
        }
        return JsonSerializer.Serialize(files, WebJsonContext.Default.DictionaryStringString);
    }

    /// <summary><see cref="ExportData"/> で取り出したファイルを戻す。<see cref="Init"/> の前に呼ぶ (Core は作るときにファイルを読む)。</summary>
    [JSExport]
    public static void ImportData(string json)
    {
        if (s_runtime is not null) throw new InvalidOperationException("ImportData は Init の前に呼んでください");
        var root = Path.GetFullPath(DataDirectory) + Path.DirectorySeparatorChar;
        foreach (var (name, content) in JsonSerializer.Deserialize(json, WebJsonContext.Default.DictionaryStringString) ?? [])
        {
            var path = Path.GetFullPath(Path.Combine(DataDirectory, name));
            if (!path.StartsWith(root, StringComparison.Ordinal)) throw new ArgumentException($"保存先の外のファイルです: {name}");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

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

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class WebJsonContext : JsonSerializerContext;
