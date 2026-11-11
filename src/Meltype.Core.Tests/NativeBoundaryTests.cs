// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

using System.Runtime.InteropServices;
using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Ime;
using Meltype.Input;
using Meltype.Mac;

namespace Meltype.Tests;

/// <summary>C ABI の実際の入口を呼ぶ。辞書・学習データはスタブで、ユーザーの設定を読み書きしない。</summary>
internal static unsafe class NativeBoundaryTests
{
    /// <summary>入力欄 1 つの本体 (ImeHost) を、C ABI の meltype_ime_handle で呼ぶ。応答の result を返す。</summary>
    private sealed class NativeSession : IDisposable, IImeEnvironment
    {
        private readonly GCHandle _handle;

        public NativeSession() => _handle = GCHandle.Alloc(new ImeHost(this, () => Settings));

        public Settings Settings { get; } = new() { SpaceAroundEnglish = true, LiveConversion = false };

        public InputMode? InputMethod => null;

        public MeltypeSession CreateSession(Func<Settings> settings) => new(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionServices(), settings);

        public string? ReadingOf(string text) => null;

        public SnippetStore Snippets { get; } = new(null);

        public string? ClipboardText => null;

        public (string Title, string Artist)? NowPlaying => null;

        /// <summary>最後の応答で変換中の文字があったか。</summary>
        public bool IsComposing { get; private set; }

        public JsonDocument Key(int scalar, int? vk = null, string? after = null) =>
            // 文字を入力するキーは文字だけを渡す (仮想キーコードは本体が決める)
            Send(vk is null ? $"{{\"ch\":{scalar}}}" : $"{{\"vk\":{vk},\"ch\":{scalar}}}", after);

        /// <summary>1 回のキーで複数のスカラーが入る (Mac)。</summary>
        public JsonDocument Text(string text, string? after = null) => Send($"{{\"text\":{JsonSerializer.Serialize(text)}}}", after);

