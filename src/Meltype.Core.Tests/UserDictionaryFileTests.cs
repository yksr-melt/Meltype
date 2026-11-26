// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Dictionary;

namespace Meltype.Tests;

/// <summary>ユーザー辞書の取り込み・書き出し (ほかの日本語入力からの乗り換え)。</summary>
internal static class UserDictionaryFileTests
{
    [Test]
    public static void Export_TimestampUsesTheSameFormatInEveryLocale()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
            var exported = Encoding.Unicode.GetString(UserDictionaryFile.Export([new UserWord("てすと", "テスト")]));
            var stamp = exported.Split("!DateTime:", StringSplitOptions.None)[1].Split('\r')[0];
            var date = DateTime.ParseExact(stamp, "yyyy/MM/dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(DateTime.Now.Year, date.Year, "書き出す日時は Microsoft IME が読める西暦");
            Assert.Equal(new UserWord("てすと", "テスト"), UserDictionaryFile.Parse(Encoding.Unicode.GetBytes(exported)).Words[0]);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public static void Import_MicrosoftImeAndGoogleFormats()
    {
        // Microsoft IME の「一覧の出力」(UTF-16 LE、BOM 付き)
        var msime = "!Microsoft IME Dictionary Tool\r\n!Version:\r\n!Format:WORDLIST\r\n\r\nゆきしろ\t雪代\t人名\r\nメルタイプ\tMeltype\t固有名詞\r\nあ\t亜\t名詞\r\n";
        var result = UserDictionaryFile.Parse([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(msime)]);
        Assert.Equal("UTF-16", result.Encoding);
        Assert.Equal(3, result.Words.Count);
        Assert.Equal(new UserWord("ゆきしろ", "雪代", "人名"), result.Words[0], "品詞は取り込んだ辞書の名前のまま残す");
        Assert.Equal(new UserWord("めるたいぷ", "Meltype", "固有名詞"), result.Words[1], "カタカナの読みはひらがなにする");
        Assert.Equal(new UserWord("あ", "亜"), result.Words[2], "1 文字の読みも取り込む");
        Assert.Equal(0, result.Skipped);

        // Google 日本語入力のエクスポート (UTF-8、BOM なし、4 列)
        var google = Encoding.UTF8.GetBytes("# コメント\nきごうとう\t記号等\t名詞\t\nkaomoji\t(^^)\t顔文字\tコメント\n");
        var g = UserDictionaryFile.Parse(google);
        Assert.Equal("UTF-8", g.Encoding);
        Assert.Equal(new UserWord("きごうとう", "記号等"), g.Words[0]);
        Assert.Equal("顔文字", g.Words[1].Pos);

        // BOM の無い UTF-16
        Assert.Equal(1, UserDictionaryFile.Parse(Encoding.Unicode.GetBytes("ゆきしろ\t雪代\t名詞\r\n")).Words.Count, "BOM の無い UTF-16");
    }

    [Test]
    public static void Category_IsKeptInUserdictAndDoesNotChangeConversion()
    {
        // #856: 語を分類に分けて管理する。分類は変換に使わない
        var directory = Path.Combine(Path.GetTempPath(), "meltype-userdict-category-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.Equal(null, dictionary.Add("ほしの", "星野", "配信者"));
            dictionary.Add("きごうとう", "記号等");
            var lines = File.ReadAllLines(path);
            Assert.True(lines.Contains("ほしの\t星野\t名詞\t配信者"), "分類は 4 列目");
            Assert.True(lines.Contains("きごうとう\t記号等"), "分類の無い名詞は前の版と同じ 2 列");
            var reloaded = new UserDictionary(path, builtIn: false);
            Assert.Equal("配信者", reloaded.Words.Single(w => w.Word == "星野").Category, "読み直しても分類が残る");
            Assert.Equal("星野", reloaded.Lookup("ほしの").Single());
            reloaded.SetCategory(reloaded.Words.Where(w => w.Word == "記号等").ToList(), "記号");
            var categories = new UserDictionary(path, builtIn: false).Categories;
            Assert.True(categories.Count == 2 && categories.Contains("配信者") && categories.Contains("記号"), string.Join(",", categories));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void PartOfSpeech_IsKeptInUserdictAndExport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-userdict-pos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            // 前の版の userdict.txt は品詞の列が無い。名詞として読む
            File.WriteAllText(path, "# Meltype ユーザー辞書\nきごうとう\t記号等\n");
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.Equal(new UserWord("きごうとう", "記号等", UserWord.Noun), dictionary.Words.Single(), "品詞の列が無い行は名詞");
            dictionary.AddRange([new UserWord("ぐぐる", "ググる", "動詞ラ行五段"), new UserWord("てすと", "テスト", "さ変名詞")]);
            var lines = File.ReadAllLines(path);
            Assert.True(lines.Contains("きごうとう\t記号等"), "名詞は前の版と同じ 2 列で書く");
            Assert.True(lines.Contains("ぐぐる\tググる\t動詞ラ行五段"), "名詞でない品詞は 3 列目に書く");
            var reloaded = new UserDictionary(path, builtIn: false);
            Assert.Equal("動詞ラ行五段", reloaded.Words.Single(w => w.Word == "ググる").Pos, "読み直しても品詞が残る");
            Assert.Equal("さ変名詞", reloaded.Words.Single(w => w.Word == "テスト").Pos, "取り込んだ辞書の品詞名のまま残す");
            var exported = UserDictionaryFile.Parse(UserDictionaryFile.Export(reloaded.Words));
            Assert.Equal("動詞ラ行五段", exported.Words.Single(w => w.Word == "ググる").Pos, "書き出しても品詞が残る");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void Export_RoundTrips_AndSkipsDuplicates()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        Assert.Equal(2, dictionary.AddRange([new UserWord("ゆきしろ", "雪代"), new UserWord("めるたいぷ", "Meltype"), new UserWord("ゆきしろ", "雪代")]), "同じ語は 1 回だけ");
        Assert.True(dictionary.Add("き", "木") is null, "画面からの登録と同じ方法で 1 文字の読みを登録する");
        var bytes = UserDictionaryFile.Export(dictionary.Words);
        Assert.True(bytes is [0xFF, 0xFE, ..], "Microsoft IME と同じ UTF-16 LE (BOM 付き)");
        var back = UserDictionaryFile.Parse(bytes);
        Assert.Equal(0, back.Skipped, "書き出した語はすべて取り込める");
        var imported = new UserDictionary(null, builtIn: false);
        Assert.Equal(3, imported.AddRange(back.Words), "空の辞書に取り込める");
        Assert.Equal("木", imported.Lookup("き").Single(), "書き出した 1 文字の読みも登録される");
        Assert.Equal(0, imported.AddRange(back.Words), "登録済みの語は増やさない");
    }

    // macOS の「ユーザ辞書」から Finder にドラッグして書き出した plist (#40)
    private const string MacPlist = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <array>
        	<dict>
        		<key>phrase</key>
        		<string>雪代</string>
        		<key>shortcut</key>
        		<string>ゆきしろ</string>
        	</dict>
        	<dict>
        		<key>phrase</key>
        		<string>Meltype</string>
        		<key>shortcut</key>
        		<string>メルタイプ</string>
        	</dict>
        	<dict>
        		<key>phrase</key>
        		<string>亜</string>
        		<key>shortcut</key>
        		<string>あ</string>
        	</dict>
        	<dict>
        		<key>phrase</key>
        		<string>1 行目
        2 行目</string>
        		<key>shortcut</key>
        		<string>ふくすう</string>
        	</dict>
        </array>
        </plist>
        """;

    [Test]
    public static void Import_MacPlist()
    {
        var result = UserDictionaryFile.Parse(Encoding.UTF8.GetBytes(MacPlist));
        Assert.Equal("plist", result.Encoding);
        Assert.Equal(3, result.Words.Count, "複数行の定型文は飛ばす");
        Assert.Equal(new UserWord("ゆきしろ", "雪代"), result.Words[0]);
        Assert.Equal(new UserWord("めるたいぷ", "Meltype"), result.Words[1], "カタカナの読みはひらがなにする");
        Assert.Equal(new UserWord("あ", "亜"), result.Words[2], "1 文字の読みも取り込む");
        Assert.Equal(1, result.Skipped);
        Assert.True(UserDictionaryFile.Parse([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(MacPlist)]).Words.Count == 3, "BOM 付きでも読む");
        Assert.True(Throws(() => UserDictionaryFile.Parse(Encoding.ASCII.GetBytes("bplist00\0\0"))), "バイナリの plist は読めないと伝える");
    }

    [Test]
    public static void UserDictionary_ReadsPlistInFolder_AndKeepsUserdict()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meltype-plist-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "userdict.txt");
            File.WriteAllText(path, "ゆきしろ\t雪城\nかいしゃ\t会社名\n");
            var folder = Path.Combine(directory, "dictionaries");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "ユーザ辞書.plist"), MacPlist);
            File.WriteAllBytes(Path.Combine(folder, "binary.plist"), Encoding.ASCII.GetBytes("bplist00"));
            var dictionary = new UserDictionary(path, builtIn: false, importDirectory: folder);
            Assert.Equal(2, dictionary.Count, "userdict.txt の語だけを数える (.plist の語は保存しない)");
            Assert.Equal(3, dictionary.ImportedCount, "読めない plist は飛ばす");
            Assert.Equal("亜", dictionary.Lookup("あ").Single(), "1 文字の読みも使える");
            Assert.Equal("雪城,雪代", string.Join(",", dictionary.Lookup("ゆきしろ")), "userdict.txt の登録が先");
            Assert.Equal("Meltype", dictionary.Lookup("めるたいぷ")[0]);
            Assert.Equal("会社名", dictionary.Lookup("かいしゃ")[0]);
            dictionary.Add("てすと", "テスト");
            var saved = File.ReadAllText(path);
            Assert.True(saved.Contains("かいしゃ\t会社名") && !saved.Contains("めるたいぷ"), "保存は userdict.txt の語だけ");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void Import_SkipsWordsWithControlChars()
    {
        // 語の途中の CR・ESC・C1 は、確定するとアプリにキー操作として届く (ターミナルで CR が Enter になる)
        var text = Encoding.UTF8.GetBytes("ゆきしろ\t雪代\nこまんど\tls\r-la\nえすけーぷ\ta\u001Bb\nしーわん\ta\u0085b\n");
        var result = UserDictionaryFile.Parse(text);
        Assert.Equal(1, result.Words.Count, "制御文字を含む語は取り込まない");
        Assert.Equal(new UserWord("ゆきしろ", "雪代"), result.Words[0]);
        Assert.Equal(3, result.Skipped);

        // XML 1.0 に ESC は書けないので、書ける DEL で確かめる
        var plist = MacPlist.Replace("<string>雪代</string>", "<string>雪&#x7F;代</string>");
        Assert.Equal(2, UserDictionaryFile.Parse(Encoding.UTF8.GetBytes(plist)).Words.Count, "plist の語も制御文字を含むものは飛ばす");

        var dictionary = new UserDictionary(null, builtIn: false);
        Assert.Equal(1, dictionary.AddRange([new UserWord("ゆきしろ", "雪代"), new UserWord("こまんど", "ls\r-la"), new UserWord("えすけーぷ", "a\u001Bb")]), "AddRange も制御文字を含む語を飛ばす");
        Assert.True(dictionary.Add("こまんど", "ls\u0015") is not null, "Add も制御文字を含む語を断る");
        Assert.Equal(1, dictionary.Count);
    }

    [Test]
    public static void UserDictionary_SkipsControlCharsInUserdict()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-userdict-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "ゆきしろ\t雪代\nえすけーぷ\ta\u001Bb\n");
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.Equal(1, dictionary.Count, "前に取り込んだ制御文字を含む語は読まない");
            Assert.Equal(0, dictionary.Lookup("えすけーぷ").Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }
}
