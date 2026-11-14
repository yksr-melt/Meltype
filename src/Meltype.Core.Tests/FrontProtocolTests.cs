// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.Json;
using System.Text.RegularExpressions;
using Meltype.Composition;
using Meltype.Ime;

namespace Meltype.Tests;

/// <summary>
/// 本体の応答 (<see cref="ImeOutput"/> の JSON) の項目と、JSON を読むフロントのソースが読む名前を突き合わせる。
/// 本体に項目を足したら、フロントごとに読むか Unused に入れるかを決める。Meltype キーボードは C# の型で受け取るので対象にしない。
/// </summary>
internal static class FrontProtocolTests
{
    /// <param name="Reads">ソースから、JSON の項目を読んでいる所の名前を取り出す。</param>
    /// <param name="Unused">本体が出すが、このフロントが使わない項目。</param>
    /// <param name="Others">ソースから取り出せてしまう、本体の応答でない名前。</param>
    private sealed record FrontSource(string Name, string Path, Func<string, string> Region, Regex Reads, string[] Unused, string[] Others);

    private static readonly FrontSource[] Fronts =
    [
        new("Meltype IME (DLL)", "native/tip/TextService.cpp", source => source, new(@"\[L""(\w+)""\]"),
            // 入力モードと、左右の Alt (フックで見る) は Meltype.exe (TipServer) が使う
            Unused: ["version", "modeIndicatorMs", "insertByPaste", "keyReleases", "altKeys"],
            // Meltype.exe (TipServer) の応答: 背景の変換の結果の待ち合わせと、別ビルドの impl への応答
            Others: ["generation", "stale"]),
        new("fcitx5", "linux/fcitx5/meltype.cpp", source => source, new(@"\b(?:get|str|flag|integer|strings)\(""(\w+)"""),
            Unused: ["version", "handlesInput", "imeOpen", "capture", "printableAscii", "printableAsciiExcept", "shortcuts", "vk", "modifiers",
                "ctrlKeysWhileComposing", "revision", "currentWord", "insertByPaste", "keyReleases", "altKeys"],
            Others: []),
        new("IBus", "linux/ibus-engine-meltype", source => source, new(@"\[""(\w+)""\]|\.get\(""(\w+)"""),
            Unused: ["version", "handlesInput", "imeOpen", "capture", "printableAscii", "printableAsciiExcept", "shortcuts", "vk", "modifiers",
                "ctrlKeysWhileComposing", "revision", "currentWord", "insertByPaste", "keyReleases", "altKeys"],
            // 送る入力欄の事実
            Others: ["app", "selection", "NonText"]),
        new("Android", "android/app/src/main/kotlin/io/github/ninjin0802/keyboard/app/core/ImeJson.kt", source => source,
            new(@"\b(?:get|opt)(?:String|Int|Boolean|JSONObject|JSONArray)\(""(\w+)""|\bisNull\(""(\w+)"""),
            // Android reports field facts to Core, and handles text edits and candidate choices from its output.
            Unused: ["version", "state", "handlesInput", "imeOpen", "languageMode", "modeIndicatorMs", "capture", "printableAscii",
                "printableAsciiExcept", "shortcuts", "vk", "modifiers", "ctrlKeysWhileComposing", "revision", "currentWord", "insertByPaste",
                "keyReleases", "altKeys", "hint", "spellingSuggestion", "typedKeys", "notes", "numbered",
                "selectedMeaning", "meaningDelayMs"],
            Others: []),
        new("macOS", "mac/Sources/MeltypeIME/NativeCore.swift", source => source[source.IndexOf("struct ImeOutput", StringComparison.Ordinal)..source.IndexOf("/// libMeltypeNative", StringComparison.Ordinal)],
            new(@"\blet (\w+):"),
            // IMK は候補ウィンドウの番号を消せない・IME からアプリのキャレットを動かせない
            Unused: ["version", "handlesInput", "imeOpen", "capture", "printableAscii", "printableAsciiExcept", "shortcuts", "vk", "modifiers",
                "ctrlKeysWhileComposing", "revision", "currentWord", "moveCaretBack", "insertByPaste", "numbered", "keyReleases", "altKeys"],
            Others: []),
    ];

    [Test]
    public static void Fronts_ReadOrDeclareEveryFieldTheCoreSends()
    {
        var sent = SentNames();
        var root = RepositoryRoot();
        foreach (var front in Fronts)
        {
            var source = front.Region(File.ReadAllText(Path.Combine(root, front.Path)));
            var read = front.Reads.Matches(source).SelectMany(m => m.Groups.Values.Skip(1)).Where(g => g.Success).Select(g => g.Value).ToHashSet();
            var missing = sent.Except(read).Except(front.Unused).Order().ToList();
            Assert.True(missing.Count == 0, $"{front.Name} ({front.Path}) が読まない項目。読むか Unused に入れる: {string.Join(", ", missing)}");
            var unknown = read.Except(sent).Except(front.Others).Order().ToList();
            Assert.True(unknown.Count == 0, $"{front.Name} ({front.Path}) が読む、本体が出さない名前: {string.Join(", ", unknown)}");
            var stale = front.Unused.Where(name => !sent.Contains(name) || read.Contains(name)).ToList();
            Assert.True(stale.Count == 0, $"{front.Name} の Unused に、本体が出さないか読んでいる名前がある: {string.Join(", ", stale)}");
        }
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
