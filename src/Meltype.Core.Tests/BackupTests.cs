// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>設定・学習データ・ユーザー辞書のバックアップ (別の PC への移行)。</summary>
internal static class BackupTests
{
    [Test]
    public static void Backup_TimestampUsesGregorianCalendarInEveryLocale()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        var root = Path.Combine(Path.GetTempPath(), "meltype-backup-culture-" + Guid.NewGuid().ToString("N"));
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
            Directory.CreateDirectory(root);
            var (content, _) = Backup.Create(root, "test");
            var created = Backup.Read(content).Created;
            var date = DateTime.ParseExact(created, "yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(DateTime.Now.Year, date.Year, "仏暦ではなく西暦で保存する");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public static void Backup_RoundTrips_AndStaysInsideDataFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "meltype-backup-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        try
        {
            Directory.CreateDirectory(Path.Combine(source, "dictionaries"));
            File.WriteAllText(Path.Combine(source, "config.json"), "{\"Mode\":\"Keyboard\"}");
            File.WriteAllText(Path.Combine(source, "userdict.txt"), "ゆきしろ\t雪代\n");
            File.WriteAllText(Path.Combine(source, "dictionaries", "candidates.txt"), "てすと テスト\n");
            File.WriteAllText(Path.Combine(source, "meltype.log"), "ログは入れない");

            var (content, files) = Backup.Create(source, "0.2.0");
            Assert.Equal(3, files, "ログは入れない");
            var backup = Backup.Read(content);
            Assert.Equal("0.2.0", backup.App);
            Assert.True(backup.Files.Any(file => file.Name == "dictionaries/candidates.txt"), "ユーザーの辞書も入る");

            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "userdict.txt"), "まえ\t前\n");
            Assert.Equal(3, backup.Restore(target));
            Assert.Equal("ゆきしろ\t雪代\n", File.ReadAllText(Path.Combine(target, "userdict.txt")));
            Assert.True(File.Exists(Path.Combine(target, "userdict.txt.before-restore")), "今までのファイルを残す");

            // データフォルダーの外を指す名前は書かない
            var evil = Encoding.UTF8.GetBytes("{\"format\":\"meltype-backup\",\"version\":1,\"app\":\"x\",\"created\":\"x\",\"files\":{\"../evil.txt\":\"AA==\",\"sub/x.txt\":\"AA==\"}}");
            Assert.Equal(0, Backup.Read(evil).Restore(target), "../ や知らないフォルダーは無視");
            Assert.True(!File.Exists(Path.Combine(root, "evil.txt")), "データフォルダーの外に書かない");

            // 決まった名前 (バックアップに入れるもの) 以外は戻さない
            foreach (var name in new[] { "evil.exe", "C:evil.txt", "config.json:ads", "dictionaries/a:b.txt", "dictionaries/../x.txt", "dictionaries/sub/x.txt", "dictionaries/con.txt", "dictionaries/x.ps1", "..\\evil.txt", "/etc/x.txt" })
            {
                Assert.True(!Backup.IsRestorableName(name), name);
            }
            Assert.True(Backup.IsRestorableName("config.json") && Backup.IsRestorableName("dictionaries/candidates.txt"), "バックアップに入れるものは戻す");

            Assert.True(IsBroken(Encoding.UTF8.GetBytes("{\"a\":1}")), "Meltype のバックアップでないファイルは読まない");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Test]
    public static void Backup_IncludesPhraseHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "meltype-backup-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "phrases.txt"), "ゆきしろ\t雪代\t1\t0\n");

            var (content, files) = Backup.Create(source, "0.2.0");
            Assert.Equal(1, files, "予測変換の学習 (phrases.txt) も入る");
            Assert.True(Backup.IsRestorableName("phrases.txt"), "phrases.txt は戻す");

            Directory.CreateDirectory(target);
            Assert.Equal(1, Backup.Read(content).Restore(target));
            Assert.Equal("ゆきしろ\t雪代\t1\t0\n", File.ReadAllText(Path.Combine(target, "phrases.txt")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Test]
    public static void Read_BrokenBackup_IsInvalidData()
    {
        // 1 つ目のファイルは読める。壊れたファイルが 1 つでもあれば、読めるファイルも戻さない
        const string Good = "\"config.json\":\"AA==\"";
        foreach (var json in new[]
        {
            "", "[]", "{\"format\":1}", "{\"format\":\"\\ud800\"}",
            Json(files: "[]"), Json(files: "1"), Json(app: "1"), Json(app: "\"\\ud800\""), Json(created: "\"\\ud800\""),
            Json(files: "{" + Good + ",\"\\ud800\":\"\"}"),
            Json(files: "{" + Good + ",\"userdict.txt\":1}"),
            Json(files: "{" + Good + ",\"userdict.txt\":null}"),
            Json(files: "{" + Good + ",\"userdict.txt\":\"!!\"}"),
            Json(files: "{" + Good + ",\"userdict.txt\":\"\\ud800\"}"),
            "{\"format\":\"meltype-backup\",\"app\":\"x\",\"created\":\"x\"}",
        })
        {
            Assert.True(IsBroken(Encoding.UTF8.GetBytes(json)), json);
        }

        // base64 の 1 バイトが UTF-8 でない値に化けている
        var corrupted = Encoding.UTF8.GetBytes(Json(files: "{" + Good + "}"));
        corrupted[corrupted.AsSpan().IndexOf("AA=="u8)] = 0xFF;
        Assert.True(IsBroken(corrupted), "0xFF");

        static string Json(string app = "\"x\"", string created = "\"x\"", string files = "{}") =>
            "{\"format\":\"meltype-backup\",\"version\":1,\"app\":" + app + ",\"created\":" + created + ",\"files\":" + files + "}";
    }

    // 設定画面 (BackupPage) が捕まえる、壊れたバックアップの例外か
    private static bool IsBroken(byte[] content)
    {
        try { Backup.Read(content); return false; } catch (InvalidDataException) { return true; }
    }
}
