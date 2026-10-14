// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 MedeiaBeliar

using Meltype.Config;
using Meltype.IME;
using Meltype.Input;

namespace Meltype.Tests;

internal static class KeyboardLayoutTests
{
    [Test]
    public static void JapaneseOnly_ChecksLanguageRatherThanPhysicalLayout()
    {
        var settings = new Settings { JapaneseKeyboardOnly = true };
        foreach (var hkl in new[] { 0x0411, 0x04110411, unchecked((int)0xE0010411) })
            Assert.True(KeyboardLayoutPolicy.AllowsInput(settings, new IntPtr(hkl)), "日本語の配列・IME を許可する");
        foreach (var hkl in new[] { 0, 0x04090409, unchecked((int)0xE0010412), 0x04110412, 0x08040804 })
            Assert.True(!KeyboardLayoutPolicy.AllowsInput(settings, new IntPtr(hkl)), "英語・韓国語・中国語・取得失敗は通す");
        Assert.True(!KeyboardLayoutPolicy.AllowsInput(settings, (ImeTarget?)null), "入力先がなければ動作しない");

        settings.JapaneseKeyboardOnly = false;
        Assert.True(KeyboardLayoutPolicy.AllowsInput(settings, new IntPtr(0x04090409)), "OFF なら従来どおり");
        Assert.True(KeyboardLayoutPolicy.AllowsInput(settings, IntPtr.Zero), "OFF なら入力言語を制限しない");
    }
}
