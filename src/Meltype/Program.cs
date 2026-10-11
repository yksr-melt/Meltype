// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Diagnostics;
using Meltype.UI;

namespace Meltype;

internal static class Program
{
    [STAThread]
    /// <summary>
    /// 語ごとの自動の判定 (学習は使わない)。今の版の判定にも同じものを使う (<see cref="Updater.PreviewChanges"/>)。
    /// </summary>
    internal static string JudgeWords(IEnumerable<string> words)
    {
        var detector = Composition.CompositionDetector.CreateDefault();
        if (Detection.WindowsSpellChecker.Shared.IsAvailable) detector.SpellChecker = Detection.WindowsSpellChecker.Shared;
        return Composition.WordJudge.JudgeAll(detector, words);
    }

    private static int JudgeWords(string input, string output)
    {
        try
        {
            File.WriteAllText(output, JudgeWords(File.ReadAllLines(input)));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Main(string[] args)
    {
        // Meltype.exe --selftest [結果ファイル]: キーボードフックを掛けずに、主な機能が動くかだけを確かめる。
        if (args.FirstOrDefault() == "--selftest") return SelfTest.Run(args.ElementAtOrDefault(1));
        // Meltype.exe --exit: 動いている Meltype を終了させる (インストール・アンインストール用。管理者として動いていても止められる)。
        if (args.FirstOrDefault() == "--exit") return ExitSignal.Send() ? 0 : 1;
        // Meltype.exe --judge-words <語の一覧> <結果>: 語ごとの自動の判定を書き出す。前の版の Meltype が、更新の前に
        // 「あなたの打ち方で変わる語」を調べるために、ダウンロードした新しい版をこれで動かす (issue #285)。
        if (args.FirstOrDefault() == "--judge-words" && args.Length >= 3) return JudgeWords(args[1], args[2]);

        // フックを二重に掛けると同じ打鍵を二重に保留・再入力してしまうので、多重起動させない。
        using var mutex = new Mutex(initiallyOwned: true, @"Local\Meltype.SingleInstance", out var createdNew);
        // Meltype.exe --restore <バックアップ>: 動いている Meltype が終わるのを待ってから、バックアップを戻して起動する
        // (動いている Meltype が終わるときに学習データを書き戻すので、戻すのはその後)。
        var restore = args.FirstOrDefault() == "--restore" ? args.ElementAtOrDefault(1) : null;
        if (!createdNew && (restore is null || !WaitForExit(mutex))) return 0;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error($"UI で例外: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error($"未処理の例外: {e.ExceptionObject}");
            Log.FlushFile();
            WriteCrashLog(e.ExceptionObject);
        };

        AppPaths.MigrateFromOldName();
        Directory.CreateDirectory(AppPaths.DataDirectory);
        if (restore is not null) RestoreBackup(restore);
        var settings = Settings.Load(AppPaths.ConfigFile);
        if (!File.Exists(AppPaths.ConfigFile))
        {
            try { settings.Save(AppPaths.ConfigFile); } catch { }
        }

        // 動作モードが Meltype IME なのに、Meltype IME が登録されていない (登録を外した・ほかのユーザーがアンインストールした):
        // どこでも何も起きなくなるので、Meltype キーボードに戻して保存する (Meltype IME を入れ直したら、トレイで選び直す)。
        // 保存しておくと、あとで前の版 (動作モード「Meltype IME」を読めない) を入れても、設定が初期化されない
        if (settings.Mode == InputMode.Tsf && Tip.TipServer.IsKnownUnregistered)
        {
            settings.Mode = InputMode.Keyboard;
            try { settings.Save(AppPaths.ConfigFile); } catch { }
            Diagnostics.Log.Warn("Meltype IME が登録されていないので、動作モードを Meltype キーボードに戻しました。");
        }

        // ダウンロード済みの新しい版があれば、起動せずに更新する (install.ps1 が新しい版を起動する)。
        if (Updater.ApplyStagedAtStartup(() => settings.AutoUpdate,
                version => UpdateChangesDialog.Confirm(new Composition.LanguageMemory(AppPaths.LanguageMemoryFile), version, settings))) return 0;

        MeltypeEngine engine;
        try
        {
            engine = new MeltypeEngine(settings, AppPaths.ConfigFile, AppPaths.ModelFile, AppPaths.UserDictionaryDirectory);
            engine.Start();
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            MessageBox.Show($"Meltype を開始できませんでした。\n\n{ex.Message}\n\n詳しい内容: {AppPaths.CrashLogFile}", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        using (engine)
        {
            using var exitSignal = new ExitSignal();
            Application.Run(new TrayApplicationContext(engine));
        }
        return 0;
    }

    /// <summary>
    /// 落ちたとき (起動できなかったとき) の例外を crash.log に書き足す。ファイルへのログ (設定) が OFF でも書く:
    /// 起動直後に落ちる報告で、原因の手がかりが何も残っていなかった。打った文字は含まない (例外の種類と場所だけ)。
    /// </summary>
    private static void WriteCrashLog(object exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(AppPaths.CrashLogFile,
                $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} Meltype {AppInfo.Version} / {Environment.OSVersion} ====\n{exception}\n\n");
        }
        catch
        {
            // 書けなくても、元の例外の処理を続ける
        }
    }

    /// <summary>前の Meltype が終わる (単一起動の印が空く) のを待つ。</summary>
    private static bool WaitForExit(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.FromSeconds(15));
        }
        catch (AbandonedMutexException)
        {
            // 前の Meltype が印を返さずに終わった: 取れたことになる
            return true;
        }
    }

    private static void RestoreBackup(string path)
    {
        try
        {
            var count = Backup.Restore(File.ReadAllBytes(path), AppPaths.DataDirectory);
            Log.Info($"バックアップから {count} 個のファイルを戻しました。");
            MessageBox.Show($"バックアップを戻しました ({count} 個のファイル)。\n今までのファイルは、データフォルダーに .before-restore として残しています。", "Meltype",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"バックアップを戻せませんでした。\n\n{ex.Message}", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
