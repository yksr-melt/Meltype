// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Composition;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Learning;

namespace Meltype.Ime;

/// <summary>
/// 自己診断のうち Core の部品を確かめるもの。アプリの自己診断 (Windows の Meltype.exe --selftest) が、自分の画面などの確かめと並べて呼ぶ。
/// 配布用にランタイムを削るとき、ここで使った部品が読み込まれたものとして残る (Build-Package.ps1)。設定で ON にしたときだけ使う部品もここで一度使う。
/// </summary>
public static class SelfCheck
{
    /// <summary>確かめる名前と、確かめる処理 (結果の説明を返す。失敗は例外)。appDirectory は同梱の Mozc のヘルパーを置いたフォルダー。</summary>
    public static IReadOnlyList<(string Name, Func<string> Run)> Checks(string appDirectory) =>
    [
        ("設定", () => new Config.Settings().Clone().Normalize().Mode.ToString()),
        ("設定の保存と読み込み", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"meltype-selftest-{Guid.NewGuid():N}.json");
            try
            {
                new Config.Settings().Save(path);
                return Config.Settings.Load(path).Mode.ToString();
            }
            finally
            {
                File.Delete(path);
            }
        }),
        ("判定 (英数状態の検知)", () => ScoreEngine.CreateDefault(() => new Config.Settings()).Evaluate(new DetectionInput("konn", [], false)).Verdict.ToString()),
        ("変換ボックスの英語判定", () =>
        {
            // フロントと同じく ImeHost に打ったキーを送る (変換エンジンは使わない)
            var settings = new Config.Settings();
            var host = new ImeHost(new NoEnvironment(), new SessionParts(CompositionDetector.CreateDefault(), new NoConverter(), new CompositionServices()), () => settings);
            ImeOutput? output = null;
            foreach (var c in "kyouhagoogle") output = host.Handle(new ImeEvent.KeyPressed(new ImeKey { Ch = c }));
            return output?.Result.View?.Text ?? "(表示なし)";
        }),
        ("書き間違い辞書", () => MisspellingDictionary.Load(null).Find("ぶれすれっど")?.Right ?? "(見つからない)"),
        ("補助辞書", () => string.Join(",", CandidateDictionary.Load(null).Lookup("はし"))),
        ("文脈の手がかり辞書", () => ContextRules.Load(null).Choose("あつい", "気温", "厚い", _ => null) ?? "(なし)"),
        ("ユーザー辞書", () => new UserDictionary(null).Add("きごうとう", "記号等") ?? "登録できる"),
        ("変換エンジン (Mozc)", () =>
        {
            var directory = TemporaryDirectory();
            try
            {
                using var runtime = new MeltypeRuntime(directory);
                var converter = runtime.CreateConverter(appDirectory);
                if (!converter.MozcInstalled) return "(Mozc のヘルパーが無い)";
                // Zenzai (llama-server) は起動しない
                return $"{(converter.Convert(new ConversionRequest("きょうはいいてんき", null) { Zenzai = false }) is { } clauses ? string.Concat(clauses.Select(c => c.Text)) : "(変換できない)")} / 漢字 → {converter.Reading("漢字") ?? "(逆変換できない)"}";
            }
            finally
            {
                Delete(directory);
            }
        }),
        ("学習データ・ログの保存", () =>
        {
            var directory = TemporaryDirectory();
            try
            {
                new ConversionHistory(Path.Combine(directory, "conversions.json")).Remember("はしを", "箸を");
                Diagnostics.Log.SetFileOutput(Path.Combine(directory, "meltype.log"));
                Diagnostics.Log.Info("自己診断");
                Diagnostics.Log.SetFileOutput(null);
                return string.Join(",", Directory.GetFiles(directory).Select(Path.GetFileName));
            }
            finally
            {
                Delete(directory);
            }
        }),
        // Zenzai (llama-server と HttpClient で話す) と GitHub の Issue の題名の候補 (HTTPS) が使う部品。どちらも既定は OFF で、ほかの機能では読み込まれない。
        // 配布用にランタイムを削るとき、ここで読み込まないと「読み込まれなかった大きなアセンブリ」として消え、
        // ON にしたとたん、打つたびに例外になって日本語を打てなくなった (#973)。
        ("Zenzai (llama-server と HTTP)", () =>
        {
            // 本物と同じ ZenzaiConverter で、loopback の偽の llama-server に 1 回変換を頼む (POST・JSON の道を通す。外へはつながない)
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(() => AnswerOnce(listener, """{"content":"今日は"}"""));
            using var zenzai = new ZenzaiConverter(new Uri($"http://127.0.0.1:{port}/"), timeoutMs: 10000);
            var text = zenzai.Convert(new ConversionRequest("きょうは", null)) is [var clause] ? clause.Text : null;
            // 偽のサーバーの側で例外になったときは、その例外を出す (Wait が投げる)
            server.Wait(TimeSpan.FromSeconds(10));
            return text ?? throw new InvalidOperationException("偽の llama-server から答えを受け取れない");
        }),
        ("GitHub の題名 (HTTPS)", () =>
            // TLS (SslStream) と証明書の確かめ (X509Chain) の型に触って読み込むだけ。外へはつながないので、TLS の接続そのものは確かめていない
            $"{typeof(System.Net.Security.SslStream).Name}, {typeof(System.Security.Cryptography.X509Certificates.X509Chain).Name}"),
    ];

    /// <summary>設定・学習データを置く一時フォルダー (自己診断で、使っている人のデータに書かないように)。</summary>
    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), $"meltype-selftest-{Guid.NewGuid():N}");

    private static void Delete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>HTTP の要求を 1 つ受け取り (見出しと Content-Length の本文を読み切る)、JSON の本文で答える。</summary>
    private static void AnswerOnce(System.Net.Sockets.TcpListener listener, string json)
    {
        using var client = listener.AcceptTcpClient();
        using var stream = client.GetStream();
        var received = new List<byte>();
        var buffer = new byte[4096];
        int read;
        int headerEnd;
        while ((headerEnd = Encoding.ASCII.GetString([.. received]).IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0 && (read = stream.Read(buffer)) > 0) received.AddRange(buffer[..read]);
        if (headerEnd < 0) return;
        var header = Encoding.ASCII.GetString([.. received], 0, headerEnd);
        var length = header.Split("\r\n").Select(l => l.Split(':', 2)).FirstOrDefault(p => p.Length == 2 && p[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) is { } p ? int.Parse(p[1].Trim(), CultureInfo.InvariantCulture) : 0;
        while (received.Count < headerEnd + 4 + length && (read = stream.Read(buffer)) > 0) received.AddRange(buffer[..read]);
        var body = Encoding.UTF8.GetBytes(json);
        stream.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
        stream.Write(body);
    }

    private sealed class NoEnvironment : IImeEnvironment
    {
        public Config.InputMode? InputMethod => null;

        public SnippetStore Snippets { get; } = new(null);

        public string? ClipboardText => null;

        public string? OtherTipVersion => null;
    }

    private sealed class NoConverter : IKanjiConverter
    {
        public IReadOnlyList<ConversionClause>? Convert(ConversionRequest request) => null;
    }
}
