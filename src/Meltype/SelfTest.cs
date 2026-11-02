// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Text;
using Meltype.Composition;
using Meltype.Detection;

namespace Meltype;

/// <summary>
/// 自己診断 (Meltype.exe --selftest 結果ファイル)。キーボードフックは掛けずに、主な機能が動くかだけを確かめる。
/// 配布用パッケージでは .NET ランタイムから使わない部品を削っているので、削りすぎていないかを作成時に確かめるのに使う。
/// </summary>
internal static class SelfTest
{
    public static int Run(string? reportPath)
    {
        var report = new StringBuilder();
        var failed = 0;
        void Check(string name, Func<string> action)
        {
            try
            {
                var result = action();
                report.AppendLine(CultureInfo.InvariantCulture, $"OK   {name}: {result}");
            }
            catch (Exception ex)
            {
                failed++;
                report.AppendLine(CultureInfo.InvariantCulture, $"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        ApplicationConfiguration.Initialize();
        Check("バージョン", () => AppInfo.Version);
        Check("ポータブル版のデータ保存先", () =>
        {
            if (!Config.AppPaths.IsPortable) return "portable marker が無い通常版";
            Directory.CreateDirectory(Config.AppPaths.DataDirectory);
            return Config.AppPaths.DataDirectory;
        });
        Check("設定", () => new Config.Settings().Clone().Normalize().Mode.ToString());
        Check("設定の保存と読み込み", () =>
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
        });
        Check("判定 (英数状態の検知)", () => ScoreEngine.CreateDefault(() => new Config.Settings())
            .Evaluate(new DetectionInput("konn", [], false)).Verdict.ToString());
        var detector = CompositionDetector.CreateDefault();
        Check("変換ボックスの英語判定", () =>
        {
            var text = new CompositionText(detector);
            foreach (var c in "kyouhagoogle") text.Append(c);
            return text.Display(final: false);
        });
        Check("英語のスペルチェッカー", () => WindowsSpellChecker.Shared.IsAvailable ? $"meeting={WindowsSpellChecker.Shared.IsWord("meeting")}" : "(使えない: 同梱の辞書だけで判定)");
        detector.SpellChecker = WindowsSpellChecker.Shared;
        Check("書き間違い辞書", () => MisspellingDictionary.Load(null).Find("ぶれすれっど")?.Right ?? "(見つからない)");
        Check("補助辞書", () => string.Join(",", CandidateDictionary.Load(null).Lookup("はし")));
        Check("文脈の手がかり辞書", () => ContextRules.Load(null).Choose("あつい", "気温") ?? "(なし)");
        Check("ユーザー辞書", () => new UserDictionary(null).Add("きごうとう", "記号等") ?? "登録できる");
        using (var converter = new MsImeKanjiConverter())
        {
            Check("変換エンジン", () => converter.Convert("きょうはいいてんき") ?? "(変換できない: Microsoft IME が無い?)");
            Check("文節", () => string.Join("|", converter.ConvertClauses("たんいをとる")?.Select(c => c.Text) ?? ["(取れない)"]));
        }
        using (var candidates = new WinRtCandidates())
        {
            Check("Windows の変換候補", () => string.Join(",", candidates.Get("はし").Take(5)));
        }
        Check("UI Automation", () => { var result = ""; var thread = new Thread(() => result = FocusInspector.Probe()); thread.SetApartmentState(ApartmentState.MTA); thread.Start(); thread.Join(); return result; });
        Check("画面", () =>
        {
            using var engine = new MeltypeEngine(new Config.Settings(), null, null);
            using var invoker = new Control();
            invoker.CreateControl();
            using var service = new CompositionService(invoker, detector, new CompositionOptions { UserDictionary = new UserDictionary(null), History = new ConversionHistory(null) });
            var names = new List<string>();
            foreach (var form in new Form[] { new UI.SettingsForm(engine), new UI.UserDictionaryForm(service), new UI.LogForm(engine) })
            {
                using (form)
                {
                    form.CreateControl();
                    using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    names.Add(form.Text);
                }
            }
            return string.Join(", ", names);
        });

        Check("タスクトレイ", () =>
        {
            // トレイのアイコン・メニュー・ホットキーを作ってすぐ閉じる (キーボードフックは掛けない)。
            // TsfIntroduced にしておく (作る人の PC に Meltype IME があると、入力方式が Meltype IME に切り替わってしまう)。
            using var engine = new MeltypeEngine(new Config.Settings { TsfIntroduced = true }, null, null);
            var tray = new UI.TrayApplicationContext(engine);
            tray.ExitThread();
            return "OK";
        });
        Check("学習データ・ログの保存", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"meltype-selftest-{Guid.NewGuid():N}");
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
                Directory.Delete(directory, recursive: true);
            }
        });

        Check("クリップボード", () =>
        {
            // 報告の画面の「ログをコピー」・ログの画面の「コピー」・貼り付けで入力するアプリが使う。
            // WinForms のクリップボードは、実行時に System.Reflection.Metadata を読み込む。配布用にランタイムを削るとき、
            // 「自己診断で読み込まれなかった大きなアセンブリを削る」でこれも消えてしまい、コピーできなくなる (#41)。
            // ここで実際にコピーして、削りすぎていないことと、これからもコピーできることを確かめる。
            // 中身は、テキストだったときだけ元に戻す (自己診断は作る人の PC で動くため)。
            var saved = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            var text = $"Meltype 自己診断 {Guid.NewGuid():N}";
            Clipboard.SetText(text);
            var copied = Clipboard.GetText() == text;
            if (saved is not null) Clipboard.SetText(saved);
            return copied ? "コピーできる" : "コピーできない";
        });

        // Zenzai (llama-server と HttpClient で話す) と GitHub の Issue の題名の候補 (HTTPS) が使う部品。どちらも既定は OFF で、ほかの機能では読み込まれない。
        // 配布用にランタイムを削るとき、ここで読み込まないと「読み込まれなかった大きなアセンブリ」として消え、
        // ON にしたとたん、打つたびに例外になって日本語を打てなくなった (#973)。
        Check("Zenzai (llama-server と HTTP)", () =>
        {
            // 本物と同じ ZenzaiConverter で、loopback の偽の llama-server に 1 回変換を頼む (POST・JSON の道を通す。外へはつながない)
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(() => AnswerOnce(listener, """{"content":"今日は"}"""));
            using var zenzai = new ZenzaiConverter(new Uri($"http://127.0.0.1:{port}/"), timeoutMs: 10000);
            var text = zenzai.Convert("きょうは");
            // 偽のサーバーの側で例外になったときは、その例外を出す (Wait が投げる)
            server.Wait(TimeSpan.FromSeconds(10));
            return text ?? throw new InvalidOperationException("偽の llama-server から答えを受け取れない");
        });
        Check("GitHub の題名 (HTTPS)", () =>
            // TLS (SslStream) と証明書の確かめ (X509Chain) の型に触って読み込むだけ。外へはつながないので、TLS の接続そのものは確かめていない
            $"{typeof(System.Net.Security.SslStream).Name}, {typeof(System.Security.Cryptography.X509Certificates.X509Chain).Name}");

        report.AppendLine(failed == 0 ? "すべて OK" : $"{failed} 件失敗");
        // 配布用パッケージの作成で、使わない部品を削るのに使う (Build-Package.ps1)。
        report.AppendLine("LOADED: " + string.Join(" ", AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).Order()));
        if (reportPath is not null) File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true));
        else MessageBox.Show(report.ToString(), "Meltype 自己診断", MessageBoxButtons.OK, failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        return failed == 0 ? 0 : 1;
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
}