        private JsonDocument Send(string key, string? after)
        {
            var field = $"{{\"composing\":{(IsComposing ? "true" : "false")}{(after is null ? "" : ",\"after\":" + JsonSerializer.Serialize(after))}}}";
            delegate* unmanaged<IntPtr, byte*, byte*> handle = &Exports.ImeHandle;
            var json = Marshal.StringToCoTaskMemUTF8($"{{\"version\":{ImeHost.Version},\"type\":\"KeyPressed\",\"instance\":\"t\",\"field\":{field},\"key\":{key}}}");
            byte* pointer;
            try { pointer = handle(GCHandle.ToIntPtr(_handle), (byte*)json); }
            finally { Marshal.FreeCoTaskMem(json); }
            Assert.True(pointer != null, "C ABI が有効な JSON を返す");
            try
            {
                using var output = JsonDocument.Parse(Marshal.PtrToStringUTF8((IntPtr)pointer)!);
                var result = JsonDocument.Parse(output.RootElement.GetProperty("result").GetRawText());
                IsComposing = result.RootElement.GetProperty("view").ValueKind == JsonValueKind.Object;
                return result;
            }
            finally
            {
                delegate* unmanaged<byte*, void> free = &Exports.Free;
                free(pointer);
            }
        }

        public void Type(string text, string? after = null)
        {
            foreach (var c in text)
            {
                using var result = Key(c, after: after);
            }
        }

        public void Dispose() => _handle.Free();
    }

    [Test]
    public static void Native_MultiScalarTextCommitsAndPasses()
    {
        // tools/test-mac-boundaries.swift と同じ場面 (Mac は 1 回のキーで入る複数のスカラーを text で渡す)
        foreach (var (raw, text, f6, expected) in new (string, string, bool, string?)[]
                 {
                     ("e", "\u0301\u0300", false, "e"), ("ka", "\u3099\uFE0F", false, "か"), ("ka", "\U000E0100\U000E0101", false, "か"),
                     ("e", "\u0301\u0300", true, "え"), ("ka", "👩‍👩‍👧‍👦", false, "か "), ("ka", "e\u0301", false, "か "),
                     ("@kuraido", "\u0301\u0300", false, "@kuraido"), ("", "\u0301\u0300", false, null),
                 })
        {
            using var native = new NativeSession();
            native.Type(raw, after: "world");
            if (f6) native.Key(0, VirtualKeys.F6).Dispose();
            using var result = native.Text(text, after: "world");
            AssertPassThrough(result, expected);
        }
    }

    private static void AssertPassThrough(JsonDocument result, string? commit)
    {
        var root = result.RootElement;
        Assert.True(!root.GetProperty("consumed").GetBoolean(), "元の OS イベントを 1 回だけ通す");
        var commits = root.GetProperty("commits");
        Assert.Equal(commit is null ? 0 : 1, commits.GetArrayLength(), "確定を重複させない");
        if (commit is not null)
        {
            Assert.Equal(commit, commits[0].GetProperty("text").GetString());
            Assert.True(commits[0].GetProperty("removeBefore").ValueKind == JsonValueKind.Null, "前の文字は消さない");
        }
        Assert.Equal(JsonValueKind.Null, root.GetProperty("view").ValueKind, "未確定表示を閉じる");
    }

    [Test]
    public static void Native_EmptyBracketsKeepThePairThroughCandidateSelection()
    {
        foreach (var (input, count, expected) in new[] { ("[]", 7, "｢｣"), ("()", 2, "（）"), ("{}", 2, "｛｝"), ("<>", 4, "＜＞") })
        {
            using var native = new NativeSession();
            native.Type(input);
            using var conversion = native.Key(' ', VirtualKeys.Space);
            var view = conversion.RootElement.GetProperty("view");
            Assert.True(view.GetProperty("converting").GetBoolean(), input);
            Assert.Equal(1, view.GetProperty("clauses").GetArrayLength(), input);
            Assert.Equal(count, view.GetProperty("candidates").GetProperty("items").GetArrayLength(), input);
            Assert.Equal(expected, view.GetProperty("candidates").GetProperty("items")[1].GetString(), input);
            using var selected = native.Key('2', 0x32);
            Assert.Equal(expected, selected.RootElement.GetProperty("commits")[0].GetProperty("text").GetString(), input);
            Assert.Equal(JsonValueKind.Null, selected.RootElement.GetProperty("view").ValueKind, input);
        }
    }

    [Test]
    public static void Native_BmpCombiningAccentPreservesLatinRaw()
    {
        foreach (var (raw, mark) in new[] { ("e", 0x0301), ("cafe", 0x0301), ("a", 0x0308) })
        {
            using var native = new NativeSession();
            native.Type(raw);
            using var result = native.Key(mark);
            AssertPassThrough(result, raw);
            Assert.True(!native.IsComposing, "アクセントの前に原文を確定する");
            using var secondMark = native.Key(0x0300);
            AssertPassThrough(secondMark, null);
        }
    }

    [Test]
    public static void Native_CombiningMarksKeepExplicitJapaneseAndVariationForms()
    {
        foreach (var mark in new[] { 0x3099, 0xFE0F, 0xE0100 })
        {
            using var native = new NativeSession();
            native.Type("ka");
            using var result = native.Key(mark);
            AssertPassThrough(result, "か");
        }
        using var explicitKana = new NativeSession();
        explicitKana.Type("e");
        using var mode = explicitKana.Key(0, VirtualKeys.F6);
        using var accent = explicitKana.Key(0x0301);
        AssertPassThrough(accent, "え");
    }

    [Test]
    public static void Native_CombiningMarksAttachBeforeAutomaticEnglishSpacing()
    {
        foreach (var mark in new[] { 0x3099, 0xFE0F, 0xE0100 })
        {
            using var native = new NativeSession();
            native.Type("ka", after: "world");
            using var result = native.Key(mark);
            AssertPassThrough(result, "か");
        }
        using var explicitKana = new NativeSession();
        explicitKana.Type("e", after: "world");
        using var mode = explicitKana.Key(0, VirtualKeys.F6);
        using var accent = explicitKana.Key(0x0301);
        AssertPassThrough(accent, "え");
    }

    [Test]
    public static void Native_NormalCommitStillAddsEnglishSpacing()
    {
        using var native = new NativeSession();
        native.Type("ka", after: "world");
        using var result = native.Key(0, VirtualKeys.Return);
        Assert.True(result.RootElement.GetProperty("consumed").GetBoolean(), "通常の Enter は消費する");
        Assert.Equal("か ", result.RootElement.GetProperty("commits")[0].GetProperty("text").GetString());
    }

    [Test]
    public static void Native_SupplementaryScalarCommitsDisplayWithoutTruncating()
    {
        foreach (var scalar in new[] { 0x1F60A, 0x20BB7, 0x10000, 0x10FFFF })
        {
            using var native = new NativeSession();
            native.Type("kyouha");
            using var result = native.Key(scalar);
            AssertPassThrough(result, "きょうは");
            using var empty = native.Key(scalar);
            AssertPassThrough(empty, null);
        }
    }

    [Test]
    public static void Native_InvalidScalarLeavesCompositionUnchanged()
    {
        using var native = new NativeSession();
        using var initial = native.Key('e');
        var view = initial.RootElement.GetProperty("view").GetRawText();
        foreach (var scalar in new[] { -1, 0xD800, 0xDFFF, 0x110000, int.MaxValue })
        {
            using var result = native.Key(scalar);
            Assert.True(!result.RootElement.GetProperty("consumed").GetBoolean(), "不正なスカラーを通す");
            Assert.Equal(0, result.RootElement.GetProperty("commits").GetArrayLength());
            Assert.Equal(view, result.RootElement.GetProperty("view").GetRawText());
            Assert.True(native.IsComposing, "未確定の内容を変えない");
        }
    }
}
