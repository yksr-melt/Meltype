// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Globalization;
using System.Text;
using Meltype.Composition;
using Meltype.Ime;

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
        foreach (var (name, run) in SelfCheck.Checks(AppContext.BaseDirectory)) Check(name, run);
        Check("UI Automation", () => { var result = ""; var thread = new Thread(() => result = FocusInspector.Probe()); thread.SetApartmentState(ApartmentState.MTA); thread.Start(); thread.Join(); return result; });
        Check("画面", () =>
        {
            using var data = new TemporaryData();
            using var engine = new MeltypeEngine(new MeltypeRuntime(data.Directory));
            var names = new List<string>();
            foreach (var form in new Form[] { new UI.LogForm(engine) })
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
            using var data = new TemporaryData();
            using var engine = new MeltypeEngine(new MeltypeRuntime(data.Directory));
            var tray = new UI.TrayApplicationContext(engine, new Config.AppState { TsfIntroduced = true });
            tray.ExitThread();
            return "OK";
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

    /// <summary>設定・学習データを置く一時フォルダー (自己診断で、使っている人のデータに書かないように)。</summary>
    internal sealed class TemporaryData : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), $"meltype-selftest-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch (IOException) { }
        }
    }
}
