// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Konayukiw

namespace Meltype.Config;

/// <summary>
/// 確定した文字をクリップボード経由で入力する (#95修正)。
///
/// Qt Windowsは、KEYEVENTF_UNICODEで送った文字のkeyupがアプリに届かないと
/// 2文字目以降を「1文字目のリピート」と判定して最初の1文字を入力文字数分繰り返す。
/// この誤判定は「同じ wParam VK_PACKETのkeydownが続く」ことが条件なので、
/// 文字をキーとして送らない貼り付けなら原理的に起きない (参考: tdesktop#26643、WinCompose#512)。
/// </summary>

public static class PastePolicy
{
    public static bool IsQtWindowClass(string? className) =>
    className is { Length: >= 3 } &&
    className[0] == 'Q' && className[1] == 't' && className[2] is >= '0' and <= '9';

    public static bool ShouldPaste(Settings settings, string? processName, bool isQtApp) => settings.CommitMethodFor(processName) switch
    {
        CommitMethod.Paste => true,
        CommitMethod.Keys => false,
        CommitMethod.Auto => isQtApp,
        var method => throw new InvalidOperationException($"不明な CommitMethod: {method}"),
    };
}
