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
        try
        {
            using var runtime = new MeltypeRuntime(directory);
            body(runtime, directory);
        }
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
        });
    }

    [Test]
    public static void UserDictionary_ExternalWriteReachesOpenSessions()
    {
        // Mac は取り込んだ語を Swift で userdict.txt に書く。Meltype キーボードは入力欄が変わっても同じ本体を使うので、入力を始めたときに確かめる
        WithRuntime((runtime, directory) =>
        {
            var now = 0L;
            var session = new MeltypeSession(runtime.Detector, new CompositionTests.FakeConverter(), new CompositionServices { UserDictionary = runtime.UserDictionary },
                () => runtime.Settings.Current, timeMs: () => now);
            runtime.UserDictionary.Add("あおい", "葵");
            var path = Path.Combine(directory, "userdict.txt");
            File.WriteAllText(path, "あおい\t葵\nゆきしろ\t雪代\n");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            now = 1000;
            session.HandleKey('A', 'a', false, false, false, false);
            Assert.Equal("雪代", runtime.UserDictionary.Lookup("ゆきしろ") is [var word, ..] ? word : "(なし)", "開いている入力欄で読み直す");
            Assert.Equal("葵", runtime.UserDictionary.Lookup("あおい") is [var registered, ..] ? registered : "(なし)", "自分で登録した語も残る");
        });
    }

    [Test]
    public static void UsableSettings_AppliesToEveryRead()
    {
        // OS で使えない設定 (Windows の使えない Meltype IME) は、起動したとき・外で書き換えたとき・設定画面で変えたときのどれでも直す
        var directory = Path.Combine(Path.GetTempPath(), "meltype-runtime-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "config.json");
            Directory.CreateDirectory(directory);
            new Settings { Mode = InputMode.Tsf }.Save(path);
            using var runtime = new MeltypeRuntime(directory, usableSettings: settings =>
            {
                if (settings.Mode != InputMode.Tsf) return settings;
                var usable = settings.Clone();
                usable.Mode = InputMode.Keyboard;
                return usable;
            });
            Assert.Equal(InputMode.Keyboard, runtime.Settings.Current.Mode, "起動したとき (保存できなかった設定を読んでも)");
            Settings? notified = null;
            runtime.SettingsChanged += next => notified = next;
            var next = runtime.Settings.Current.Clone();
            next.Mode = InputMode.Tsf;
            runtime.Settings.Apply(next);
            Assert.Equal(InputMode.Keyboard, notified?.Mode ?? InputMode.Tsf, "設定画面で変えたとき");
            notified = null;
            next.Punctuation = PunctuationStyle.FullWidthCommaPeriod;
            next.Save(path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            // 入力しなくても、MeltypeRuntime が自分で確かめて知らせる
            var deadline = Environment.TickCount64 + SettingsFile.CheckIntervalMs * 3;
            while (Volatile.Read(ref notified) is null && Environment.TickCount64 < deadline) Thread.Sleep(20);
            Assert.Equal(PunctuationStyle.FullWidthCommaPeriod, notified?.Punctuation ?? PunctuationStyle.Japanese, "外で書き換えたら、入力しなくても知らせる");
            Assert.Equal(InputMode.Keyboard, notified?.Mode ?? InputMode.Tsf, "外で書き換えたときも直す");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
