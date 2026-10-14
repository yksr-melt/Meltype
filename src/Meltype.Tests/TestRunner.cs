// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// Windows 版のテストと調査用の道具。Meltype.Core.Tests のテスト (OS に依存しない部分) もまとめて流し、
/// そのときは Windows のスペルチェッカーを使う。
/// </summary>
internal static class TestRunner
{
    [STAThread]
    public static int Main(string[] args)
    {
        TestSupport.WordChecker = Detection.WindowsSpellChecker.Shared;
        // dotnet run --project src/Meltype.Tests -- --convert きょうはいいてんきです
        // で、Microsoft IME の変換エンジン (MSIME.Japan) が使えるかを確かめる。
        // dotnet run --project src/Meltype.Tests -- --autocorrect teh recieve
        // で、Windows のスペルチェッカーの自動修正 (teh → the) を確かめる。
        if (args.FirstOrDefault() == "--autocorrect")
        {
            foreach (var word in args.Skip(1)) Console.WriteLine($"{word} → {Detection.WindowsSpellChecker.Shared.AutoCorrection(word) ?? "(なし)"}");
            return 0;
        }
        // 不具合報告のフォームに自動で入れる実行環境 (Meltype の「不具合の報告・提案...」と同じもの)
        if (args.FirstOrDefault() == "--report-info")
        {
            var environment = Diagnostics.ReportInfo.Environment(Config.Settings.Load(Config.AppPaths.ConfigFile));
            Console.WriteLine(environment);
            Console.WriteLine();
            Console.WriteLine(AppInfo.GitHubReportUrl("2-misdetection.yml", environment));
            return 0;
        }
        if (args.FirstOrDefault() == "--units") { DebugUnits.Run(args[1]); return 0; }
        // henkan-test / japanese-henkan-test を Windows で: アプリと同じく Windows のスペルチェッカーを使い、
        // 漢字の読みは Microsoft IME の逆変換で求める。Mozc (MELTYPE_MOZC、無ければ native\mozc\bin) が無ければ Microsoft IME で変換する。
        if (args.FirstOrDefault() is "--henkan" or "--jht" or "--jht-batch")
        {
            if (Detection.WindowsSpellChecker.Shared.IsAvailable) CompositionTests.Detector.SpellChecker = Detection.WindowsSpellChecker.Shared;
            var bundled = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "native", "mozc", "bin", "meltype_mozc_helper.exe");
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MELTYPE_MOZC")) && File.Exists(bundled)) Environment.SetEnvironmentVariable("MELTYPE_MOZC", Path.GetFullPath(bundled));
            using var ime = new Composition.MsImeKanjiConverter();
            Henkan.FallbackConverter = ime;
            if (args[0] == "--jht-batch")
            {
                Jht.Batch(ime.Reading, Henkan.Keyboard, () => Henkan.EngineName);
                Henkan.Shutdown();
                return 0;
            }
            var text = string.Join(" ", args.Skip(1));
            if (args[0] == "--henkan")
            {
                Henkan.Run(text);
                Henkan.Shutdown();
                return 0;
            }
            var slash = text.IndexOf(" / ", StringComparison.Ordinal);
            Jht.Run(slash >= 0 ? text[..slash] : text, slash >= 0 ? text[(slash + 3)..] : null, ime.Reading, Henkan.Keyboard, Henkan.EngineName);
            Henkan.Shutdown();
            return 0;
        }
        if (args.FirstOrDefault() == "--repro")
        {
            // GitHub の bot 用: 報告された打鍵を打ってみて JSON で返す (Meltype.Core.Tests の --repro に、アプリと同じ Windows のスペルチェッカーを足したもの)。
            if (Detection.WindowsSpellChecker.Shared.IsAvailable) CompositionTests.Detector.SpellChecker = Detection.WindowsSpellChecker.Shared;
            Repro.Type(args.ElementAtOrDefault(1) ?? "", args.ElementAtOrDefault(2) ?? "enter");
            return 0;
        }
        if (args.FirstOrDefault() == "--type")
        {
            // dotnet run --project src/Meltype.Tests -- --type "ke-kiwotabeta "
            // 本物の変換エンジンをつないだ変換ボックスに 1 文字ずつ打ち、表示の変化を見る (ライブ変換 ON)。
            // 本物のアプリと同じく Windows のスペルチェッカーも使う。
            CompositionTests.Detector.SpellChecker = Detection.WindowsSpellChecker.Shared;
            using var ime = new Composition.MsImeKanjiConverter();
            using var winrt = new Composition.WinRtCandidates();
            // native\mozc\bin に Mozc の変換ヘルパーがあれば、アプリと同じく Mozc + Microsoft IME (両方) で変換する。
            // MELTYPE_ENGINE=System なら Microsoft IME だけ。
            using var mozc = new Composition.MozcConverter(Path.GetFullPath(Path.Combine("native", "mozc", "bin", "meltype_mozc_helper.exe")), Path.Combine(Path.GetTempPath(), "meltype-mozc-profile"));
            var engine = Enum.TryParse<Config.ConversionEngine>(Environment.GetEnvironmentVariable("MELTYPE_ENGINE"), out var chosen) ? chosen : Config.ConversionEngine.Hybrid;
            var converter = new Composition.HybridConverter(() => engine, mozc.IsInstalled ? mozc : null, ime, r => winrt.Get(r));
            Console.WriteLine($"変換エンジン: {(mozc.IsInstalled && engine != Config.ConversionEngine.System ? "Mozc + Microsoft IME" : "Microsoft IME")}");
            var translations = Composition.TranslationDictionary.Load();
            foreach (var text in args.Skip(1))
            {
                // "前の文字列|打つキー" の形なら、前の文字列をキャレットの前にある確定済みの文字として扱う。
                var bar = text.IndexOf('|');
                // MELTYPE_DIRECT=1 なら英数 (直接入力) の状態から打ち始める。
                var direct = Environment.GetEnvironmentVariable("MELTYPE_DIRECT") == "1";
                var keyboard = new CompositionTests.Keyboard(live: true, direct: direct, converter: converter, moreCandidates: converter.Candidates, userDictionary: new Composition.UserDictionary(null), translations: translations);
                if (bar >= 0) keyboard.Host.PrecedingText = text[..bar];
                foreach (var c in bar >= 0 ? text[(bar + 1)..] : text)
                {
                    var before = keyboard.Host.Events.Count;
                    keyboard.Type(c.ToString());
                    var view = keyboard.Host.View;
                    // 変換ボックスを通らずにアプリへ送ったキー (英数状態で英語と判定した打鍵など)
                    var passed = string.Concat(keyboard.Host.Events.Skip(before).Where(e => e.StartsWith("passed:") || e.StartsWith("down:"))
                        .Select(e => (char)Convert.ToInt32(e[(e.IndexOf(':') + 1)..], 16)).Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == ' ').Select(char.ToLowerInvariant));
                    Console.WriteLine($"  {c} → {(view is null ? "(なし)" : view.Converting ? "[" + string.Join("|", view.Clauses!) + "] 候補: " + string.Join(",", view.Candidates) : view.Text)}{(passed.Length > 0 ? $"  (アプリへ: {passed})" : "")}{(direct ? $"  [{(keyboard.Direct ? "英数" : "日本語")}]" : "")}");
                }
                Console.WriteLine($"  確定: {string.Join("|", keyboard.Host.Output)}");
                Console.WriteLine();
            }
            return 0;
        }
        if (args.FirstOrDefault() == "--winmd") { WinMdProbe.Run(args[1], args.Skip(2)); return 0; }
        if (args.FirstOrDefault() == "--candidates") { using var winrt = new Composition.WinRtCandidates(); foreach (var r in args.Skip(1)) Console.WriteLine($"{r}: {string.Join(", ", winrt.Get(r))}"); foreach (var e in Diagnostics.Log.Snapshot()) Console.WriteLine(e); return 0; }
        if (args.FirstOrDefault() == "--mozc")
        {
            // dotnet run --project src/Meltype.Tests -- --mozc <meltype_mozc_helper.exe> はははきょうしょくじにいきました ...
            // Mozc と Microsoft IME の変換結果を並べて比べる。
            using var mozc = new Composition.MozcConverter(args[1], Path.Combine(Path.GetTempPath(), "meltype-mozc-profile"));
            using var ime = new Composition.MsImeKanjiConverter();
            foreach (var reading in args.Skip(2))
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var clauses = mozc.ConvertClauses(reading);
                var elapsed = watch.ElapsedMilliseconds;
                Console.WriteLine(reading);
                Console.WriteLine($"  Mozc ({elapsed}ms): {(clauses is null ? "(失敗)" : string.Join("|", clauses.Select(c => c.Text)))}");
                Console.WriteLine($"  IME : {string.Join("|", ime.ConvertClauses(reading)?.Select(c => c.Text) ?? [])}");
            }
            foreach (var entry in Diagnostics.Log.Snapshot()) Console.WriteLine($"  {entry}");
            return 0;
        }
        if (args.FirstOrDefault() == "--reading") { using var c = new Composition.MsImeKanjiConverter(); foreach (var t in args.Skip(1)) Console.WriteLine($"{t} → {c.Reading(t) ?? "(なし)"}"); return 0; }
        if (args.FirstOrDefault() == "--gen-emoji") { EmojiGenerator.Run(args[1], args[2], args[3]); return 0; }
        if (args.FirstOrDefault() == "--eval") { Quality.Print(Quality.Run()); return 0; }
        if (args.FirstOrDefault() == "--tip-server") return TipHarness.Server(int.TryParse(args.ElementAtOrDefault(1), out var seconds) ? seconds : 600);
        if (args.FirstOrDefault() == "--tip-e2e")
        {
            TipEndToEnd.ShotDirectory = args.ElementAtOrDefault(1);
            return TipEndToEnd.Run();
        }
        if (args.FirstOrDefault() == "--tip-client") return TipHarness.Client(args[1].Replace("\\n", "\n"));
        if (args.FirstOrDefault() == "--render-forms")
        {
            // 調査用: 設定画面とユーザー辞書の画面を表示せずに画像にする。
            Application.EnableVisualStyles();
            var engine = new MeltypeEngine(new Config.Settings(), null, null);
            using var invoker = new Control();
            invoker.CreateControl();
            using var service = new Composition.CompositionService(invoker, Composition.CompositionDetector.CreateDefault(), new Composition.CompositionOptions { UserDictionary = new Composition.UserDictionary(null) });
            service.UserDictionary.Add("きごうとう", "記号等");
            var composition = new Composition.CompositionWindow();
            // 候補が 9 個より多いとき (ページに分けて出す) の見た目。2 ページ目の候補を選んでいる。
            composition.ShowView(new Composition.CompositionView("ぶれすれっど", ["ブレスレッド", "ぶれすれっど", "buresureddo"], 0, true, "Space/↓ 候補", Suggestion: "もしかして: ブレスレット　<Tab>で修正"), new Point(-5000, -5000));
            var indicator = new Composition.ModeIndicatorWindow();
            indicator.Flash(true, new Point(-5000, -5000));
            foreach (var form in new Form[] { new UI.SettingsForm(engine), new UI.UserDictionaryForm(service), new UI.ReportDialog(new Config.Settings()), new UI.LearnedWordsForm(LearnedSample(), new Composition.ConversionHistory(null)), new UI.WelcomeForm(), indicator, composition })
            {
                using (form)
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-5000, -5000);
                    form.Show();
                    Application.DoEvents();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(args[1], form.GetType().Name + ".png"));
                    // 設定画面は、アプリ別設定の表のあたりまでスクロールした画像も作る。
                    if (form is UI.SettingsForm && FindControl(form, c => c is Button { Text: "実行中のアプリから追加…" }) is { } button &&
                        FindControl(form, c => c is ScrollableControl { AutoScroll: true } s && s.Contains(button)) is ScrollableControl scroller)
                    {
                        scroller.ScrollControlIntoView(button.Parent!);
                        Application.DoEvents();
                        using var scrolled = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(scrolled, new Rectangle(0, 0, form.Width, form.Height));
                        scrolled.Save(Path.Combine(args[1], "SettingsForm-apps.png"));
                    }
                    // スクロールできる画面は、いちばん下までスクロールした画像も作る (一番下の項目が見切れていないか)。
                    if (FindControl(form, c => c is ScrollableControl { AutoScroll: true, VerticalScroll.Visible: true }) is ScrollableControl bottom)
                    {
                        bottom.AutoScrollPosition = new Point(0, bottom.DisplayRectangle.Height);
                        Application.DoEvents();
                        using var scrolled = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(scrolled, new Rectangle(0, 0, form.Width, form.Height));
                        scrolled.Save(Path.Combine(args[1], form.GetType().Name + "-bottom.png"));
                    }
                }
            }
            return 0;
        }
        if (args.FirstOrDefault() == "--runtime-closure") { RuntimeClosure.Run(args[1]); return 0; }
        if (args.FirstOrDefault() == "--context")
        {
            // dotnet run --project src/Meltype.Tests -- --context 財布の:かわ 川に:はし
            using var converter = new Composition.MsImeKanjiConverter();
            foreach (var pair in args.Skip(1))
            {
                var parts = pair.Split(':');
                Console.WriteLine($"{parts[0]} + {parts[1]}: 単独={converter.Convert(parts[1])}  " +
                    string.Join("  ", new uint[] { 0x10, 0x20, 0x30 }.Select(f => $"0x{f:X}={converter.ProbeWithContext(parts[0], parts[1], f)}")) +
                    $"  読みごと={converter.Convert(parts[0] + parts[1])}");
            }
            return 0;
        }
        if (args.FirstOrDefault() == "--convert")
        {
            using var converter = new Composition.MsImeKanjiConverter();
            var translations = Composition.TranslationDictionary.Load();
            foreach (var text in args.Skip(1))
            {
                Console.WriteLine($"{text} → {converter.Convert(text) ?? "(変換できない)"}");
                var clauses = converter.ConvertClauses(text);
                Console.WriteLine("  解析: " + converter.DescribeMorph(text));
                Console.WriteLine("  文脈付き (この本は): " + string.Join(" | ", converter.ConvertClauses(text, "この本は")?.Select(c => $"{c.Reading}={c.Text}") ?? ["(取れない)"]));
                Console.WriteLine("  文節: " + (clauses is null ? "(取れない)" : string.Join(" | ", clauses.Select(c => $"{c.Reading}={c.Text}"))));
            }
            foreach (var entry in Diagnostics.Log.Snapshot()) Console.WriteLine(entry);
            return converter.IsAvailable ? 0 : 1;
        }

        if (args.FirstOrDefault() == "--explain") { TestHost.Explain(args.Skip(1)); return 0; }

        return TestHost.Run([typeof(TestSupport).Assembly, typeof(TestRunner).Assembly], args.FirstOrDefault());
    }

    /// <summary>--render-forms 用の、学習した語の見本。</summary>
    private static Composition.LanguageMemory LearnedSample()
    {
        var memory = new Composition.LanguageMemory(null) { IsReadableRomaji = _ => true };
        memory.Remember("go", english: true);
        memory.Remember("api", english: true);
        memory.Remember("sushi", english: false, explicitChoice: true);
        return memory;
    }

    /// <summary>画面の中から条件に合うコントロールを探す (--render-forms 用)。</summary>
    private static Control? FindControl(Control root, Func<Control, bool> match)
    {
        foreach (Control child in root.Controls)
        {
            if (match(child)) return child;
            if (FindControl(child, match) is { } found) return found;
        }
        return null;
    }
}
