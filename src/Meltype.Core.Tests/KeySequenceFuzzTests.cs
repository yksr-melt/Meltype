// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// でたらめなキーの並び (英字・Space・Tab・Esc・BackSpace・矢印・F6〜F10 を Shift の有無と混ぜたもの) を打っても、
/// 例外にならず、最後に Enter を押せば変換ボックスが空になる。報告を待たずに、キーの組み合わせで起きる状態の食い違いを見つける。
/// 種を固定しているので、外れたら同じ並びで再現できる。
/// </summary>
internal static class KeySequenceFuzzTests
{
    private static readonly int[] Keys =
    [
        .. Enumerable.Range('A', 26),
        VirtualKeys.Space, VirtualKeys.Space, VirtualKeys.Tab, VirtualKeys.Escape, VirtualKeys.Back, VirtualKeys.Return,
        VirtualKeys.Left, VirtualKeys.Right, VirtualKeys.Up, VirtualKeys.Down,
        VirtualKeys.F6, VirtualKeys.F7, VirtualKeys.F8, VirtualKeys.F9, VirtualKeys.F10,
        0x2E /* Delete */, '1', '2', 0xBD /* - */, 0xBE /* . */,
    ];

    [Test]
    public static void RandomKeys_NeverThrow_AndEnterEmptiesTheBox()
    {
        var random = new Random(20261123);
        var failures = new List<string>();
        for (var sequence = 0; sequence < 300 && failures.Count < 5; sequence++)
        {
            var keys = Enumerable.Range(0, random.Next(1, 30))
                .Select(_ => (Vk: Keys[random.Next(Keys.Length)], Shift: random.Next(5) == 0)).ToArray();
            var shown = string.Join(" ", keys.Select(k => (k.Shift ? "S+" : "") + VkName(k.Vk)));
            try
            {
                var k = new CompositionTests.Keyboard();
                k.TypeKeys(keys);
                // 候補を選んでいる・予測を選んでいるときの Enter も確定するので、2 回までで空になる
                k.TypeKeys((VirtualKeys.Return, false));
                if (!string.IsNullOrEmpty(k.Showing)) k.TypeKeys((VirtualKeys.Return, false));
                if (!string.IsNullOrEmpty(k.Showing)) failures.Add($"{shown} → Enter のあとも {k.Showing}");
            }
            catch (Exception ex)
            {
                failures.Add($"{shown} → {ex.GetType().Name}: {ex.Message}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static string VkName(int vk) => vk switch
    {
        >= 'A' and <= 'Z' => ((char)(vk + 32)).ToString(),
        VirtualKeys.Space => "Space",
        VirtualKeys.Tab => "Tab",
        VirtualKeys.Escape => "Esc",
        VirtualKeys.Back => "Back",
        VirtualKeys.Return => "Enter",
        VirtualKeys.Left => "←",
        VirtualKeys.Right => "→",
        VirtualKeys.Up => "↑",
        VirtualKeys.Down => "↓",
        _ => $"0x{vk:X2}",
    };
}
