// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

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
                report.AppendLine($"OK   {name}: {result}");
            }
            catch (Exception ex)
            {
                failed++;
                report.AppendLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        ApplicationConfiguration.Initialize();
        Check("バージョン", () => AppInfo.Version);
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

        report.AppendLine(failed == 0 ? "すべて OK" : $"{failed} 件失敗");
        // 配布用パッケージの作成で、使わない部品を削るのに使う (Build-Package.ps1)。
        report.AppendLine("LOADED: " + string.Join(" ", AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).Order()));
        if (reportPath is not null) File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true));
        else MessageBox.Show(report.ToString(), "Meltype 自己診断", MessageBoxButtons.OK, failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        return failed == 0 ? 0 : 1;
    }
}
