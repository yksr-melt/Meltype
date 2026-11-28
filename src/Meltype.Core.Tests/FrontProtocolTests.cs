// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.Json;
using Meltype.Composition;
using Meltype.Ime;

namespace Meltype.Tests;

/// <summary>
/// 本体の応答 (<see cref="ImeOutput"/> の JSON) の項目と、JSON を読むフロントのソースが読む名前を突き合わせる。
/// 本体に項目を足したら、フロントごとに読むか Unused に入れるかを決める。Meltype キーボードは C# の型で受け取るので対象にしない。
/// </summary>
internal static class FrontProtocolTests
{
    /// <summary>
    /// JSON の項目を読む書き方。ソースを字句に分けてから当てはめるので、コメントと文字列の中身・空白・改行の違いに左右されない。
    /// </summary>
    /// <param name="Calls">最初の引数の文字列が項目名になる関数 (<c>get("x")</c>)。</param>
    /// <param name="Index">添字の文字列が項目名になる (<c>o["x"]</c>)。リストの字句 (<c>["x"]</c>) は添字でないので数えない。</param>
    /// <param name="Fields">型の宣言のフィールド名が項目名になる (Swift の <c>Decodable</c>: <c>let x:</c>)。</param>
    private sealed record Reader(string[] Calls, bool Index = false, bool Fields = false);

    /// <param name="Unused">本体が出すが、このフロントが使わない項目。</param>
    /// <param name="Others">ソースから取り出せてしまう、本体の応答でない名前。</param>
    private sealed record FrontSource(string Name, string Path, Func<string, string> Region, Reader Reads, string[] Unused, string[] Others);

    private static readonly FrontSource[] Fronts =
    [
        new("Meltype IME (DLL)", "native/tip/TextService.cpp", source => source, new([], Index: true),
            // 入力モードと、左右の Alt (フックで見る) は Meltype.exe (TipServer) が使う
            Unused: ["version", "modeIndicatorMs", "insertByPaste", "keyReleases", "altKeys"],
            // Meltype.exe (TipServer) の応答: 背景の変換の結果の待ち合わせ、別ビルドの impl への応答、スムーズカーソルの有効状態
            Others: ["generation", "stale", "smoothCaret"]),
        new("fcitx5", "linux/fcitx5/meltype.cpp", source => source, new(["get", "str", "flag", "integer", "strings"]),
            Unused: ["version", "handlesInput", "imeOpen", "printableAscii", "printableAsciiExcept", "shortcuts", "vk", "modifiers",
                "ctrlKeysWhileComposing", "revision", "currentWord", "insertByPaste", "keyReleases", "colors"],
            Others: []),
        new("IBus", "linux/ibus-engine-meltype", source => source, new(["get"], Index: true),
            Unused: ["version", "handlesInput", "imeOpen", "printableAscii", "printableAsciiExcept", "shortcuts", "vk", "modifiers",
                "ctrlKeysWhileComposing", "revision", "currentWord", "insertByPaste", "keyReleases", "colors"],
            // 送る入力欄の事実
            Others: ["app", "selection"]),
        new("端末", "tui/meltype-tty", source => source, new([], Index: true),
            // 端末はキーをすべて送り (capture を見ない)、canRemove: false なので removeBefore は来ない。案内 (hint) は端末に届かないキーも書くので出さない。変換中の文字は行に重ねて出すだけで、中のカーソルは描かない
            Unused: ["version", "state", "handlesInput", "imeOpen", "languageMode", "modeIndicatorMs", "capture", "printableAscii", "printableAsciiExcept",
                "shortcuts", "vk", "modifiers", "ctrlKeysWhileComposing", "altKeys", "keyReleases", "revision", "currentWord", "insertByPaste",
                "removeBefore", "hint", "notes", "colors", "selectedMeaning", "meaningDelayMs", "caretBack"],
            Others: []),
        new("Android", "android/app/src/main/kotlin/app/meltype/keyboard/core/ImeJson.kt", source => source,
            new(["getString", "getInt", "getBoolean", "getJSONObject", "getJSONArray", "optString", "optInt", "optBoolean", "optJSONObject", "optJSONArray", "isNull"]),
            // Android reports field facts to Core, and handles text edits, candidate choices and the language mode from its output. The composing text is sent with setComposingText, which keeps the cursor at its end, so caretBack is unused.
            Unused: ["version", "handlesInput", "imeOpen", "modeIndicatorMs", "capture", "printableAscii",
                "printableAsciiExcept", "shortcuts", "vk", "modifiers", "ctrlKeysWhileComposing", "revision", "currentWord", "insertByPaste",
                "keyReleases", "altKeys", "hint", "spellingSuggestion", "typedKeys", "notes", "colors", "numbered",
                "selectedMeaning", "meaningDelayMs", "caretBack"],
            Others: []),
        new("macOS", "mac/Sources/MeltypeIME/NativeCore.swift", source => source[source.IndexOf("struct ImeOutput", StringComparison.Ordinal)..source.IndexOf("/// libMeltypeNative", StringComparison.Ordinal)],
            new([], Fields: true),
            // Input Method Kit では IME からアプリのキャレットを動かせない。
            Unused: ["version", "handlesInput", "imeOpen", "capture", "printableAscii", "printableAsciiExcept", "shortcuts", "vk", "modifiers",
                "ctrlKeysWhileComposing", "revision", "currentWord", "moveCaretBack", "insertByPaste", "keyReleases", "altKeys"],
            Others: []),
    ];

