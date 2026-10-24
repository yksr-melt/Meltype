// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W

namespace Meltype.Input;

/// <summary>Control キーの割り当てを、入力処理とショートカットの競合検証で同じ規則で読み取る。</summary>
internal static class ControlKeyBindingParser
{
    private static readonly Dictionary<string, int> BindableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F6"] = VirtualKeys.F6, ["F7"] = VirtualKeys.F7, ["F8"] = VirtualKeys.F8, ["F9"] = VirtualKeys.F9, ["F10"] = VirtualKeys.F10,
        ["Left"] = VirtualKeys.Left, ["Right"] = VirtualKeys.Right, ["Up"] = VirtualKeys.Up, ["Down"] = VirtualKeys.Down,
        ["Home"] = 0x24, ["End"] = 0x23, ["Delete"] = 0x2E, ["Back"] = VirtualKeys.Back, ["Enter"] = VirtualKeys.Return,
        ["Esc"] = VirtualKeys.Escape, ["Tab"] = VirtualKeys.Tab,
    };

    /// <summary>英字=キー名の一覧を読む。不正な項目は無視し、同じ英字の有効な割り当ては後のものを優先する。</summary>
    internal static Dictionary<int, int> Parse(string? text)
    {
        var result = new Dictionary<int, int>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var part in text.Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && pair[0].Length == 1 && char.IsAsciiLetter(pair[0][0]) && BindableKeys.TryGetValue(pair[1], out var target))
                result[char.ToUpperInvariant(pair[0][0])] = target;
        }
        return result;
    }
}
