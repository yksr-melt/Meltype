// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

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

        // 「仕事用」を足す (今の値を写す) → 仕事用だけ慎重・ライブ変換 OFF にする
        var work = settings.AddProfile("仕事用")!;
        Assert.Equal("仕事用", work.ActiveProfile);
        work.DetectionLevel = DetectionLevel.Conservative;
        work.LiveConversion = false;
        work.AppKinds.Add(new AppKind { Name = "チャット" });

        // 標準に戻すと、標準の値に戻る。共通の項目 (ログ・更新) は切り替えても変わらない
        work.FileLog = true;
        work.TsfOffered = true;
        work.SettingsShowHelp = false;
        work.SettingsWheelChangesValue = false;
        var standard = work.SwitchProfile("標準");
        Assert.Equal("標準", standard.ActiveProfile);
        Assert.Equal(DetectionLevel.Balanced, standard.DetectionLevel);
        Assert.True(standard.LiveConversion, "標準はライブ変換 ON のまま");
        Assert.Equal(0, standard.AppKinds.Count, "表 (独自の種類) もプロファイルごと");
        Assert.True(standard.FileLog, "ログはどのプロファイルでも共通");
        Assert.True(standard.TsfOffered, "Meltype IME を入れるかを聞いたかは、どのプロファイルでも共通 (切り替えるたびに聞かない)");
        Assert.True(!standard.SettingsShowHelp && !standard.SettingsWheelChangesValue, "設定画面の説明の欄・ホイールの設定は、どのプロファイルでも共通");

        // もう一度 仕事用 にすると、仕事用で変えた値が戻ってくる
        var back = standard.SwitchProfile("仕事用");
        Assert.Equal(DetectionLevel.Conservative, back.DetectionLevel);
        Assert.True(!back.LiveConversion, "仕事用はライブ変換 OFF");
        Assert.Equal("チャット", back.AppKinds.Single().Name);

        // 保存して読み直しても同じ
        var path = Path.Combine(Path.GetTempPath(), $"meltype-profile-{Environment.ProcessId}.json");
        try
        {
            back.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal("仕事用", loaded.ActiveProfile);
            Assert.Equal(DetectionLevel.Balanced, loaded.SwitchProfile("標準").DetectionLevel);
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
        a.DetectionLevel = DetectionLevel.Conservative;
        a.LiveConversion = false;
        a.AppKinds.Add(new AppKind { Name = "チャット" });
        a.FileLog = true;
        var file = a.ExportProfile();

        // B さんが読み込む: 新しいプロファイルとして足して切り替える。共通の項目 (ログ) は B さんのまま
        var b = new Settings().Normalize();
        var imported = b.ImportProfile(file)!;
        Assert.Equal("配信用", imported.ActiveProfile);
        Assert.Equal(DetectionLevel.Conservative, imported.DetectionLevel);
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

        // 知らない項目・共通の項目は無視し、範囲外の値は収める
        var odd = b.ImportProfile("{\"format\":\"meltype-profile\",\"name\":\"x\",\"values\":{\"Enabled\":false,\"Unknown\":1,\"MaxPendingKeys\":999999}}")!;
        Assert.True(odd.Enabled, "ON/OFF は読み込まない");
        Assert.Equal(12, odd.MaxPendingKeys, "範囲に収める");
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
    public static void SettingsForm_KeepsTrayChangesMadeWhileOpen()
    {
        // 設定画面を開いた時点の値。開いている間に、トレイで一時停止・判定の強さを変える
        var opened = new Settings().Normalize().AddProfile("仕事用")!.SwitchProfile("標準");
        var current = opened.Clone();
        current.Enabled = false;
        current.DetectionLevel = DetectionLevel.Aggressive;

        // 画面で何も変えずに閉じる: トレイで変えた値のまま
        var kept = Settings.MergeEdits(opened, opened.Clone(), current);
        Assert.True(!kept.Enabled, "一時停止のまま");
        Assert.Equal(DetectionLevel.Aggressive, kept.DetectionLevel, "トレイで変えた判定の強さのまま");

        // 画面で変えた項目だけを重ねる。画面で判定の強さも変えたら画面の値
        var edited = opened.Clone();
        edited.LiveConversion = false;
        var merged = Settings.MergeEdits(opened, edited, current);
        Assert.True(!merged.LiveConversion && !merged.Enabled, "ライブ変換 OFF を重ね、一時停止のまま");
        Assert.Equal(DetectionLevel.Aggressive, merged.DetectionLevel);
        edited.DetectionLevel = DetectionLevel.Conservative;
        Assert.Equal(DetectionLevel.Conservative, Settings.MergeEdits(opened, edited, current).DetectionLevel, "画面で変えた値");

        // トレイで「仕事用」に切り替えた: 画面 (標準のまま) で変えた値は標準に入り、仕事用は変わらない
        var switched = opened.SwitchProfile("仕事用");
        switched.DetectionLevel = DetectionLevel.Manual;
        var onWork = Settings.MergeEdits(opened, edited, switched);
        Assert.Equal("仕事用", onWork.ActiveProfile, "トレイで切り替えたプロファイルのまま");
        Assert.True(onWork.LiveConversion, "仕事用のライブ変換は変えない");
        Assert.Equal(DetectionLevel.Manual, onWork.DetectionLevel, "トレイで変えた仕事用の判定の強さのまま");
        var standard = onWork.SwitchProfile("標準");
        Assert.True(!standard.LiveConversion, "画面で変えた値は標準に入る");
        Assert.Equal(DetectionLevel.Conservative, standard.DetectionLevel);
        Assert.Equal(Settings.MergeEdits(opened, opened.Clone(), switched).ToJson(), switched.ToJson(), "何も変えなければ今の設定そのもの");

        // 画面で「仕事用」に切り替えた (トレイでは切り替えていない): 画面のプロファイルにする
        var formSwitched = opened.SwitchProfile("仕事用");
        formSwitched.TypoEnabled = false;
        var toWork = Settings.MergeEdits(opened, formSwitched, current);
        Assert.Equal("仕事用", toWork.ActiveProfile);
        Assert.True(!toWork.TypoEnabled && !toWork.Enabled, "画面で変えた値を重ね、一時停止のまま");
        Assert.Equal(DetectionLevel.Balanced, toWork.DetectionLevel, "標準でトレイで変えた判定の強さは、仕事用に入れない");
        Assert.Equal(DetectionLevel.Aggressive, toWork.SwitchProfile("標準").DetectionLevel, "標準のトレイで変えた値は標準に残る");
    }
    [Test]
    public static void PasteApps_MatchProcessNames()
    {
        // #5: DaVinci Resolve は 1 文字ずつ送ると取り違えるので、最初から貼り付けで入れる
        var settings = new Settings().Normalize();
        Assert.True(settings.UsesPaste("Resolve.exe") && settings.UsesPaste("resolve.EXE"), "Resolve は最初から (大文字小文字は問わない)");
        Assert.True(!settings.UsesPaste("notepad.exe") && !settings.UsesPaste(null), "ほかのアプリは今までどおり");
        settings.PasteApps = "Resolve.exe, LINE.exe ;foo.exe";
        Assert.True(settings.UsesPaste("LINE.exe") && settings.UsesPaste("foo.exe"), "カンマ・セミコロン区切り");
    }
    [Test]
    public static void TextInputApps_MatchProcessNames()
    {
        // #52: Premiere Pro は入力欄と判定されないので、設定に書いたアプリではフォーカスのある所を入力欄として扱う
        var settings = new Settings().Normalize();
        Assert.True(!settings.TreatsAsTextInput("Adobe Premiere Pro.exe") && !settings.TreatsAsTextInput(null), "既定は空 (何もしない)");
        settings.TextInputApps = " Adobe Premiere Pro.exe , foo.exe;bar.exe ";
        Assert.True(settings.TreatsAsTextInput("Adobe Premiere Pro.exe") && settings.TreatsAsTextInput("adobe premiere pro.EXE"), "空白を含む名前 (大文字小文字は問わない)");
        Assert.True(settings.TreatsAsTextInput("foo.exe") && settings.TreatsAsTextInput("bar.exe"), "カンマ・セミコロン区切り");
        Assert.True(!settings.TreatsAsTextInput("Adobe") && !settings.TreatsAsTextInput("Pro.exe") && !settings.TreatsAsTextInput("Premiere"), "名前の一部では一致しない");
        Assert.True(!settings.TreatsAsTextInput("AfterFX.exe"), "書いていないアプリはそのまま");
        settings.TextInputApps = "a.exe b.exe";
        Assert.True(settings.TreatsAsTextInput("a.exe") && settings.TreatsAsTextInput("b.exe"), "空白区切りの .exe の並びも通す");

        var path = Path.Combine(Path.GetTempPath(), $"meltype-textinput-{Guid.NewGuid():N}.json");
        try
        {
            new Settings { TextInputApps = "Adobe Premiere Pro.exe" }.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal("Adobe Premiere Pro.exe", loaded.TextInputApps, "保存して読み込んでも保つ");
            Assert.True(loaded.Clone().TreatsAsTextInput("Adobe Premiere Pro.exe"), "複製しても保つ");
            Assert.Equal("", Settings.Load(Path.Combine(Path.GetTempPath(), $"meltype-none-{Guid.NewGuid():N}.json")).TextInputApps, "設定ファイルが無ければ空");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