    [Test]
    public static void Fronts_ReadOrDeclareEveryFieldTheCoreSends()
    {
        var sent = SentNames();
        var root = RepositoryRoot();
        foreach (var front in Fronts)
        {
            var tokens = SourceLexer.Tokenize(front.Region(File.ReadAllText(Path.Combine(root, front.Path))));
            var read = ReadNames(tokens, front.Reads);
            var missing = sent.Except(read).Except(front.Unused).Order().ToList();
            Assert.True(missing.Count == 0, $"{front.Name} ({front.Path}) が読まない項目。読むか Unused に入れる: {string.Join(", ", missing)}");
            var unknown = read.Except(sent).Except(front.Others).Order().ToList();
            Assert.True(unknown.Count == 0, $"{front.Name} ({front.Path}) が読む、本体が出さない名前: {string.Join(", ", unknown)}");
            var stale = front.Unused.Where(name => !sent.Contains(name) || read.Contains(name)).ToList();
            Assert.True(stale.Count == 0, $"{front.Name} の Unused に、本体が出さないか読んでいる名前がある: {string.Join(", ", stale)}");
            var staleOthers = front.Others.Where(name => sent.Contains(name) || !read.Contains(name)).ToList();
            Assert.True(staleOthers.Count == 0, $"{front.Name} の Others に、本体が出すか読んでいない名前がある: {string.Join(", ", staleOthers)}");
        }
    }

    [Test]
    public static void SourceLexer_SkipsCommentsAndTellsIndexFromListLiteral()
    {
        var tokens = SourceLexer.Tokenize("""
            a["x"] # b["y"]
            f(["z"]) // g("w")
            /* get("v") */ get( "u" ) s = "q\"r"
            """);
        Assert.Equal("u,x", string.Join(",", ReadNames(tokens, new(["get"], Index: true)).Order()));
    }

    private static HashSet<string> ReadNames(List<SourceToken> tokens, Reader reader)
    {
        var names = new HashSet<string>();
        for (var i = 0; i + 2 < tokens.Count; i++)
        {
            var (a, b, c) = (tokens[i], tokens[i + 1], tokens[i + 2]);
            if (reader.Calls.Length > 0 && a.Kind == SourceTokenKind.Word && reader.Calls.Contains(a.Text) && b.Text == "(" && c.Kind == SourceTokenKind.String) names.Add(c.Text);
            // 添字: 名前・閉じ括弧の直後の [ "x" ]
            if (reader.Index && b.Text == "[" && c.Kind == SourceTokenKind.String && (a.Kind == SourceTokenKind.Word || a.Text is "]" or ")")
                && i + 3 < tokens.Count && tokens[i + 3].Text == "]") names.Add(c.Text);
            if (reader.Fields && a.Text == "let" && b.Kind == SourceTokenKind.Word && c.Text == ":") names.Add(b.Text);
        }
        return names;
    }

    /// <summary>本体の応答に出る名前すべて。項目は値が無くても出すので、一覧と候補のある応答 1 つで揃う。</summary>
    private static HashSet<string> SentNames()
    {
        var state = new ImeState { Capture = new ImeCapture { Shortcuts = [new(0x19, 0)], CtrlKeysWhileComposing = [new(0x48, 2)] } };
        var view = new CompositionView("x", ["x", "y"], 0, true, "h", ["x"], 0);
        var json = new ImeOutput(state, new SessionResult(true, [new TextEdit(0, "x")], view)).ToJson();
        var names = new HashSet<string>();
        Collect(JsonElement.Parse(json), names);
        return names;
    }

    private static void Collect(JsonElement element, HashSet<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                names.Add(property.Name);
                Collect(property.Value, names);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) Collect(item, names);
        }
    }

    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Meltype.sln"))) dir = Path.GetDirectoryName(dir);
        Assert.True(dir is not null, "リポジトリ (Meltype.sln) が見つからない");
        return dir!;
    }
}
