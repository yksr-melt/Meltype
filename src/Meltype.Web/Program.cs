// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi

using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meltype.Engine;
using Meltype.Ime;

namespace Meltype.Web;

/// <summary>
/// ブラウザーの入力欄 (web-engine/src/meltype.js) から呼ぶ関数。出来事は <see cref="ImeEvent"/> の JSON、答えは <see cref="ImeOutput"/> の JSON。
/// 関数の名前と分け方は、Mac・Linux・Android 版の C の関数 (src/Meltype.Core.CApi/Exports.cs) に合わせる。
/// 設定・学習データはブラウザーのメモリー上のファイル (<see cref="DataDirectory"/>) に置く。残すときはページの側が
/// <see cref="ExportData"/> で取り出して保存し、次に開いたときと、ほかのタブが保存したときに <see cref="ImportData"/> で戻す。
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class WebExports
{
    public static void Main()
    {
    }

    private static MeltypeRuntime? s_runtime;

    // 入力欄の本体。番号はページが持つ (ImeDestroy で消す)
    private static readonly Dictionary<int, ImeHost> s_hosts = [];
    private static int s_nextHost;

    private const string DataDirectory = "/meltype";

    private static MeltypeRuntime Runtime => s_runtime ??= new MeltypeRuntime(DataDirectory);

    /// <summary>Mozc の WebAssembly に要求 1 行を渡し、答えの 1 行を受け取る (失敗なら null)。</summary>
    [JSImport("mozcRequest", "meltype")]
    private static partial string? MozcRequest(string line);

    /// <summary>変換の結果が後から届いた (ページは入力欄に ConversionFinished の出来事を送る)。</summary>
    [JSImport("conversionReady", "meltype")]
    private static partial void ConversionReady();

    /// <summary>最初に 1 回呼ぶ。mozc が false なら漢字変換はせず、かなのまま確定する。</summary>
    [JSExport]
    public static void Init(bool mozc)
    {
        // 同梱のヘルパーも Zenzai も無い (appDirectory は無いフォルダー)。Mozc は JS の関数で答える
        Runtime.StartConverter("/nonexistent", mozc ? new CallbackMozcHelper() : null);
        Runtime.ConversionReady += ConversionReady;
    }

    /// <summary>入力欄 1 つの本体を作り、番号を返す。ImeDestroy で消す。</summary>
    [JSExport]
    public static int ImeCreate()
    {
        // 変換の結果はいつも後から受け取る (ブラウザーは 1 つのスレッドで動くので、キーの処理で答えを待てない)
        var host = s_nextHost++;
        s_hosts[host] = Runtime.CreateHost(new NativeEnvironment(), () => Runtime.Settings.Current, () => true);
        return host;
    }

    /// <summary>出来事 (<see cref="ImeEvent"/> の JSON) を処理して、<see cref="ImeOutput"/> の JSON を返す。</summary>
    [JSExport]
    public static string ImeHandle(int host, string json) => s_hosts[host].Handle(json);

    [JSExport]
    public static void ImeDestroy(int host) => s_hosts.Remove(host);

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

    /// <summary>
    /// <see cref="ExportData"/> で取り出したファイルを戻す。<see cref="Init"/> の前 (前に保存したもの) と、
    /// ほかのタブが保存したときに呼ぶ。動いている Core は、ほかのプロセスがファイルを書き換えたときと同じく読み直す。
    /// 1 秒ごとの確かめを待たずにすぐ読み直す (待つ間に確定すると、覚えたことを保存のときに捨てるため)。
    /// </summary>
    [JSExport]
    public static void ImportData(string json)
    {
        var root = Path.GetFullPath(DataDirectory) + Path.DirectorySeparatorChar;
        foreach (var (name, content) in JsonSerializer.Deserialize(json, WebJsonContext.Default.DictionaryStringString) ?? [])
        {
            var path = Path.GetFullPath(Path.Combine(DataDirectory, name));
            if (!path.StartsWith(root, StringComparison.Ordinal)) throw new ArgumentException($"保存先の外のファイルです: {name}");
            // 同じ中身は書かない (書くと、ファイルが変わったとみなして読み直す)
            if (File.Exists(path) && File.ReadAllText(path) == content) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        s_runtime?.Learning.Refresh();
    }

    /// <summary>学習データをすべて消す (ユーザー辞書と設定は消さない)。</summary>
    [JSExport]
    public static void ResetLearning() => Runtime.Learning.Reset();

    /// <summary>ブラウザーの環境。クリップボード・入力言語は読めない。</summary>
    private sealed class NativeEnvironment : IImeEnvironment
    {
        public Config.InputMode? InputMethod => null;

        public Dictionary.SnippetStore Snippets => Runtime.Snippets;

        public string? ClipboardText => null;

        public string? OtherTipVersion => null;
    }

    /// <summary>Mozc の WebAssembly (Android 版と同じく、要求 1 行を関数で答える)。動かすのはページなので、Start・Reset・Dispose では何もしない。</summary>
    private sealed class CallbackMozcHelper : IMozcHelper
    {
        public bool IsInstalled => true;

        // Mozc の WebAssembly はページを閉じるまで動き続ける (起動し直さない)
        public int Launches => 1;

        public long WorkingSet => 0;

        public bool Start() => true;

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
