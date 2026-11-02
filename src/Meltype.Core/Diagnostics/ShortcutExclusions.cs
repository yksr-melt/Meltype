// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W

using Meltype.Input;

namespace Meltype.Diagnostics;

/// <summary>正規化で除外したキー登録と、その理由・読み込み元のプロファイル。</summary>
internal sealed record ShortcutExclusion(ToggleShortcut? Shortcut, ShortcutValidationError Reason, string Profile);

/// <summary>トレイの生成前やプロファイルの読み込み途中に除外した登録も、通知まで保持する。</summary>
internal static class ShortcutExclusions
{
    private static readonly object Gate = new();
    private static readonly List<ShortcutExclusion> Pending = [];

    /// <summary>通常の通知より優先して知らせるべき、未通知の除外情報があるか。</summary>
    internal static bool HasPending
    {
        get { lock (Gate) return Pending.Count > 0; }
    }

    /// <summary>除外情報を追加したことを、記録したスレッドから通知する。</summary>
    internal static event Action? Added;

    /// <summary>除外したキー・理由・プロファイルを警告ログに記録し、トレイ通知用の情報を追加する。</summary>
    internal static void Report(ToggleShortcut? shortcut, ShortcutValidationError reason, string profile)
    {
        var detail = shortcut is null ? "null" : $"Key={shortcut.Key} (0x{shortcut.Key:X}), Modifiers={shortcut.Modifiers} ({(int)shortcut.Modifiers})";
        var explanation = reason switch
        {
            ShortcutValidationError.Duplicate => "同じショートカットが登録済み",
            ShortcutValidationError.Conversion or ShortcutValidationError.FunctionKey => "既存の変換操作と競合",
            ShortcutValidationError.ControlKeyBinding => "「Control キーの割り当て」と競合",
            ShortcutValidationError.ConvertKeys => "「無変換で英数 / 変換で日本語」と競合",
            ShortcutValidationError.UnmodifiedKey => "通常の文字入力に使うキー",
            _ => "不正なキー登録",
        };
        Log.Warn($"入力モード切り替えキーを除外しました: {detail}; 理由: {explanation} ({reason}); プロファイル: {profile}");
        lock (Gate) Pending.Add(new(shortcut, reason, profile));
        Added?.Invoke();
    }

    /// <summary>未通知の除外情報をまとめて取り出し、次の呼び出しで重複して返さないよう一覧を空にする。</summary>
    internal static ShortcutExclusion[] Drain()
    {
        lock (Gate)
        {
            var result = Pending.ToArray();
            Pending.Clear();
            return result;
        }
    }
}
