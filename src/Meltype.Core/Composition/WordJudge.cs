// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;

namespace Meltype.Composition;

/// <summary>
/// 更新の前に「あなたの打ち方で変わる語」を見せるための、語ごとの自動の判定 (issue #285)。
/// 今の版と新しい版 (更新の zip の中の Meltype.exe --judge-words) の両方で同じ語を打ってみて、結果が変わる語だけを見せる。
/// 判定はこの PC の中だけで行い、何も送らない。学習 (覚えた語) は使わない (版による自動の判定の違いを見るため)。
/// </summary>
public static class WordJudge
{
    /// <summary>
    /// 1 語を打って確定したときの見え方 (漢字にはせず、英字 / かなのまま)。前後の文脈は無いものとする。
    /// detector には学習 (Memory) を付けないこと。
    /// </summary>
    public static string Judge(CompositionDetector detector, string word)
    {
        var text = new CompositionText(detector);
        foreach (var c in word) text.Append(c);
        return text.Display(final: true);
    }

    /// <summary>語の一覧を判定して「語[Tab]見え方」の行にする (--judge-words の出力)。</summary>
    public static string JudgeAll(CompositionDetector detector, IEnumerable<string> words)
    {
        var output = new StringBuilder();
        foreach (var word in words.Where(IsWord).Distinct(StringComparer.Ordinal))
            output.Append(word).Append('\t').Append(Judge(detector, word)).Append('\n');
        return output.ToString();
    }

    /// <summary>判定に使う語か: 英字だけで 2 文字以上 (languages.json の語と同じ)。</summary>
    public static bool IsWord(string word) => word.Length >= 2 && word.All(char.IsAsciiLetter);

    /// <summary>「語[Tab]見え方」の行を読む。</summary>
    public static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var tab = line.IndexOf('\t');
            if (tab > 0) result[line[..tab]] = line[(tab + 1)..].TrimEnd('\r');
        }
        return result;
    }

    /// <summary>今の版と新しい版で見え方が変わる語 (新しい版で判定できなかった語は除く)。語の順。</summary>
    public static IReadOnlyList<WordChange> Changes(IReadOnlyDictionary<string, string> now, IReadOnlyDictionary<string, string> next) =>
        now.Where(entry => next.TryGetValue(entry.Key, out var shown) && shown != entry.Value)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new WordChange(entry.Key, entry.Value, next[entry.Key]))
            .ToList();
}

/// <summary>更新で見え方が変わる語。Now は今の版、Next は新しい版での見え方。</summary>
public sealed record WordChange(string Word, string Now, string Next)
{
    /// <summary>今の版では英字で見えているか (「今のまま覚えておく」で英語 / 日本語のどちらとして覚えるか)。</summary>
    public bool NowEnglish => Now == Word || Now.All(c => c < 0x80);
}
