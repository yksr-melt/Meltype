// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
namespace Meltype.Input;

/// <summary>
/// 設定に書くキーの組み合わせ (Ctrl+Space・Alt+Backquote・Shift+F10 など)。日本語 / 英数の切り替えキーを増やすのに使う (issue #530・#209)。
/// 複数は ; か , で区切る。修飾キーは Ctrl・Alt・Shift・Win。キー名は A〜Z・0〜9・F1〜F12・Space・Tab・Enter・Esc・Backquote・
/// Muhenkan・Henkan・Kanji (半角/全角)・CapsLock・Eisu (JIS キーボードの 英数 キー。Shift を押すと CapsLock になる)。読めない書き方は飛ばす。
/// </summary>
public readonly record struct KeyShortcut(int Vk, bool Ctrl, bool Alt, bool Shift, bool Win)
{
    private static readonly Dictionary<string, int> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Esc"] = 0x1B, ["Backquote"] = 0xC0, ["Grave"] = 0xC0,
        ["Muhenkan"] = 0x1D, ["Henkan"] = 0x1C, ["Kanji"] = 0x19, ["CapsLock"] = 0x14,
        // JIS キーボードの 英数 キー (CapsLock の刻印のキー)。Shift なしは VK_OEM_ATTN、Shift+英数 は CapsLock (issue #955)
        ["Eisu"] = 0xF0,
    };

    /// <summary>「Ctrl+Space; Alt+Backquote」のような文字列を読む。読めない項目は飛ばす。</summary>
    public static List<KeyShortcut> ParseList(string? text)
    {
        var result = new List<KeyShortcut>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var part in text.Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParse(part, out var shortcut)) result.Add(shortcut);
        }
        return result;
    }

    public static bool TryParse(string text, out KeyShortcut shortcut)
    {
        shortcut = default;
        bool ctrl = false, alt = false, shift = false, win = false;
        int? vk = null;
        foreach (var token in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "shift": shift = true; continue;
                case "win" or "windows": win = true; continue;
            }
            if (vk is not null) return false;
            if (token.Length == 1 && char.IsAsciiLetterOrDigit(token[0])) vk = char.ToUpperInvariant(token[0]);
            else if (Names.TryGetValue(token, out var named)) vk = named;
            else if (token.Length is 2 or 3 && (token[0] is 'F' or 'f') && int.TryParse(token[1..], out var f) && f is >= 1 and <= 12) vk = 0x70 + f - 1;
            else return false;
        }
        if (vk is null) return false;
        shortcut = new KeyShortcut(vk.Value, ctrl, alt, shift, win);
        return true;
    }

    /// <summary>押されたキーと修飾キーの状態がこの組み合わせか。</summary>
    public bool Matches(int vk, bool ctrl, bool alt, bool shift, bool win) =>
        vk == Vk && ctrl == Ctrl && alt == Alt && shift == Shift && win == Win;
}
