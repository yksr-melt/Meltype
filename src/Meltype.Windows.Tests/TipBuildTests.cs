// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Tests;

internal static class TipBuildTests
{
    private const string X64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string X86 = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string Other = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    // x64・x86 の下に MeltypeTipImpl.dll.sha256 を置いた tip フォルダー (hash が null のアーキテクチャには置かない)
    private static string TipFolder(string root, string name, string? x64, string? x86)
    {
        var folder = Path.Combine(root, name);
        foreach (var (arch, hash) in new[] { ("x64", x64), ("x86", x86) })
        {
            Directory.CreateDirectory(Path.Combine(folder, arch));
            if (hash is not null) File.WriteAllText(Path.Combine(folder, arch, "MeltypeTipImpl.dll.sha256"), hash);
        }
        return folder;
    }

    private static void WithFolder(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "meltype-tip-build-" + Guid.NewGuid().ToString("N"));
        try { test(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static string Request(string impl) => $"{{{impl}\"thread\":1,\"event\":{{\"type\":\"StateRequested\"}}}}";

    [Test]
    public static void TipBuild_AcceptsOnlyTheImplShippedWithThisExe() => WithFolder(root =>
    {
        var build = new Tip.TipBuild(TipFolder(root, "shipped", X64, X86));
        Assert.True(build.Matches(Request($"\"impl\":\"{X64}\",")), "64 ビットのアプリの impl");
        Assert.True(build.Matches(Request($"\"impl\":\"{X86}\",")), "32 ビットのアプリの impl");
        Assert.True(build.Matches(Request($"\"impl\":\"{X64.ToLowerInvariant()}\",")), "ハッシュの大文字・小文字は区別しない");
        Assert.True(!build.Matches(Request($"\"impl\":\"{Other}\",")), "別ビルドの impl");
        Assert.True(!build.Matches(Request("")), "stub と impl に分割する前の DLL (impl を送らない)");
        Assert.True(!build.Matches(Request("\"impl\":1,")), "impl が文字列でない");
        Assert.True(!build.Matches("{"), "JSON としてパースできない");
        Assert.True(build.Matches("{\"wait\":3}"), "変換の結果を待つ要求は impl を送らない");
    });

    [Test]
    public static void TipBuild_WithoutShippedTipFolderAcceptsAnyImpl() => WithFolder(root =>
    {
        var build = new Tip.TipBuild(Path.Combine(root, "missing"));
        Assert.True(build.Matches(Request($"\"impl\":\"{Other}\",")), "tip フォルダーが無い Meltype.exe は照合しない");
        Assert.True(build.Matches(Request("")), "impl を送らない要求も受ける");
        Assert.True(build.IsInstalled(TipFolder(root, "installed", Other, Other)), "登録済みの impl も照合しない");
    });

    [Test]
    public static void TipBuild_InstalledImplMustMatchForBothArchitectures() => WithFolder(root =>
    {
        var build = new Tip.TipBuild(TipFolder(root, "shipped", X64, X86));
        Assert.True(build.IsInstalled(TipFolder(root, "same", X64, X86)), "登録済みの impl が同じビルド");
        Assert.True(!build.IsInstalled(TipFolder(root, "old-x86", X64, Other)), "32 ビットの impl だけ別ビルド");
        Assert.True(!build.IsInstalled(TipFolder(root, "before-split", null, null)), "stub と impl に分割する前の登録 (MeltypeTipImpl.dll.sha256 が無い)");
    });
}
