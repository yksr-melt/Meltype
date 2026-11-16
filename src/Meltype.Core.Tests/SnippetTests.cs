// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>;名前 で、ユーザーが登録した定型文を出す (issue #290)。</summary>
internal static class SnippetTests
{
    [Test]
    public static void Trigger_FindsNameAfterSpaceOrLineStart()
    {
        Assert.True(SnippetTrigger.PassesThrough(";", "", ';') && SnippetTrigger.PassesThrough(";", ";t", 'y'), ";ty はそのままアプリへ");
        Assert.Equal(";ty", SnippetTrigger.TypedBefore(";", ";ty"));
        Assert.Equal(";sig", SnippetTrigger.TypedBefore(";", "hello\n;sig"), "行の先頭");
        Assert.Equal(";sig", SnippetTrigger.TypedBefore(";", "hello ;sig"), "空白の直後");
        Assert.True(SnippetTrigger.TypedBefore(";", ";") is null, "記号だけ");
        Assert.True(!SnippetTrigger.PassesThrough(";", ";ty", ' '), "空白で名前が終わる");
    }

    [Test]
    public static void Trigger_IgnoresSemicolonInsideCode()
    {
        // for(;i<n;i++)・a;b の ; は前が空白・先頭ではないので対象にしない
        Assert.True(!SnippetTrigger.PassesThrough(";", "for(", ';'), "( の後の ;");
        Assert.True(SnippetTrigger.TypedBefore(";", "for(;i") is null, "for(;i で展開しない");
        Assert.True(!SnippetTrigger.PassesThrough(";", "a", ';') && SnippetTrigger.PassesThrough(";", "a ", ';'), "a;b と a ;b");
        Assert.True(SnippetTrigger.TypedBefore(";", ";a;b") is null, "名前に ; は入らない");
        // 記号を変えられる。空なら使わない
        Assert.True(!SnippetTrigger.PassesThrough("", "", ';'), "記号が空なら使わない");
        Assert.Equal("!i", SnippetTrigger.TypedBefore("!", "!i"));
    }

    [Test]
    public static void Store_RejectsControlCharsExceptLineBreaksAndTab()
    {
        // ESC・Ctrl+U などは、入れるとアプリにキー操作として届く (bash で行が実行されうる)
        var store = new SnippetStore(null);
        Assert.True(store.Set("sig", "--\r\n雪代\tメルト"), "改行・タブは使える");
        Assert.True(!store.Set("esc", "a\u001Bb") && !store.Set("ctlu", "a\u0015b") && !store.Set("c1", "a\u0085b"), "ほかの制御文字は登録しない");
        Assert.Equal(1, store.Import("{\"ok\":\"x\",\"esc\":\"a\\u001Bb\",\"ctlo\":\"a\\u000Fb\"}"), "取り込みでも制御文字を含む文は飛ばす");
        Assert.True(store.Get("esc") is null && store.Get("ctlo") is null && store.Get("ok") == "x", "制御文字のない文だけ取り込む");
    }

    [Test]
    public static void Store_SavesExportsAndImports()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-snippets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "snippets.json");
            var store = new SnippetStore(path);
            Assert.True(store.Set("sig", "--\r\n雪代"), "複数行も登録できる");
            Assert.True(!store.Set("bad name", "x") && !store.Set("a;b", "x") && !store.Set("empty", ""), "名前に空白・; は使えない。文が空なら登録しない");
            Assert.Equal("--\n雪代", new SnippetStore(path).Get("sig"), "保存して読み込める");

            var exported = store.Export();
            var other = new SnippetStore(null);
            other.Set("sig", "古い");
            Assert.Equal(1, other.Import(exported));
            Assert.Equal("--\n雪代", other.Get("sig"), "同じ名前は取り込んだ方にする");
            store.Remove("sig");
            Assert.Equal(0, new SnippetStore(path).Count, "消したら残らない");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
