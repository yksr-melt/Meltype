// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>ユーザー定義の Erlang 用プロファイルを、OS を通さず Core の入力・確定契約で検証する。</summary>
internal static class ErlangCodeProfileTests
{
    // 通常のプロファイル追加・切替・保存を使うテスト用設定。
    // BuiltIn をすべて無効にして % だけを登録し、# のレコード構文をコードのまま扱えることを確かめる。
    private static Settings LoadErlangProfile(bool percentEnabled = true)
    {
        var settings = new Settings().Normalize().AddProfile("Erlang")!;
        settings.CodeSingleComments.BuiltIn = new BuiltInCodeSingleComments
        {
            SlashSlash = false,
            Hash = false,
            DoubleHyphen = false,
            Rem = false,
        };
        settings.CodeSingleComments.Custom = [new() { Token = "%", Enabled = percentEnabled }];
        settings = settings.SwitchProfile(Settings.DefaultProfileName);
        Assert.Equal(0, settings.CodeSingleComments.Custom.Count, "標準には独自コメントを登録していない");
        Assert.True(settings.CodeSingleComments.BuiltIn.Hash, "標準の # 設定を変えない");

        var directory = Path.Combine(Path.GetTempPath(), $"meltype-erlang-profile-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "config.json");
        try
        {
            settings.Save(path);
            settings = Settings.Load(path);
            Assert.Equal(Settings.DefaultProfileName, settings.ActiveProfile);
            settings = settings.SwitchProfile("Erlang");
            settings.Save(path);
            settings = Settings.Load(path);
            Assert.Equal("Erlang", settings.ActiveProfile);
            var comments = settings.CodeSingleComments;
            Assert.True(!comments.BuiltIn.SlashSlash && !comments.BuiltIn.Hash &&
                !comments.BuiltIn.DoubleHyphen && !comments.BuiltIn.Rem, "既存の行コメントはすべて無効");
            Assert.Equal("%", comments.Custom.Single().Token);
            Assert.Equal(percentEnabled, comments.Custom.Single().Enabled);
            return settings;
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    // 未確定は IsComposing / View、確定要求は Commits、直接入力は Consumed=false で検証する。
    // アプリへ渡ったキーと確定文字列は Text に反映する。実 OS の表示・入力反映は検証対象外。
    private sealed class InputHarness
    {
        public MeltypeSession Session { get; }
        public string Text { get; private set; } = "";
        private readonly bool _surrounding;

        public InputHarness(Func<Settings> settings, bool surrounding)
        {
            _surrounding = surrounding;
            Session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
                new CompositionOptions(), settings) { CodeInput = true };
        }

        public SessionResult Press(char c)
        {
            var vk = c switch
            {
                ' ' => VirtualKeys.Space,
                '\n' => VirtualKeys.Return,
                '#' => '3',
                '%' => '5',
                '(' => '9',
                ')' => '0',
                '{' => 0xDB,
                '}' => 0xDD,
                '=' => 0xBB,
                '-' => VirtualKeys.OemMinus,
                ',' => VirtualKeys.OemComma,
                '.' => VirtualKeys.OemPeriod,
                _ when char.IsAsciiLetter(c) => char.ToUpperInvariant(c),
                _ => c,
            };
            var shift = char.IsAsciiLetterUpper(c) || c is '#' or '%' or '(' or ')' or '{' or '}';
            var result = Session.HandleKey(vk, c is ' ' or '\n' ? null : c, shift, false, false, false,
                _surrounding ? Text : null);
            foreach (var edit in result.Commits)
                Text = Text[..Math.Max(0, Text.Length - edit.DeleteBefore)] + edit.Text;
            if (!result.Consumed) Text += c;
            return result;
        }

        public void AssertDirect(string input)
        {
            foreach (var c in input)
            {
                var context = $"{input}, surrounding={_surrounding}, before={Text}, key={c}";
                var result = Press(c);
                Assert.True(!result.Consumed, context + ": キーは直接入力");
                Assert.True(result.View is null, context + ": 未確定表示なし");
                Assert.True(!Session.IsComposing, context + ": 未確定入力なし");
                Assert.Equal(0, result.Commits.Count, context + ": 確定文字を生成しない");
            }
        }
    }

    [Test]
    public static void Erlang_ProfileSwitchUpdatesTheSessionCommentSettings()
    {
        var settings = LoadErlangProfile().SwitchProfile(Settings.DefaultProfileName);
        var input = new InputHarness(() => settings, surrounding: true);
        Assert.True(!input.Session.CodeEnglishAt("# "), "標準では # コメントが有効");
        Assert.True(input.Session.CodeEnglishAt("% "), "標準では % を登録していない");

        settings = settings.SwitchProfile("Erlang");
        Assert.True(input.Session.CodeEnglishAt("# "), "Erlang では # はコード");
        Assert.True(!input.Session.CodeEnglishAt("% "), "Erlang では % はコメント");

        settings = settings.SwitchProfile(Settings.DefaultProfileName);
        Assert.True(!input.Session.CodeEnglishAt("# "), "標準へ戻すと # コメントも戻る");
        Assert.True(input.Session.CodeEnglishAt("% "), "Erlang の % 設定は標準へ漏れない");
    }

    [Test]
    public static void Erlang_RecordSyntaxRemainsDirectDuringSequentialInput()
    {
        var settings = LoadErlangProfile();
        foreach (var surrounding in new[] { true, false })
        foreach (var code in new[]
        {
            "-record(person,{name,age}).",
            "#person{name=taro,age=20}",
            "Person#person{age=21}",
            "Person#person.name",
        })
        {
            var input = new InputHarness(() => settings, surrounding);
            input.AssertDirect(code);
            input.AssertDirect("\n");
            Assert.Equal(code + "\n", input.Text, "Enter でも遅れて確定せず、そのまま入力する");
        }
    }

    [Test]
    public static void Erlang_PercentCommentComposesCommitsAndEndsAtANewline()
    {
        var settings = LoadErlangProfile();
        foreach (var surrounding in new[] { true, false })
        {
            var input = new InputHarness(() => settings, surrounding);
            input.AssertDirect("% ");
            SessionResult? last = null;
            foreach (var c in "kyouha")
            {
                last = input.Press(c);
                Assert.True(last.Consumed, "コメント内のキーは変換ボックスで扱う");
                Assert.True(last.View is not null && input.Session.IsComposing, "コメント内の日本語は未確定");
                Assert.Equal(0, last.Commits.Count, "入力途中では確定文字を返さない");
            }
            Assert.Equal("きょうは", last!.View!.Text);
            Assert.Equal("% ", input.Text, "未確定の日本語はアプリの確定文字列に含まない");

            var enter = input.Press('\n');
            Assert.True(enter.Consumed, "最初の Enter は改行せず日本語を確定する");
            Assert.Equal("きょうは", enter.Commits.Single().Text);
            Assert.True(enter.View is null && !input.Session.IsComposing, "確定後は未確定表示・入力状態ともに解除");
            Assert.Equal("% きょうは", input.Text);

            input.AssertDirect("\n");
            input.AssertDirect("#person{name=taro,age=20}");
            input.AssertDirect("\n");
            Assert.Equal("% きょうは\n#person{name=taro,age=20}\n", input.Text, "コメントは次の行へ継続しない");
        }
    }

    [Test]
    public static void Erlang_DisabledPercentCommentRemainsDirect()
    {
        var settings = LoadErlangProfile(percentEnabled: false);
        foreach (var surrounding in new[] { true, false })
        {
            var input = new InputHarness(() => settings, surrounding);
            input.AssertDirect("% kyouha\n");
            Assert.Equal("% kyouha\n", input.Text, "無効にした % では日本語入力を開始しない");
        }
    }
}
