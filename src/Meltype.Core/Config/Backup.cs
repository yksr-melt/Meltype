// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

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
    private static readonly string[] RootFiles = ["config.json", "model.json", "conversions.json", "translations.json", "languages.json", "userdict.txt", "snippets.json"];

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
        using var stream = new MemoryStream();
        var count = 0;
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteNumber("version", FormatVersion);
            writer.WriteString("app", appVersion);
            writer.WriteString("created", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
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

    /// <summary>バックアップの中身を確かめる (作った日・版・ファイルの一覧)。読めなければ例外。</summary>
    public static (string Created, string App, IReadOnlyList<string> Files) Inspect(byte[] content)
    {
        using var document = Parse(content);
        var root = document.RootElement;
        return (root.GetProperty("created").GetString() ?? "", root.GetProperty("app").GetString() ?? "",
            root.GetProperty("files").EnumerateObject().Select(p => p.Name).ToList());
    }

    /// <summary>
    /// バックアップを戻す (同じ名前のファイルは上書き。今のファイルは .before-restore として残す)。戻したファイルの数を返す。
    /// 戻した後は Meltype を起動し直す (読み込み済みの設定・学習データを読み直すため)。
    /// </summary>
    public static int Restore(byte[] content, string dataDirectory)
    {
        using var document = Parse(content);
        var count = 0;
        foreach (var file in document.RootElement.GetProperty("files").EnumerateObject())
        {
            // バックアップに入れるファイルと同じ名前だけを戻す (それ以外の名前は無視)
            var name = file.Name;
            if (!IsRestorableName(name)) continue;
            var path = Path.GetFullPath(Path.Combine(dataDirectory, name));
            if (!path.StartsWith(Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path)) File.Copy(path, path + ".before-restore", overwrite: true);
            File.WriteAllBytes(path, Convert.FromBase64String(file.Value.GetString() ?? ""));
            count++;
        }
        return count;
    }

    private static JsonDocument Parse(byte[] content)
    {
        var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("format", out var format) || format.GetString() != Format)
        {
            document.Dispose();
            throw new InvalidDataException("Meltype のバックアップのファイルではありません。");
        }
        return document;
    }
}
