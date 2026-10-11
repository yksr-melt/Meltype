// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>;名前 で、ユーザーが登録した定型文を出す (issue #290)。</summary>
internal static class SnippetTests
{
    /// <summary>フックと同じ順に打つ: 渡すか聞いてから (PassesThrough)、アプリに届いた文字を伝える (Append)。渡さなかった文字は false を返す。</summary>
    private static List<bool> Type(SnippetTrigger trigger, string text, string? before = null)
    {
        var passed = new List<bool>();
        foreach (var c in text)
        {
            passed.Add(trigger.PassesThrough(c, before));
            trigger.Append(c.ToString());
            before = null;
        }
        return passed;
    }

    [Test]
    public static void Trigger_ExpandsRegisteredNameAfterSpaceOrLineStart()
    {
        var store = new SnippetStore(null);
        store.Set("ty", "ありがとうございます。確認します。");
        store.Set("sig", "--\r\n雪代");

        var trigger = new SnippetTrigger();
        Assert.True(Type(trigger, ";ty").All(p => p), ";ty はそのままアプリへ");
        Assert.Equal("ty", trigger.Name);
        var expansion = trigger.Expand(store);
        Assert.True(expansion is { Delete: 3, Text: "ありがとうございます。確認します。", Typed: ";ty" }, expansion?.ToString() ?? "null");
        Assert.True(!trigger.IsActive, "展開したら名前の途中を終える");

        // 空白の直後の ;sig (複数行の文。改行は \n にそろえる)
        trigger = new SnippetTrigger();
        Type(trigger, "hello ;sig");
        Assert.Equal("--\n雪代", trigger.Expand(store)?.Text);

        // 未登録の名前は何もしない
        trigger = new SnippetTrigger();
        Type(trigger, ";foo");
        Assert.True(trigger.Expand(store) is null, "未登録");
    }

    [Test]
    public static void Trigger_IgnoresSemicolonInsideCode()
    {
        // for(;i<n;i++)・a;b の ; は前が空白・先頭ではないので対象にしない
        var store = new SnippetStore(null);
        store.Set("i", "x");
        var trigger = new SnippetTrigger();
        Type(trigger, "for(");
        Assert.True(!trigger.PassesThrough(';'), "( の後の ;");
        trigger.Append(";i");
        Assert.True(trigger.Expand(store) is null, "for(;i で展開しない");
        // キャレットの前の文字を教えてもらえば、それで決める
        trigger = new SnippetTrigger();
        Assert.True(!trigger.PassesThrough(';', "a"), "a;b");
        Assert.True(trigger.PassesThrough(';', "a "), "a ;b");
        // 記号を変えられる。空なら使わない
        trigger = new SnippetTrigger { Mark = "" };
        Assert.True(!trigger.PassesThrough(';'), "記号が空なら使わない");
        trigger = new SnippetTrigger { Mark = "!" };
        Type(trigger, "!i");
        Assert.Equal("x", trigger.Expand(store)?.Text);
    }

    [Test]
    public static void Trigger_BackSpaceAndCaretMoves()
    {
        var store = new SnippetStore(null);
        store.Set("ty", "thanks");
        var trigger = new SnippetTrigger();
        Type(trigger, ";tyx");
        trigger.Backspace();
        Assert.Equal("thanks", trigger.Expand(store)?.Text);
        // 記号まで消したら、記号の前 (先頭) に戻る
        trigger = new SnippetTrigger();
        Type(trigger, ";t");
        trigger.Backspace();
        trigger.Backspace();
        Assert.True(!trigger.IsActive && trigger.PassesThrough(';'), "先頭に戻る");
        // キャレットが動いたら分からない
        trigger = new SnippetTrigger();
        Type(trigger, ";ty");
        trigger.Lose();
        Assert.True(trigger.Expand(store) is null, "キャレットが動いたら展開しない");
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
