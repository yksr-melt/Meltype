// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace Meltype;

/// <summary>
/// Windows の起動 (サインイン) 時に Meltype を起動するか (トレイの「Windows の起動時に起動」)。
/// スタートアップのフォルダー・HKCU の Run に置いたものは、Windows がサインインの後に数秒〜十数秒遅らせて起動するので、
/// その間は Meltype を使えなかった (issue #283)。サインインしたらすぐ起動する、タスク スケジューラのタスク (このユーザーの
/// ログオン時・管理者権限なし) で起動する。タスクを作れなければ、今までどおり HKCU の Run に登録する。
/// Install.cmd・インストーラーはスタートアップのフォルダーにショートカットを置くので、起動したときにタスクへ移す (<see cref="MigrateToTask"/>)。
/// </summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Meltype";
    /// <summary>タスク スケジューラのタスクの名前 (アンインストールで消す: uninstall.ps1・Meltype.iss と合わせる)。</summary>
    internal const string TaskName = "Meltype";

    private static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Meltype.lnk");

    public static bool IsEnabled => TaskExists() || HasLegacyEntry();

    /// <summary>スタートアップのフォルダー・Run に登録してあるか (遅れて起動する、前の登録の仕方)。</summary>
    private static bool HasLegacyEntry()
    {
        if (File.Exists(Shortcut)) return true;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        if (enabled)
        {
            if (CreateTask(Application.ExecutablePath))
            {
                RemoveLegacyEntries();
            }
            else if (!HasLegacyEntry())
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
            }
        }
        else
        {
            DeleteTask();
            RemoveLegacyEntries();
        }
        Diagnostics.Log.Info(enabled ? "Windows の起動時に Meltype を起動します。" : "Windows の起動時に Meltype を起動しません。");
    }

    /// <summary>
    /// スタートアップのフォルダー・Run に登録してあれば、すぐ起動するタスクに移す (起動したときに裏で行う)。
    /// タスクを作れなければ、今の登録のまま。
    /// </summary>
    public static void MigrateToTask()
    {
        try
        {
            if (!HasLegacyEntry() || TaskExists()) return;
            if (!CreateTask(Application.ExecutablePath)) return;
            RemoveLegacyEntries();
            Diagnostics.Log.Info("Windows の起動時の起動を、サインインしたらすぐ起動するタスクに移しました。");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"起動時の登録をタスクに移せませんでした: {ex.Message}");
        }
    }

    private static void RemoveLegacyEntries()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) key.DeleteValue(ValueName, throwOnMissingValue: false);
        if (File.Exists(Shortcut)) File.Delete(Shortcut);
    }

    private static bool TaskExists() => Schtasks("/Query", "/TN", TaskName) == 0;

    private static void DeleteTask()
    {
        if (TaskExists()) Schtasks("/Delete", "/TN", TaskName, "/F");
    }

    /// <summary>このユーザーのログオン時に、遅らせずに exe を起動するタスクを作る (同じ名前があれば置き換える)。</summary>
    private static bool CreateTask(string exe)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"meltype-startup-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(xmlPath, TaskXml(WindowsIdentity.GetCurrent().Name, exe), System.Text.Encoding.Unicode);
            var created = Schtasks("/Create", "/TN", TaskName, "/XML", xmlPath, "/F") == 0;
            if (!created) Diagnostics.Log.Warn("起動時に起動するタスクを作れませんでした (スタートアップに登録します)。");
            return created;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"起動時に起動するタスクを作れませんでした: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    /// <summary>タスクの定義。管理者権限なし (LeastPrivilege)・電源につないでいなくても起動・時間の制限なし。</summary>
    internal static string TaskXml(string user, string exe)
    {
        static string Escape(string text) => System.Security.SecurityElement.Escape(text) ?? "";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Meltype をサインインしたらすぐ起動する</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Escape(user)}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{Escape(exe)}</Command><WorkingDirectory>{Escape(Path.GetDirectoryName(exe) ?? "")}</WorkingDirectory></Exec></Actions>
            </Task>
            """;
    }

    private static int Schtasks(params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        if (!process.WaitForExit(10_000))
        {
            process.Kill();
            return -1;
        }
        return process.ExitCode;
    }
}
