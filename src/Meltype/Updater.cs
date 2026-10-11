// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;
using Meltype.Diagnostics;

namespace Meltype;

/// <summary>
/// 自動更新。GitHub のリリースに新しい版があれば、同梱の update.ps1 でダウンロード・展開しておき、
/// 次に Meltype を起動したとき (Windows へのサインイン時) か、トレイの「更新して再起動」で install.ps1 を実行する。
/// ネットワーク・zip の処理は PowerShell に任せる (同梱の .NET ランタイムを小さく削っているため)。
/// </summary>
internal sealed class Updater : IDisposable
{
    /// <summary>最初の確認は起動の 2 分後、その後は 6 時間ごと。</summary>
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2), Interval = TimeSpan.FromHours(6);

    private static string Directory => Path.Combine(Config.AppPaths.DataDirectory, "update");
    private static string ReadyFile => Path.Combine(Directory, "ready.txt");
    private static string ApplyingFile => Path.Combine(Directory, "applying.txt");
    private static string Script => Path.Combine(AppContext.BaseDirectory, "update.ps1");
    internal static string PowerShell => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    private readonly System.Threading.Timer _timer;
    private readonly Func<bool> _enabled;
    private int _running;

    /// <summary>新しい版をダウンロードし終えた (UI スレッドとは限らない)。引数は版。</summary>
    public event Action<string>? Ready;

    public Updater(Func<bool> enabled)
    {
        _enabled = enabled;
        _timer = new System.Threading.Timer(_ => Check(), null, FirstCheck, Interval);
    }

    /// <summary>ダウンロード済みで、今より新しい版 (無ければ null)。</summary>
    public static (string Version, string Folder)? Staged()
    {
        try
        {
            if (!File.Exists(ReadyFile)) return null;
            var parts = File.ReadAllText(ReadyFile).Trim().Split('\t');
            if (parts.Length != 2 || !IsNewer(parts[0]) || !File.Exists(Path.Combine(parts[1], "install.ps1"))) return null;
            return (parts[0], parts[1]);
        }
        catch (Exception ex)
        {
            Log.Warn($"更新の状態を読めませんでした: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 起動時: ダウンロード済みの新しい版があればインストールを始める (true なら Meltype はそのまま終了する。install.ps1 が新しい版を起動する)。
    /// 前回インストールに失敗していた (同じ版のまま起動した) ときは、繰り返さない。
    /// </summary>
    public static bool ApplyStagedAtStartup(Func<bool> enabled, Func<string, bool>? confirm = null)
    {
        try
        {
            if (File.Exists(ApplyingFile))
            {
                var tried = File.ReadAllText(ApplyingFile).Trim();
                File.Delete(ApplyingFile);
                if (IsNewer(tried))
                {
                    Log.Warn($"Meltype {tried} への更新に失敗したようです。もう一度ダウンロードするまで、自動では更新しません。");
                    File.Delete(ReadyFile);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"更新の状態を読めませんでした: {ex.Message}");
        }
        // 更新の前に、あなたの打ち方で変わる語があれば見せる (issue #285。「あとで」なら今回は更新しない)
        return enabled() && Staged() is { } staged && (confirm?.Invoke(staged.Version) ?? true) && Apply();
    }

    /// <summary>ダウンロード済みの新しい版のインストールを始める。install.ps1 が動いている Meltype を終了させてから入れ替え、起動する。</summary>
    public static bool Apply()
    {
        if (Staged() is not { } staged) return false;
        try
        {
            File.WriteAllText(ApplyingFile, staged.Version);
            var start = new ProcessStartInfo(PowerShell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = staged.Folder,
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(staged.Folder, "install.ps1") }) start.ArgumentList.Add(arg);
            Process.Start(start);
            Log.Info($"Meltype {staged.Version} に更新します。");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"更新を始められませんでした: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 更新の前に「あなたの打ち方で変わる語」を調べる (issue #285)。自分で英字 / かなに直した語 (languages.json) を、
    /// 今の版とダウンロードした新しい版 (Meltype.exe --judge-words) の両方で判定し、見え方が変わる語を返す。
    /// 調べられなければ (新しい版がこの仕組みを持たない・時間切れ) null。この PC の中だけで動かし、何も送らない。
    /// </summary>
    public static IReadOnlyList<Composition.WordChange>? PreviewChanges(IEnumerable<string> words)
    {
        if (Staged() is not { } staged) return null;
        var list = words.Where(Composition.WordJudge.IsWord).Distinct(StringComparer.Ordinal).ToList();
        if (list.Count == 0) return [];
        var exe = Path.Combine(staged.Folder, "app", "Meltype.exe");
        if (!File.Exists(exe)) return null;
        var input = Path.Combine(Directory, "judge-words.txt");
        var output = Path.Combine(Directory, "judge-result.txt");
        try
        {
            File.WriteAllLines(input, list);
            File.Delete(output);
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
            foreach (var arg in new[] { "--judge-words", input, output }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(60_000))
            {
                process.Kill();
                Log.Warn("新しい版の判定が時間内に終わりませんでした。変わる語は調べずに更新します。");
                return null;
            }
            if (process.ExitCode != 0 || !File.Exists(output)) return null;
            var next = Composition.WordJudge.Parse(File.ReadAllText(output));
            var now = Composition.WordJudge.Parse(Program.JudgeWords(list));
            var changes = Composition.WordJudge.Changes(now, next);
            Log.Info($"Meltype {staged.Version} で見え方が変わる語: {changes.Count} 語 (調べた語 {list.Count})。");
            return changes;
        }
        catch (Exception ex)
        {
            Log.Warn($"新しい版で変わる語を調べられませんでした: {ex.Message}");
            return null;
        }
        finally
        {
            try { File.Delete(input); File.Delete(output); } catch { }
        }
    }

    /// <summary>今すぐ確認する (トレイの「更新を確認」)。結果は Ready か戻り値の文言で返す。</summary>
    public Task<string> CheckNowAsync() => AppInfo.IsPublicRelease
        ? Task.Run(() => Run() ?? "更新を確認できませんでした。")
        : Task.FromResult($"Meltype {AppInfo.Version} はテスト版です。自動更新は公開版 (1.0.0) から使えます。新しいテスト版は配布元から受け取ってください。");

    private void Check()
    {
        // 公開 (1.0.0) まではリポジトリが非公開で、確認しても見つからない (ログに 404 が残るだけ) ので確認しない
        if (!_enabled() || !AppInfo.IsPublicRelease) return;
        Run();
    }

    /// <summary>update.ps1 を実行する。戻り値は利用者に見せる結果。</summary>
    private string? Run()
    {
        if (!File.Exists(Script) || AppInfo.Version == "?") return null;
        if (Interlocked.Exchange(ref _running, 1) == 1) return null;
        try
        {
            var start = new ProcessStartInfo(PowerShell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Script,
                "-Repository", AppInfo.UpdateRepository, "-CurrentVersion", AppInfo.Version, "-Directory", Directory })
            {
                start.ArgumentList.Add(arg);
            }
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(15 * 60 * 1000)) process.Kill();
            var line = output.Split('\n').LastOrDefault()?.Trim() ?? "";
            if (line.StartsWith("ready ", StringComparison.Ordinal))
            {
                var version = line[6..];
                Log.Info($"Meltype {version} をダウンロードしました。次の起動時に更新します。");
                Ready?.Invoke(version);
                return $"Meltype {version} をダウンロードしました。";
            }
            if (line == "none") return $"Meltype {AppInfo.Version} は最新です。";
            Log.Warn($"更新を確認できませんでした: {line}");
            return $"更新を確認できませんでした: {(line.StartsWith("error ", StringComparison.Ordinal) ? line[6..] : line)}";
        }
        catch (Exception ex)
        {
            Log.Warn($"更新を確認できませんでした: {ex.Message}");
            return null;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private static bool IsNewer(string version) =>
        System.Version.TryParse(version, out var other) && System.Version.TryParse(AppInfo.Version, out var current) && other > current;

    public void Dispose() => _timer.Dispose();
}
