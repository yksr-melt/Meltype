// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Meltype.Config;

/// <summary>
/// 設定・学習データ・ユーザー辞書のバックアップ (PC の買い替え・別の PC への移行)。
/// 1 つの JSON ファイル (.meltype-backup) に、データフォルダーの対象のファイルを入れる (zip を使わない: 同梱の .NET を小さく削っているため)。
/// ログと Mozc の学習データ (mozc フォルダー) は入れない。
/// </summary>
public static class Backup
{
    private const string Format = "meltype-backup";
    private const int FormatVersion = 1;

    /// <summary>バックアップに入れるファイル (データフォルダーからの相対パス)。dictionaries/ はユーザーが足した辞書。</summary>
    private static readonly string[] RootFiles = ["config.json", "state.json", "conversions.json", "translations.json", "languages.json", "fieldmodes.json", "userdict.txt", "snippets.json", "phrases.txt"];

    /// <summary>
    /// 戻してよい名前か (バックアップに入れるファイルと同じ名前だけ: 決まった名前か、dictionaries/ の直下の .txt)。
    /// 人から受け取ったバックアップでも、データフォルダーの外や、決まったもの以外のファイルには書かない
    /// (../・C:foo のようなドライブ・a:b のような代替データストリーム・予約名を含む名前は使えない)。
    /// </summary>
    public static bool IsRestorableName(string name)
    {
        if (RootFiles.Contains(name, StringComparer.Ordinal)) return true;
        if (!name.StartsWith("dictionaries/", StringComparison.Ordinal)) return false;
        var file = name["dictionaries/".Length..];
        return System.Text.RegularExpressions.Regex.IsMatch(file, @"^[^\\/:*?""<>|\x00-\x1f]{1,100}\.txt$") && !file.StartsWith('.') &&
               !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(file), @"^(con|prn|aux|nul|com\d|lpt\d)(\..*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static IEnumerable<string> TargetFiles(string dataDirectory)
    {
        foreach (var name in RootFiles)
        {
            if (File.Exists(Path.Combine(dataDirectory, name))) yield return name;
        }
        var dictionaries = Path.Combine(dataDirectory, "dictionaries");
        if (Directory.Exists(dictionaries))
        {
            foreach (var file in Directory.GetFiles(dictionaries, "*.txt")) yield return "dictionaries/" + Path.GetFileName(file);
        }
    }

    /// <summary>バックアップを作る。戻り値はファイルの中身と、入れたファイルの数。</summary>
    public static (byte[] Content, int Files) Create(string dataDirectory, string appVersion)
    {
        LearningSaves.FlushAll();
        using var stream = new MemoryStream();
        var count = 0;
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteNumber("version", FormatVersion);
            writer.WriteString("app", appVersion);
            writer.WriteString("created", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            writer.WriteStartObject("files");
            foreach (var name in TargetFiles(dataDirectory))
            {
                writer.WriteString(name, Convert.ToBase64String(File.ReadAllBytes(Path.Combine(dataDirectory, name))));
                count++;
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return (stream.ToArray(), count);
    }

    /// <summary>
    /// バックアップを読む (どのファイルも書かない)。戻してよい名前 (<see cref="IsRestorableName"/>) のファイルだけを読み、それ以外は無視する。
    /// 読めなければ InvalidDataException。
    /// </summary>
    public static BackupContents Read(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.GetString() != Format)
                throw new InvalidDataException("Meltype のバックアップのファイルではありません。");
            return new BackupContents(root.GetProperty("created").GetString() ?? "", root.GetProperty("app").GetString() ?? "",
                [.. root.GetProperty("files").EnumerateObject().Where(file => IsRestorableName(file.Name)).Select(file => (file.Name, file.Value.GetBytesFromBase64()))]);
        }
        // JsonElement は、値の型が違うときも、文字列を UTF-16 にできないとき (不正な UTF-8・片方だけのサロゲート) も InvalidOperationException を投げる
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"バックアップの中身が壊れています ({ex.Message})", ex);
        }
    }
}

/// <summary>読めたバックアップの中身 (<see cref="Backup.Read"/>)。Files は戻すファイルの名前 (データフォルダーからの相対パス) と中身。</summary>
public sealed record BackupContents(string Created, string App, IReadOnlyList<(string Name, byte[] Bytes)> Files)
{
    /// <summary>
    /// データフォルダーに戻す (同じ名前のファイルは上書き。今のファイルは .before-restore として残す)。戻したファイルの数を返す。
    /// 設定・ユーザー辞書・学習データ・定型文は、入力している Meltype が書き換えに気づいて読み直す。dictionaries/ と state.json は、Meltype を起動し直すまで読まない。
    /// </summary>
    public int Restore(string dataDirectory)
    {
        var root = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var restored = 0;
        foreach (var (name, bytes) in Files)
        {
            var path = Path.GetFullPath(Path.Combine(dataDirectory, name));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(path)) File.Copy(path, path + ".before-restore", overwrite: true);
            // 入力している Meltype が書きかけのファイルを読み直さないように、書き終えてから置き換える
            DataFile.Write(path, temp => File.WriteAllBytes(temp, bytes));
            restored++;
        }
        return restored;
    }
}
