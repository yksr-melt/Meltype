// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hrmcngs

using System.Text;
using Meltype.Detection;

namespace Meltype.Composition;

internal static class EnglishPhraseSpacing
{
    private static readonly Dictionary<string, int[]> Boundaries = Load();

    private static Dictionary<string, int[]> Load()
    {
        var result = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in DictionarySource.DataLines(DictionarySource.ReadEmbedded("english-phrases.txt")))
        {
            var fields = line.Split('\t');
            if (fields.Length != 2) continue;
            var words = fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (string.Concat(words) == fields[0]) result[fields[0]] = words.Select(word => word.Length).ToArray();
        }
        return result;
    }

    /// <summary>つなげて打った英語の決まり文句 (seeyou、thankyou) か。</summary>
    public static bool IsPhrase(string token) => Boundaries.ContainsKey(token);

    public static string Format(string text)
    {
        var result = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length;)
        {
            if (!IsTokenCharacter(text[i])) { result.Append(text[i++]); continue; }
            var end = i + 1;
            while (end < text.Length && IsTokenCharacter(text[end])) end++;
            var token = text[i..end];
            if (Boundaries.TryGetValue(token, out var lengths))
            {
                var offset = 0;
                foreach (var length in lengths)
                {
                    if (offset > 0) result.Append(' ');
                    result.Append(token, offset, length);
                    offset += length;
                }
            }
            else result.Append(token);
            i = end;
        }
        return result.ToString();
    }

    private static bool IsTokenCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+' or '#' or '.' or '\'' or '@' or '/' or ':';
}
