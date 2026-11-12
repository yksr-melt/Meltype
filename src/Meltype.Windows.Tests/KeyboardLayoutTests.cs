// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 MedeiaBeliar

namespace Meltype.Tests;

internal static class KeyboardLayoutTests
{
    [Test]
    public static void JapaneseLayout_ChecksLanguageRatherThanPhysicalLayout()
    {
        foreach (var hkl in new[] { 0x0411, 0x04110411, unchecked((int)0xE0010411) })
            Assert.True(Keyboard.KeyboardFront.IsJapaneseLayout(new IntPtr(hkl)), "日本語の配列・IME");
        foreach (var hkl in new[] { 0, 0x04090409, unchecked((int)0xE0010412), 0x04110412, 0x08040804 })
            Assert.True(!Keyboard.KeyboardFront.IsJapaneseLayout(new IntPtr(hkl)), "英語・韓国語・中国語・取得失敗");
    }
}
