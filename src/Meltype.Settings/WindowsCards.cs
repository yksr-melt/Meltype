// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Meltype.Config;

namespace Meltype.SettingsApp;

/// <summary>Windows だけのカード: 動いている Meltype.exe の画面を開くもの (<see cref="AppWindow"/>) と、起動・アンインストール。</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsCards
{
    /// <summary>ホーム (<see cref="AboutPage"/>) の設定の下に並べるカード: 終了・使い方・報告。</summary>
    public static IEnumerable<Control> Home()
    {
        var message = Pages.Message();
        yield return Row("Meltype を終了", "入力の処理をやめます。もう一度使うときは、スタートメニューの「Meltype」を開きます。",
            SettingEditors.Button("終了", () => Exit(message)), message);
        yield return WindowCard("使い方", "基本のキーと、思いどおりにならないときのキーです。", AppWindow.Guide);
        yield return WindowCard("不具合の報告・提案", "実行環境とログを確かめてから、GitHub で報告できます。", AppWindow.Report);
        yield return WindowCard("直した誤判定を報告", "最近自分で直した判定を、中身を確かめてから GitHub で報告できます。記録は Meltype を終了すると消えます。", AppWindow.Corrections);
    }

    /// <summary>
    /// 入力する Meltype (Meltype.exe) を起動する。設定アプリを開いたら常に呼ぶ (スタートメニューの「Meltype」は設定アプリ)。
    /// Meltype.exe は多重起動しないので、もう動いていれば何もせずに終わる。
    /// </summary>
    public static void StartMeltype()
    {
        try
        {
            Process.Start(new ProcessStartInfo(MeltypeExe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            // Meltype.Settings だけをビルドして動かしているとき (Meltype.exe は別のプロジェクト)
            Diagnostics.Log.Warn($"Meltype を起動できませんでした: {ex.Message}");
        }
    }

    private static string MeltypeExe => Path.Combine(AppContext.BaseDirectory, "Meltype.exe");

    // 終了の合図 (Meltype.exe --exit) は、学習データを書いて終わるまで待つ
    private static async void Exit(TextBlock message)
    {
        Pages.Show(message, "");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(MeltypeExe, ["--exit"]) { UseShellExecute = false })!;
            await process.WaitForExitAsync();
            Pages.Show(message, process.ExitCode == 0 ? "終了しました。" : "Meltype は動いていません。");
        }
        catch (Win32Exception ex)
        {
            Pages.Show(message, $"Meltype.exe を実行できませんでした: {ex.Message}", error: true);
        }
    }

    /// <summary>設定の項目のすぐ後ろに並べるカード。ポータブル版は起動の登録・Meltype IME・更新を扱わない。</summary>
    public static IEnumerable<Control> After(SettingItem item) => item.Property.Name switch
    {
        nameof(Settings.KeyCount) => [WindowCard("打鍵数を見る", "キーごと・日ごと・時間帯ごとの打鍵数です。", AppWindow.KeyCount)],
        nameof(Settings.FileLog) => [WindowCard("ログ / 判定理由", "動作のログと、打った英字を日本語・英語のどちらと判定したかの理由です。", AppWindow.Log)],
        _ when AppPaths.IsPortable => [],
        nameof(Settings.Enabled) => [StartupCard()],
        nameof(Settings.Mode) => [WindowCard("Meltype IME を使う", "動作モードを Meltype IME にします。Windows に登録されていなければ登録します (管理者権限の確認が出ます)。", AppWindow.RegisterIme, "使う…")],
        nameof(Settings.AutoUpdate) => [WindowCard("今すぐ更新を確認", "新しい版があれば、ダウンロードしてから、更新するかを聞きます。結果は別の画面に出ます。", AppWindow.CheckForUpdate, "確認")],
        nameof(Settings.ShowReleaseNotesAfterUpdate) => [WindowCard("リリースノート", "今の版で変わったことです。", AppWindow.ReleaseNotes)],
        _ => [],
    };

    public static Border Uninstall()
    {
        var message = Pages.Message();
        // 確認はアンインストーラーが出す
        var start = SettingEditors.Button("アンインストール…", () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "unins000.exe")) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() })?.Dispose();
            }
            catch (Win32Exception ex)
            {
                Pages.Show(message, $"アンインストールを始められませんでした: {ex.Message}\nWindows の「設定」→「アプリ」→「インストールされているアプリ」からアンインストールしてください。", error: true);
            }
        });
        return Row("アンインストール", "Meltype を PC から取り除きます。", start, message);
    }

    private static Border StartupCard()
    {
        var message = Pages.Message();
        // 今の登録を調べ終えるまで押せない
        var toggle = new ToggleSwitch { OnContent = null, OffContent = null, IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
        Load();
        return Row("Windows の起動時に起動", "サインインしたらすぐ起動するよう、タスク スケジューラに「Meltype」というタスクを登録します (OFF にすると消します)。", toggle, message);

        async void Load()
        {
            try
            {
                toggle.IsChecked = await Task.Run(WindowsStartup.IsEnabled);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
            {
                Pages.Show(message, $"調べられませんでした: {ex.Message}", error: true);
                return;
            }
            toggle.IsCheckedChanged += Save;
            toggle.IsEnabled = true;
        }

        async void Save(object? sender, EventArgs e)
        {
            var enabled = toggle.IsChecked == true;
            toggle.IsEnabled = false;
            Pages.Show(message, "");
            try
            {
                await Task.Run(() => WindowsStartup.Set(enabled));
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
            {
                Pages.Show(message, $"変えられませんでした: {ex.Message}", error: true);
            }
            // schtasks.exe が失敗しても Set は例外を出さないので、実際の登録を読み直して出す
            toggle.IsCheckedChanged -= Save;
            Load();
        }
    }

    /// <summary>動いている Meltype.exe の画面を開くカード。Meltype が動いていなければ、カードにそう出す。</summary>
    private static Border WindowCard(string title, string description, AppWindow window, string action = "開く…")
    {
        var message = Pages.Message();
        return Row(title, description, SettingEditors.Button(action, () => Open(window, message)), message);
    }

    private static Border Row(string title, string description, Control control, TextBlock message)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24 };
        var hint = Pages.Hint(description);
        // 設定の項目のカード (SettingsPage.Card) の説明と同じ大きさ
        hint.FontSize = 12;
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { Pages.Heading(title), hint, message } };
        AutomationProperties.SetName(control, title);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return new Border { Classes = { "card" }, Child = grid };
    }

    /// <summary>動いている Meltype.exe に画面を開いてもらう (Meltype.exe --open)。動いていなければ message に出す。</summary>
    private static async void Open(AppWindow window, TextBlock message)
    {
        Pages.Show(message, "");
        try
        {
            var start = new ProcessStartInfo(MeltypeExe, ["--open", window.ToString()]) { UseShellExecute = false };
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
            switch (process.ExitCode)
            {
                case 0: break;
                case 1: Pages.Show(message, "Meltype が動いていません。スタートメニューの「Meltype」を開き直してから、もう一度押してください。", error: true); break;
                // Meltype.exe と設定アプリの版が違う (Meltype.exe がこの画面を知らない)
                default: Pages.Show(message, $"Meltype.exe がこの画面を開けませんでした (終了コード {process.ExitCode})。Meltype を入れ直してください。", error: true); break;
            }
        }
        catch (Win32Exception ex)
        {
            Pages.Show(message, $"Meltype.exe を実行できませんでした: {ex.Message}", error: true);
        }
    }
}
