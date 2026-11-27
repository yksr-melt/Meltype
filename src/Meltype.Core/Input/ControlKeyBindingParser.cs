// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 A6P3W

namespace Meltype.Input;

/// <summary>Control + 英字に割り当てた働き。</summary>
internal abstract record ControlKeyTarget
{
    private ControlKeyTarget() { }

    /// <summary>別のキー (F6〜F10・矢印・Enter など) と同じ働き。</summary>
    public sealed record Key(int Vk) : ControlKeyTarget;

    /// <summary>表示がかなだけなら半角カタカナ (F8)、それ以外は半角英数 (F10)。</summary>
    public sealed record HalfWidth : ControlKeyTarget;
}

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

    private static ControlKeyTarget? Target(string name) =>
        name.Equals("HalfWidth", StringComparison.OrdinalIgnoreCase) ? new ControlKeyTarget.HalfWidth()
        : BindableKeys.TryGetValue(name, out var vk) ? new ControlKeyTarget.Key(vk) : null;

    /// <summary>英字=キー名の一覧を読む。不正な項目は無視し、同じ英字の有効な割り当ては後のものを優先する。</summary>
    internal static Dictionary<int, ControlKeyTarget> Parse(string? text)
    {
        var result = new Dictionary<int, ControlKeyTarget>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var part in text.Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && pair[0].Length == 1 && char.IsAsciiLetter(pair[0][0]) && Target(pair[1]) is { } target)
                result[char.ToUpperInvariant(pair[0][0])] = target;
        }
        return result;
    }
}
