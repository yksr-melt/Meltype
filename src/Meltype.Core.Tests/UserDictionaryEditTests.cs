// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Text.Json;
using Meltype.Dictionary;

namespace Meltype.Tests;

/// <summary>ユーザー辞書の登録・編集の検査と、Android の設定画面からの頼み (<see cref="UserDictionaryRequests"/>)。</summary>
internal static class UserDictionaryEditTests
{
    [Test]
    public static void ReadingStartingWithHash_IsRefusedBecauseTheLineWouldBeAComment()
    {
        // #123 を登録できたことにすると、保存した行は次に読むときにコメントとして飛ばされ、一覧にも変換にも出ない
        WithFile(null, path =>
        {
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.Equal("読みの先頭に # は使えません。", dictionary.Add("#123", "番号"));
            Assert.Equal("読みの先頭に # は使えません。", dictionary.Add(" #123", "番号"), "前後の空白を除いてから見る");
            Assert.Equal(null, dictionary.Add("ばんごう#", "番号#"), "# が途中・単語にあるのは使える");
            Assert.Equal(0, dictionary.AddRange([new UserWord("#1", "一")]), "取り込みでも飛ばす");
            Assert.Equal("ばんごう#", string.Join(" ", new UserDictionary(path, builtIn: false).Words.Select(w => w.Reading)), "登録できた語は読み直しても残る");
        });
    }

    [Test]
    public static void Edit_KeepsThePartOfSpeechAndThePosition()
    {
        WithFile("めるたいぷ\tMeltype\nはしる\t走る\t動詞ラ行五段\nしの\t紫乃\n", path =>
        {
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.Equal(null, dictionary.Edit(new UserWord("はしる", "走る", "動詞ラ行五段"), "はしる", "奔る"));
            var words = new UserDictionary(path, builtIn: false).Words;
            Assert.Equal("めるたいぷ:Meltype:名詞 はしる:奔る:動詞ラ行五段 しの:紫乃:名詞", string.Join(" ", words.Select(w => $"{w.Reading}:{w.Word}:{w.Pos}")));
        });
    }

    [Test]
    public static void Edit_RefusesDuplicatesMissingWordsAndTheSameChecksAsAdd()
    {
        WithFile("めるたいぷ\tMeltype\nめるたいぷ\tメルタイプ\n", path =>
        {
            var dictionary = new UserDictionary(path, builtIn: false);
            var katakana = new UserWord("めるたいぷ", "メルタイプ");
            Assert.Equal("同じ読みと単語は、もう登録しています。", dictionary.Edit(katakana, "めるたいぷ", "Meltype"));
            Assert.Equal("読みの先頭に # は使えません。", dictionary.Edit(katakana, "#めるたいぷ", "メルタイプ"));
            Assert.Equal("単語を入力してください。", dictionary.Edit(katakana, "めるたいぷ", " "));
            Assert.Equal("直す語が見つかりません。ほかの画面で変えられたかもしれません。", dictionary.Edit(new UserWord("ない", "無い"), "ない", "無"));
            Assert.Equal(null, dictionary.Edit(katakana, "めるたいぷ", "メルタイプ"), "変えなければ何もしない");
            Assert.Equal(2, new UserDictionary(path, builtIn: false).Count);
        });
    }

    [Test]
    public static void Requests_ListAddEditRemove()
    {
        var directory = Directory.CreateTempSubdirectory("meltype-userdict-requests-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "userdict.txt"), "﻿# コメント\nはしる\t走る\t動詞ラ行五段\n");
            using var listed = Handle(directory, """{"op":"list"}""");
            Assert.Equal("はしる:走る:動詞ラ行五段", Words(listed), "品詞の列とコメント・BOM を Core と同じく読む");
            using var added = Handle(directory, """{"op":"add","reading":"しの","word":"紫乃"}""");
            Assert.Equal(JsonValueKind.Null, added.RootElement.GetProperty("error").ValueKind);
            Assert.Equal(2, added.RootElement.GetProperty("words").GetArrayLength());
            using var refused = Handle(directory, """{"op":"add","reading":"#1","word":"一"}""");
            Assert.Equal("読みの先頭に # は使えません。", refused.RootElement.GetProperty("error").GetString());
            using var edited = Handle(directory, """{"op":"edit","target":{"reading":"はしる","word":"走る","pos":"動詞ラ行五段"},"reading":"はしる","word":"奔る"}""");
            Assert.Equal("はしる:奔る:動詞ラ行五段 しの:紫乃:名詞", Words(edited));
            using var removed = Handle(directory, """{"op":"remove","target":{"reading":"しの","word":"紫乃","pos":"名詞"}}""");
            Assert.Equal(1, removed.RootElement.GetProperty("words").GetArrayLength());
            Assert.Equal("はしる\t奔る\t動詞ラ行五段", File.ReadAllLines(Path.Combine(directory, "userdict.txt"))[1], "Core と同じ形で保存する");
            // 分類の付いた語も、一覧で受け取ったまま (分類も付けて) 頼めば直せる・消せる (#856)
            File.AppendAllText(Path.Combine(directory, "userdict.txt"), "しの\t紫乃\t名詞\t人名\n");
            using var categorized = Handle(directory, """{"op":"edit","target":{"reading":"しの","word":"紫乃","pos":"名詞","category":"人名"},"reading":"しの","word":"志乃"}""");
            Assert.Equal(JsonValueKind.Null, categorized.RootElement.GetProperty("error").ValueKind);
            Assert.Equal("しの\t志乃\t名詞\t人名", File.ReadAllLines(Path.Combine(directory, "userdict.txt")).Last(), "分類はそのまま");
            using var removedCategorized = Handle(directory, """{"op":"remove","target":{"reading":"しの","word":"志乃","pos":"名詞","category":"人名"}}""");
            Assert.Equal(1, removedCategorized.RootElement.GetProperty("words").GetArrayLength());
            using var broken = Handle(directory, "{");
            Assert.Equal("頼みを読めませんでした。", broken.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Words(JsonDocument response) =>
        string.Join(" ", response.RootElement.GetProperty("words").EnumerateArray().Select(w => $"{w.GetProperty("reading").GetString()}:{w.GetProperty("word").GetString()}:{w.GetProperty("pos").GetString()}"));

    private static JsonDocument Handle(string directory, string request) => JsonDocument.Parse(UserDictionaryRequests.Handle(directory, request));

    private static void WithFile(string? text, Action<string> test)
    {
        var directory = Directory.CreateTempSubdirectory("meltype-userdict-edit-").FullName;
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            if (text is not null) File.WriteAllText(path, text);
            test(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
