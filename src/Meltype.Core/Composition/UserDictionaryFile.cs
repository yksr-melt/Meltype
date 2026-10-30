// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Text;
using System.Xml;

namespace Meltype.Composition;

/// <summary>
/// ユーザー辞書の取り込み・書き出し (ほかの日本語入力から乗り換えるとき・別の PC に移すとき)。
/// 取り込める形式: Microsoft IME の「一覧の出力」(UTF-16、「読み[Tab]語句[Tab]品詞」)、Google 日本語入力の「エクスポート」
/// (UTF-8、「読み[Tab]単語[Tab]品詞[Tab]コメント」)、Meltype の userdict.txt (「読み[Tab]単語」)。! と # で始まる行は飛ばす。
/// macOS の「ユーザ辞書」から Finder にドラッグして書き出した .plist (XML、各要素の shortcut = 読み、phrase = 語) も読む (#40)。
/// 書き出しは Microsoft IME の形式 (Microsoft IME・Google 日本語入力・ATOK のどれでも取り込める)。
/// </summary>
public static class UserDictionaryFile
{
    /// <summary>取り込んだ結果。Skipped は読みがかなでない・短すぎるなどで飛ばした行の数。</summary>
    public sealed record ImportResult(List<UserWord> Words, int Skipped, string Encoding);

