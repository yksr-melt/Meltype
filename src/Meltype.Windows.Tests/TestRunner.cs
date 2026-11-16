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
        using var spellChecker = Detection.WindowsSpellChecker.Shared;
        TestSupport.WordChecker = Detection.WindowsSpellChecker.Shared;
        // dotnet run --project src/Meltype.Windows.Tests -- --autocorrect teh recieve
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
        // henkan-test / japanese-henkan-test を Windows で: アプリと同じく Windows のスペルチェッカーを使う。
        // Mozc は MELTYPE_MOZC、無ければ native\mozc\bin のもの。
        if (args.FirstOrDefault() is "--henkan" or "--jht" or "--jht-batch")
        {
            if (Detection.WindowsSpellChecker.Shared.IsAvailable) CompositionTests.Detector.SpellChecker = Detection.WindowsSpellChecker.Shared;
            if (MozcHelper() is { } helper) Environment.SetEnvironmentVariable("MELTYPE_MOZC", helper);
            if (args[0] == "--jht-batch")
            {
                Jht.Batch(Henkan.ReadingOf, Henkan.Keyboard, () => Henkan.EngineName);
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
            Jht.Run(slash >= 0 ? text[..slash] : text, slash >= 0 ? text[(slash + 3)..] : null, Henkan.ReadingOf, Henkan.Keyboard, Henkan.EngineName);
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
            // dotnet run --project src/Meltype.Windows.Tests -- --type "ke-kiwotabeta "
            // 本物の変換エンジンをつないだ変換ボックスに 1 文字ずつ打ち、表示の変化を見る (ライブ変換 ON)。
            // 本物のアプリと同じく Windows のスペルチェッカーも使う。
            CompositionTests.Detector.SpellChecker = Detection.WindowsSpellChecker.Shared;
            // Mozc は MELTYPE_MOZC、無ければ native\mozc\bin のもの (Zenzai は使わない)。
            using var mozc = new Composition.MozcConverter(MozcHelper() ?? "", Path.Combine(Path.GetTempPath(), "meltype-mozc-profile"));
            var converter = new Composition.HybridConverter(mozc);
            Console.WriteLine($"変換エンジン: {(mozc.IsInstalled ? "Mozc" : "なし (かなのまま)")}");
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
                    var passed = string.Concat(keyboard.Host.Events.Skip(before).Where(e => e.StartsWith("passed:", StringComparison.Ordinal) || e.StartsWith("down:", StringComparison.Ordinal))
                        .Select(e => (char)Convert.ToInt32(e[(e.IndexOf(':') + 1)..], 16)).Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == ' ').Select(char.ToLowerInvariant));
                    Console.WriteLine($"  {c} → {(view is null ? "(なし)" : view.Converting ? "[" + string.Join("|", view.Clauses!) + "] 候補: " + string.Join(",", view.Candidates) : view.Text)}{(passed.Length > 0 ? $"  (アプリへ: {passed})" : "")}{(direct ? $"  [{(keyboard.Direct ? "英数" : "日本語")}]" : "")}");
                }
                Console.WriteLine($"  確定: {string.Join("|", keyboard.Host.Output)}");
                Console.WriteLine();
            }
            return 0;
        }
        if (args.FirstOrDefault() == "--winmd") { WinMdProbe.Run(args[1], args.Skip(2)); return 0; }
        if (args.FirstOrDefault() == "--gen-emoji") { EmojiGenerator.Run(args[1], args[2], args[3]); return 0; }
        if (args.FirstOrDefault() == "--eval") { Quality.Print(Quality.Run()); return 0; }
        if (args.FirstOrDefault() == "--tip-server") return TipHarness.Server(int.TryParse(args.ElementAtOrDefault(1), out var seconds) ? seconds : 600);
        if (args.FirstOrDefault() == "--focus-e2e") return FocusEndToEnd.Run();
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
            var engine = new MeltypeEngine(TestRuntime.Create());
            using var invoker = new Control();
            invoker.CreateControl();
            using var service = new Composition.CompositionService(invoker, engine.Runtime, () => engine.Settings);
            service.UserDictionary.Add("きごうとう", "記号等");
            var composition = new Composition.CompositionWindow();
            // 候補が 9 個より多いとき (ページに分けて出す) の見た目。2 ページ目の候補を選んでいる。
            composition.ShowView(new Composition.CompositionView("ぶれすれっど", ["ブレスレッド", "ぶれすれっど", "buresureddo"], 0, true, "Space/↓ 候補", Suggestion: "もしかして: ブレスレット　<Tab>で修正"), new Point(-5000, -5000));
            var indicator = new Composition.ModeIndicatorWindow();
            indicator.Flash(true, new Point(-5000, -5000), 1200);
            foreach (var form in new Form[] { new UI.SettingsForm(engine, new Config.AppState()), new UI.UserDictionaryForm(service), new UI.ReportDialog(new Config.Settings()), new UI.LearnedWordsForm(LearnedSample(), new Composition.ConversionHistory(null)), new UI.WelcomeForm(), indicator, composition })
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
        if (args.FirstOrDefault() == "--explain") { TestHost.Explain(args.Skip(1)); return 0; }

        return TestHost.Run([typeof(TestSupport).Assembly, typeof(TestRunner).Assembly], args.FirstOrDefault());
    }

    /// <summary>Mozc の変換ヘルパー (MELTYPE_MOZC、無ければ native\mozc\bin のもの)。どちらも無ければ null。</summary>
    internal static string? MozcHelper()
    {
        if (Environment.GetEnvironmentVariable("MELTYPE_MOZC") is { Length: > 0 } configured) return configured;
        var bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "native", "mozc", "bin", "meltype_mozc_helper.exe"));
        return File.Exists(bundled) ? bundled : null;
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
