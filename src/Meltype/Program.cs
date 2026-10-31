// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Diagnostics;
using Meltype.UI;

namespace Meltype;

internal static class Program
{
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

    [STAThread]
    private static int Main(string[] args)
    {
        using var spellChecker = Detection.WindowsSpellChecker.Shared;
        // Meltype.exe --version: 版だけを出して終わる (フック・UI は起動しない)。
        // WinExe はコンソールを持たないので、親ターミナル (cmd / PowerShell) に付け直してから書く。
        if (args.FirstOrDefault() == "--version")
        {
            Native.AttachConsole(0xFFFFFFFF);
            using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            writer.WriteLine($"Meltype {AppInfo.Version}");
            return 0;
        }
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
            Composition.LearningSaves.FlushAll();
        };

        if (!AppPaths.IsPortable) AppPaths.MigrateFromOldName();
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"データ保存先を作成できませんでした。保存先に書き込めることを確認してください。\n\n{AppPaths.DataDirectory}\n\n{ex.Message}",
                "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        if (restore is not null) RestoreBackup(restore);
        var settings = Settings.Load(AppPaths.ConfigFile);
        SettingsSchema.Write(AppPaths.DataDirectory);
        if (!File.Exists(AppPaths.ConfigFile))
        {
            try { settings.Save(AppPaths.ConfigFile); } catch { }
        }

        // 動作モードが Meltype IME なのに使えない: どこでも何も起きなくなるので、Meltype キーボードに戻して保存する
        // (Meltype IME を入れ直したら、トレイで選び直す)。
        // 保存しておくと、あとで前の版 (動作モード「Meltype IME」を読めない) を入れても、設定が初期化されない
        if (ShouldUseKeyboardOnStartup(settings, AppPaths.IsPortable, Tip.TipServer.IsKnownUnregistered, Tip.TipServer.IsInUserLanguageList, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture))
        {
            settings.Mode = InputMode.Keyboard;
            try { settings.Save(AppPaths.ConfigFile); } catch { }
            Diagnostics.Log.Warn(AppPaths.IsPortable ? "ポータブル版では Meltype IME を使わないため、Meltype キーボードに戻しました。" : "Meltype IME を使えない (ARM64 の Windows・登録されていない・キーボードの一覧に無い) ので、動作モードを Meltype キーボードに戻しました。");
        }

        // 更新の確認や「更新失敗」のダイアログを出している間も、インストーラーの終了の合図を受け取れるように、先に窓を作る
        using var exitSignal = new ExitSignal();

        // ダウンロード済みの新しい版があれば、起動せずに更新する (install.ps1 が新しい版を起動する)。
        if (!AppPaths.IsPortable && Updater.ApplyStagedAtStartup(() => settings.AutoUpdate, settings.UpdatePrerelease,
                version => UpdateChangesDialog.Confirm(new Composition.LanguageMemory(AppPaths.LanguageMemoryFile), version, settings),
                version => MessageBox.Show($"Meltype {version} への更新に失敗しました。\n\n現在のバージョンで起動します。自動での再試行は行いません。\n\n記録: {Updater.InstallLog}", "Meltype - 更新失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning))) return 0;

        // 学習データは確定のたびに書かず、少し後にまとめて書く。終了・ログオフのときに残りを書く
        // (更新の確認は学習データを別に読み込んで書くので、それより後で切り替える)
        Composition.LearningSaves.Deferred = true;
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => Composition.LearningSaves.FlushAll();

        MeltypeEngine engine;
        try
        {
            engine = new MeltypeEngine(settings, AppPaths.ConfigFile, AppPaths.UserDictionaryDirectory);
            engine.Start();
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            MessageBox.Show($"Meltype を開始できませんでした。\n\n{ex.Message}\n\n詳しい内容: {AppPaths.CrashLogFile}", "Meltype", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        // スタートアップのフォルダー・Run の登録 (サインインの後、遅れて起動する) を、すぐ起動するタスクに移す (#283)
        if (!AppPaths.IsPortable) ThreadPool.QueueUserWorkItem(_ => Startup.MigrateToTask());

        using (engine)
        {
            Application.Run(new TrayApplicationContext(engine));
        }
        Composition.LearningSaves.FlushAll();
        return 0;
    }

    /// <summary>
    /// 起動したときに、動作モードを Meltype IME から Meltype キーボードに戻すか。
    /// ARM64 の Windows (Meltype IME は対応していない)・Meltype IME が登録されていない (登録を外した・ほかのユーザーがアンインストールした) とき。
    /// まだ Meltype IME に切り替えたことがない (初期値の Meltype IME のまま) ときは、このユーザーのキーボードの一覧に無いときも戻す
    /// (ほかのユーザーが登録した PC で Meltype IME を断ると、Win + Space で選べない)。
    /// </summary>
    internal static bool ShouldFallBackToKeyboard(Settings settings, bool knownUnregistered, bool inUserLanguageList, System.Runtime.InteropServices.Architecture architecture) =>
        settings.Mode == InputMode.Tsf &&
        (architecture == System.Runtime.InteropServices.Architecture.Arm64 || knownUnregistered || (!settings.TsfIntroduced && !inUserLanguageList));

    internal static bool ShouldUseKeyboardOnStartup(Settings settings, bool isPortable, bool knownUnregistered, bool inUserLanguageList, System.Runtime.InteropServices.Architecture architecture) =>
        (isPortable && settings.Mode != InputMode.Keyboard) || ShouldFallBackToKeyboard(settings, knownUnregistered, inUserLanguageList, architecture);

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