    public static ImportResult Parse(byte[] bytes)
    {
        if (IsPlist(bytes)) return ParsePlist(bytes);
        var (text, encoding) = Decode(bytes);
        var words = new List<UserWord>();
        var skipped = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] is '!' or '#') continue;
            var fields = line.Split('\t');
            if (fields.Length < 2)
            {
                skipped++;
                continue;
            }
            var reading = KanaChars.ToHiragana(fields[0].Trim());
            var word = fields[1].Trim();
            // 語の途中の CR・ESC などは確定するとアプリにキー操作として届くので、制御文字を含む語は取り込まない
            if (reading.Length < UserDictionary.MinReadingLength || word.Length == 0 || !reading.All(IsReadingChar) || word.Any(char.IsControl))
            {
                skipped++;
                continue;
            }
            words.Add(new UserWord(reading, word));
        }
        return new ImportResult(words, skipped, encoding);
    }

    /// <summary>plist か (先頭の BOM・空白の後が「&lt;?xml」「&lt;!DOCTYPE plist」「&lt;plist」か、バイナリの「bplist」)。辞書のテキストファイルは &lt; で始まらない。</summary>
    public static bool IsPlist(byte[] bytes)
    {
        if (bytes is [(byte)'b', (byte)'p', (byte)'l', (byte)'i', (byte)'s', (byte)'t', ..]) return true;
        var start = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
        while (start < bytes.Length && bytes[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') start++;
        var head = Encoding.ASCII.GetString(bytes, start, Math.Min(bytes.Length - start, 16));
        return head.StartsWith("<?xml", StringComparison.Ordinal) || head.StartsWith("<!DOCTYPE plist", StringComparison.Ordinal) || head.StartsWith("<plist", StringComparison.Ordinal);
    }

    /// <summary>
    /// macOS の「ユーザ辞書」を書き出した XML の plist を読む: plist &gt; array &gt; dict (key と string の組。shortcut = 読み、phrase = 語)。
    /// バイナリの plist (bplist) は読めない (plutil -convert xml1 で XML にすれば読める)。
    /// </summary>
    public static ImportResult ParsePlist(byte[] bytes)
    {
        if (bytes is [(byte)'b', (byte)'p', (byte)'l', (byte)'i', (byte)'s', (byte)'t', ..])
            throw new InvalidDataException("バイナリ形式の plist は読めません。ターミナルで plutil -convert xml1 ファイル名 を実行して XML にしてください。");
        // DOCTYPE (Apple の DTD) は取りに行かずに飛ばす
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true, IgnoreWhitespace = true };
        var document = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(new MemoryStream(bytes), settings)) document.Load(reader);
        var words = new List<UserWord>();
        var skipped = 0;
        var array = document.DocumentElement?.Name == "plist" ? document.DocumentElement.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.Name == "array") : null;
        if (array is null) throw new InvalidDataException("macOS のユーザ辞書の plist ではありません (plist の中に array がありません)。");
        foreach (var item in array.ChildNodes.OfType<XmlElement>())
        {
            if (item.Name != "dict")
            {
                skipped++;
                continue;
            }
            string? reading = null, word = null;
            var children = item.ChildNodes.OfType<XmlElement>().ToList();
            for (var i = 0; i + 1 < children.Count; i++)
            {
                if (children[i].Name != "key" || children[i + 1].Name != "string") continue;
                if (children[i].InnerText == "shortcut") reading = children[i + 1].InnerText;
                else if (children[i].InnerText == "phrase") word = children[i + 1].InnerText;
            }
            // 複数行の定型文は userdict.txt (1 行に 1 語) に入らないので飛ばす。ESC などほかの制御文字を含む語も飛ばす
            reading = KanaChars.ToHiragana(reading?.Trim() ?? "");
            word = word?.Trim() ?? "";
            if (reading.Length < UserDictionary.MinReadingLength || word.Length == 0 || !reading.All(IsReadingChar) || word.Any(char.IsControl))
            {
                skipped++;
                continue;
            }
            words.Add(new UserWord(reading, word));
        }
        return new ImportResult(words, skipped, "plist");
    }

    /// <summary>Microsoft IME の一覧の形式 (UTF-16 LE、BOM 付き) で書き出す。品詞はすべて名詞。</summary>
    public static byte[] Export(IEnumerable<UserWord> words)
    {
        var builder = new StringBuilder();
        builder.Append("!Microsoft IME Dictionary Tool\r\n");
        builder.Append("!Version:\r\n");
        builder.Append("!Format:WORDLIST\r\n");
        builder.Append("!User Dictionary Name: Meltype\r\n");
        builder.Append("!Output File Name:\r\n");
        builder.Append(CultureInfo.InvariantCulture, $"!DateTime:{DateTime.Now:yyyy/MM/dd HH:mm:ss}\r\n\r\n");
        foreach (var word in words) builder.Append(CultureInfo.InvariantCulture, $"{word.Reading}\t{word.Word}\t名詞\r\n");
        return [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(builder.ToString())];
    }

    /// <summary>BOM を見て UTF-16 (LE / BE) か UTF-8 として読む。BOM が無ければ UTF-8、それで読めなければ UTF-16 LE とみなす。</summary>
    private static (string Text, string Encoding) Decode(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, ..]) return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16");
        if (bytes is [0xFE, 0xFF, ..]) return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE");
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8");
        try
        {
            var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            // 英字だけの BOM の無い UTF-16 は UTF-8 としても読めてしまうが、0 の文字が入る
            if (!utf8.Contains('\0')) return (utf8, "UTF-8");
            return (Encoding.Unicode.GetString(bytes), "UTF-16");
        }
        catch (DecoderFallbackException)
        {
            // BOM の無い UTF-16 (タブ・改行などの後ろに 0 が入る。UTF-8 の文には 0 は入らない)。Shift_JIS のファイルは読めないので、UTF-8 で保存し直してもらう。
            if (bytes.Length % 2 == 0 && bytes.Where((_, i) => i % 2 == 1).Any(b => b == 0)) return (Encoding.Unicode.GetString(bytes), "UTF-16");
            throw new InvalidDataException("文字コードを読めませんでした。UTF-8 か UTF-16 で保存したファイルを選んでください (Shift_JIS のファイルは、メモ帳などで UTF-8 で保存し直すと取り込めます)。");
        }
    }

    private static bool IsReadingChar(char c) => c is >= 'ぁ' and <= 'ゖ' or 'ー' or 'ゔ' or '・' or >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
