// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>どの OS のアプリも 1 つ作る土台 (<see cref="MeltypeRuntime"/>)。</summary>
internal static class RuntimeTests
{
    private static void WithRuntime(Action<MeltypeRuntime, string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-runtime-" + Guid.NewGuid().ToString("N"));
        try { body(new MeltypeRuntime(directory), directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void DirectModeDetection_ReturnsRomajiToJapanese()
    {
        // 英数状態で打ち始めた語がローマ字なら日本語に戻す (設定「英数状態で自動判定」)。Mac・Linux ではまだ判定しない (#1724)
        if (!OperatingSystem.IsWindows()) return;
        WithRuntime((runtime, _) =>
        {
            var session = runtime.CreateSession(new CompositionTests.FakeConverter(), directModeDetection: true);
            session.Direct = true;
            foreach (var c in "konnnichiha") session.HandleKey(char.ToUpperInvariant(c), c, false, false, false, false);
            Assert.True(!session.Direct && session.IsComposing, "ローマ字だと分かったら日本語入力に戻る");
            var english = runtime.CreateSession(new CompositionTests.FakeConverter(), directModeDetection: true);
            english.Direct = true;
            foreach (var c in "hello ") english.HandleKey(c == ' ' ? 0x20 : char.ToUpperInvariant(c), c, false, false, false, false);
            Assert.True(english.Direct, "英語なら英数のまま");
        });
    }

    [Test]
    public static void UserDictionary_PlistIsReadForTheNextSession()
    {
        // dictionaries/ に置いた macOS の「ユーザ辞書」の .plist は、次に作る入力欄から使う (#40)。Windows でも同じ
        WithRuntime((runtime, directory) =>
        {
            Assert.True(runtime.UserDictionary.Lookup("ゆきしろ").Count == 0, "置く前は無い");
            var dictionaries = Path.Combine(directory, "dictionaries");
            Directory.CreateDirectory(dictionaries);
            File.WriteAllText(Path.Combine(dictionaries, "mac.plist"), """
                <?xml version="1.0" encoding="UTF-8"?>
                <plist version="1.0"><array><dict><key>phrase</key><string>雪代</string><key>shortcut</key><string>ゆきしろ</string></dict></array></plist>
                """);
            runtime.CreateSession(new CompositionTests.FakeConverter());
            Assert.Equal("雪代", runtime.UserDictionary.Lookup("ゆきしろ") is [var word, ..] ? word : "(なし)");
            var before = runtime.UserDictionary;
            runtime.CreateSession(new CompositionTests.FakeConverter());
            Assert.True(ReferenceEquals(before, runtime.UserDictionary), "変わらなければ読み直さない (登録の画面の語を、開いている入力欄でもすぐ使う)");
        });
    }

    [Test]
    public static void SettingsApply_SavesAndNotifies()
    {
        WithRuntime((runtime, directory) =>
        {
            Settings? notified = null;
            runtime.SettingsChanged += next => notified = next;
            var next = runtime.Settings.Current.Clone();
            next.Punctuation = PunctuationStyle.FullWidthCommaPeriod;
            runtime.Settings.Apply(next);
            Assert.Equal(PunctuationStyle.FullWidthCommaPeriod, notified?.Punctuation ?? PunctuationStyle.Japanese, "変えたことを知らせる");
            Assert.Equal(PunctuationStyle.FullWidthCommaPeriod, Settings.Load(Path.Combine(directory, "config.json")).Punctuation, "config.json に保存する");
            Assert.Equal(PunctuationStyle.FullWidthCommaPeriod, runtime.Settings.Current.Punctuation);
        });
    }
}
