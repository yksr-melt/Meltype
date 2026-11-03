// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.Json;

namespace Meltype.Config;

/// <summary>
/// Meltype が覚えておく状態 (state.json)。ユーザーが選ぶ設定 (config.json) とは別のファイルで、プロファイルにも入れない。
/// 変えたら <see cref="Save"/> で書く。
/// </summary>
public sealed class AppState
{
    /// <summary>初めて起動したときの「使い方」を見せたか (見せたら true にして、次からは出さない)。</summary>
    public bool WelcomeShown { get; set; }

    /// <summary>Meltype IME (TSF) が入っているのを見つけて、動作モードを Meltype IME にしたか (一度だけ切り替える。後でユーザーが戻したら、そのまま)。</summary>
    public bool TsfIntroduced { get; set; }

    /// <summary>Meltype IME を入れるかを聞いたか (Meltype IME が入る前の版から更新した人に、起動したときに一度だけ聞く)。</summary>
    public bool TsfOffered { get; set; }

    /// <summary>リリースノートを最後に見せた版 (更新したあとの初めての起動で一度だけ見せる。issue #1207)。</summary>
    public string? ReleaseNotesShown { get; set; }

    /// <summary>設定画面の下に、項目の説明を出すか (設定画面のスイッチで切り替える)。</summary>
    public bool SettingsShowHelp { get; set; } = true;

    /// <summary>設定画面で、プルダウンの上でホイールを回したときに値を変えるか (OFF なら設定の一覧をスクロールする)。</summary>
    public bool SettingsWheelChangesValue { get; set; } = true;

    private string? _path;

    /// <summary>
    /// state.json を読む。無ければ、config.json (v8 より前は、これらの値が config.json に入っていた) から読んで、すぐに state.json に書く。
    /// config.json の移行 (<see cref="Settings.Load"/>) が古い値を消すので、それより前に呼ぶ。
    /// </summary>
    public static AppState Load(string path, string configPath)
    {
        var state = new AppState();
        if (File.Exists(path))
        {
            DataFile.Load(path, "state.json ", json => state = Parse(json));
        }
        else if (File.Exists(configPath))
        {
            DataFile.Load(configPath, "config.json の状態の値", json =>
            {
                state = Parse(json);
                // 動作モードが IME 自動切替 (消した) だった: 初めて起動したときと同じく、Meltype IME を使えなければ Meltype キーボードに戻し、
                // 使えれば入力方式も Meltype IME にする (Program.ShouldFallBackToKeyboard・TrayApplicationContext.IntroduceTsf)
                if (Settings.TopLevelStringIs(json, nameof(Settings.Mode), "AutoSwitch")) state.TsfIntroduced = false;
            });
            state._path = path;
            state.Save();
        }
        state._path = path;
        return state;
    }

    private static AppState Parse(string json) => JsonSerializer.Deserialize(json, AppStateJsonContext.Default.AppState) ?? new AppState();

    public void Save()
    {
        if (_path is not { } path) return;
        try
        {
            DataFile.Write(path, temp => File.WriteAllText(temp, JsonSerializer.Serialize(this, AppStateJsonContext.Default.AppState)));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"state.json を保存できませんでした: {ex.Message}");
        }
    }
}
