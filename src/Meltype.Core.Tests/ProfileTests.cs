// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Config;

namespace Meltype.Tests;

/// <summary>プロファイル (仕事用・趣味用・SNS 用など): 設定の値をまとめて切り替える。</summary>
internal static class ProfileTests
{
    [Test]
    public static void Profiles_SwitchValuesAndKeepSharedOnes()
    {
        var settings = new Settings().Normalize();
        Assert.Equal("標準", settings.ActiveProfile, "最初は「標準」だけ");
        Assert.Equal(1, settings.Profiles.Count);

        // 「仕事用」を足す (今の値を写す) → 仕事用だけ自動判定・ライブ変換 OFF にする
        var work = settings.AddProfile("仕事用")!;
        Assert.Equal("仕事用", work.ActiveProfile);
        work.AutoDetection = false;
        work.LiveConversion = false;
        work.AppKinds.Add(new AppKind { Name = "チャット" });

        // 標準に戻すと、標準の値に戻る。共通の項目 (ログ・更新) は切り替えても変わらない
        work.FileLog = true;
        var standard = work.SwitchProfile("標準");
        Assert.Equal("標準", standard.ActiveProfile);
        Assert.True(standard.AutoDetection, "標準は自動判定 ON のまま");
        Assert.True(standard.LiveConversion, "標準はライブ変換 ON のまま");
        Assert.Equal(0, standard.AppKinds.Count, "表 (独自の種類) もプロファイルごと");
        Assert.True(standard.FileLog, "ログはどのプロファイルでも共通");

        // もう一度 仕事用 にすると、仕事用で変えた値が戻ってくる
        var back = standard.SwitchProfile("仕事用");
        Assert.True(!back.AutoDetection, "仕事用は自動判定 OFF");
        Assert.True(!back.LiveConversion, "仕事用はライブ変換 OFF");
        Assert.Equal("チャット", back.AppKinds.Single().Name);

        // 保存して読み直しても同じ
        var path = Path.Combine(Path.GetTempPath(), $"meltype-profile-{Environment.ProcessId}.json");
        try
        {
            back.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal("仕事用", loaded.ActiveProfile);
            Assert.True(loaded.SwitchProfile("標準").AutoDetection, "標準は自動判定 ON のまま");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Profiles_RenameAndRemove()
    {
        var settings = new Settings().Normalize().AddProfile("SNS")!;
        Assert.True(settings.AddProfile("SNS") is null, "同じ名前は足せない");
        Assert.True(settings.AddProfile("  ") is null, "空の名前は足せない");

        var renamed = settings.RenameProfile("SNS", "SNS 用")!;
        Assert.Equal("SNS 用", renamed.ActiveProfile, "使っているものの名前を変えたら、使っている名前も変わる");

        // 使っているものを消すと、残りの最初のものに切り替わる。最後の 1 つは消せない
        var removed = renamed.RemoveProfile("SNS 用")!;
        Assert.Equal("標準", removed.ActiveProfile);
        Assert.Equal(1, removed.Profiles.Count);
        Assert.True(removed.RemoveProfile("標準") is null, "最後の 1 つは消せない");
    }
    [Test]
    public static void Profiles_ExportAndImportBetweenUsers()
    {
        // A さんが「配信用」を作って書き出す
        var a = new Settings().Normalize().AddProfile("配信用")!;
        a.AutoDetection = false;
        a.LiveConversion = false;
        a.AppKinds.Add(new AppKind { Name = "チャット" });
        a.FileLog = true;
        var file = a.ExportProfile();

        // B さんが読み込む: 新しいプロファイルとして足して切り替える。共通の項目 (ログ) は B さんのまま
        var b = new Settings().Normalize();
        var imported = b.ImportProfile(file)!;
        Assert.Equal("配信用", imported.ActiveProfile);
        Assert.True(!imported.AutoDetection, "自動判定 OFF も入る");
        Assert.True(!imported.LiveConversion, "ライブ変換 OFF も入る");
        Assert.Equal("チャット", imported.AppKinds.Single().Name);
        Assert.True(!imported.FileLog, "ログは共通の項目なので読み込まない");
        Assert.True(imported.SwitchProfile("標準").LiveConversion, "前からのプロファイルはそのまま");

        // 同じ名前があれば (2) を付ける
        Assert.Equal("配信用 (2)", imported.ImportProfile(file)!.ActiveProfile);

        // 読めないファイル・別の JSON は読み込まない
        Assert.True(b.ImportProfile("not json") is null, "JSON でない");
        Assert.True(b.ImportProfile("{\"Enabled\": false}") is null, "config.json など別のファイル");
        Assert.True(b.ImportProfile("{\"format\":\"meltype-profile\",\"values\":{\"LiveConversion\":\"yes\"}}") is null, "型の違う値");

        // 知らない項目・消した項目・共通の項目は無視する
        var odd = b.ImportProfile("{\"format\":\"meltype-profile\",\"name\":\"x\",\"values\":{\"Enabled\":false,\"Unknown\":1,\"MaxPendingKeys\":999999}}")!;
        Assert.True(odd.Enabled, "ON/OFF は読み込まない");

        // v8 より前に書き出したプロファイルは、まとめた項目を新しい項目に移して読む
        var old = b.ImportProfile("{\"format\":\"meltype-profile\",\"version\":7,\"name\":\"old\",\"values\":{\"SlashAsMiddleDot\":true,\"FullWidthSpaceByDefault\":true,\"PasteApps\":\"foo.exe\"}}")!;
        Assert.Equal(SlashStyle.MiddleDot, old.Slash, "/ キーで中黒 → 中黒");
        Assert.Equal(SpaceWidth.FullWidth, old.SpaceWidth, "スペースを全角で入力 → 全角");
        Assert.Equal(CommitMethod.Paste, old.CommitMethodFor("foo.exe"), "貼り付けで入力するアプリ → アプリ別設定");
        var current = b.ImportProfile("{\"format\":\"meltype-profile\",\"version\":8,\"name\":\"new\",\"values\":{\"SlashAsMiddleDot\":true}}")!;
        Assert.Equal(SlashStyle.Slash, current.Slash, "v8 の書き出しは移さない (前の項目名は無視する)");
    }

    [Test]
    public static void ProfileNames_AreNotWrittenToJson()
    {
        // プロファイルの名前の一覧は Profiles から作るので、config.json にも書き出したファイルにも入れない
        var settings = new Settings().Normalize().AddProfile("仕事用")!;
        Assert.True(!settings.ToJson().Contains("ProfileNames"), "config.json に入れない");
        Assert.True(!settings.ExportProfile().Contains("ProfileNames"), "書き出したファイルに、ほかのプロファイルの名前を入れない");
    }
    [Test]
    public static void PasteApps_MatchProcessNames()
    {
        // #5: DaVinci Resolve は 1 文字ずつ送ると取り違えるので、最初から貼り付けで入れる
        var settings = new Settings().Normalize();
        Assert.True(settings.CommitMethodFor("Resolve.exe") == CommitMethod.Paste && settings.CommitMethodFor("resolve.EXE") == CommitMethod.Paste, "Resolve は最初から (大文字小文字は問わない)");
        Assert.True(settings.CommitMethodFor("notepad.exe") == CommitMethod.Auto && settings.CommitMethodFor(null) == CommitMethod.Auto, "ほかのアプリは自動");
    }
    [Test]
    public static void TextInputApps_MatchProcessNames()
    {
        // #52: Premiere Pro は入力欄と判定されないので、アプリ別設定で「入力欄とみなす」にしたアプリではフォーカスのある所を入力欄として扱う
        var settings = new Settings().Normalize();
        Assert.True(!settings.TreatsAsTextInput("Adobe Premiere Pro.exe") && !settings.TreatsAsTextInput(null), "既定は何もしない");
        settings.AppRules.Add(new AppRule { Process = "Adobe Premiere Pro.exe", TextInput = true });
        Assert.True(settings.TreatsAsTextInput("Adobe Premiere Pro.exe") && settings.TreatsAsTextInput("adobe premiere pro.EXE"), "空白を含む名前 (大文字小文字は問わない)");
        Assert.True(!settings.TreatsAsTextInput("Adobe") && !settings.TreatsAsTextInput("Pro.exe") && !settings.TreatsAsTextInput("Premiere"), "名前の一部では一致しない");
        Assert.True(!settings.TreatsAsTextInput("AfterFX.exe"), "書いていないアプリはそのまま");

        var path = Path.Combine(Path.GetTempPath(), $"meltype-textinput-{Guid.NewGuid():N}.json");
        try
        {
            settings.Save(path);
            var loaded = Settings.Load(path);
            Assert.True(loaded.TreatsAsTextInput("Adobe Premiere Pro.exe"), "保存して読み込んでも保つ");
            Assert.True(loaded.Clone().TreatsAsTextInput("Adobe Premiere Pro.exe"), "複製しても保つ");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
