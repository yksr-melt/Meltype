// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Detection;

namespace Meltype.Detection;

/// <summary>
/// 日本語辞書の語との編集距離で打ち間違いを拾う (設計書 §15)。
/// 補助的な加点だけで、これ単独では閾値に届かないようにしてある。
/// </summary>
internal sealed class TypoDetector(WordList japaneseWords)
{
    public const int MinLength = 5;

    public void Evaluate(string letters, List<Contribution> output)
    {
        if (letters.Length < MinLength || japaneseWords.HasPrefix(letters)) return;

        // 先頭文字の打ち間違いはまれなので、同じ先頭文字の語だけを比べて計算量を抑える。
        string? best = null;
        foreach (var word in japaneseWords.WordsStartingWith(letters[0]))
        {
            for (var length = letters.Length - 1; length <= letters.Length + 1; length++)
            {
                if (length > word.Length || length < 2) continue;
                if (Levenshtein(letters, word.AsSpan(0, length), 1) <= 1)
                {
                    best = word;
                    break;
                }
            }
            if (best is not null) break;
        }
        if (best is not null) output.Add(new Contribution("Typo", 3, 0, $"「{best}」の打ち間違いに近い"));
    }

    /// <summary>上限 <paramref name="max"/> を超えたら打ち切る Levenshtein 距離。</summary>
    public static int Levenshtein(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int max = int.MaxValue)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        Span<int> previous = stackalloc int[b.Length + 1];
        Span<int> current = stackalloc int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }
            if (rowMin > max) return max + 1;
            current.CopyTo(previous);
        }
        return previous[b.Length];
    }
}
