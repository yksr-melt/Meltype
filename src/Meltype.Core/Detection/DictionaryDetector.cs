// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>
/// 日本語辞書 (ローマ字見出し) との前方一致。見出し語から綴りの揺れ
/// (shi/si, tsu/tu, chi/ti, ん の n/nn …) を展開して登録するので、訓令式で打っても一致する。
/// </summary>
public sealed class DictionaryDetector
{
    public WordList Words { get; } = new();

    public DictionaryDetector(IEnumerable<string> canonicalWords, RomajiDetector romaji)
    {
        foreach (var word in canonicalWords)
        {
            foreach (var variant in romaji.SpellingVariants(word)) Words.Add(variant);
        }
    }

    public bool IsPrefix(string letters) => Words.HasPrefix(letters);

    public void Evaluate(string letters, List<Contribution> output)
    {
        if (letters.Length < 3) return;
        if (Words.ContainsWord(letters))
        {
            output.Add(new Contribution("Dictionary", 5, 0, "日本語辞書の語と一致"));
        }
        else if (Words.HasPrefix(letters))
        {
            output.Add(new Contribution("Dictionary", letters.Length >= 4 ? 4 : 3, 0, "日本語辞書の語の先頭と一致"));
        }
        // 助詞 + 日本語の語 (no + tasuku = のタスク、ga + meeru)。英数状態で前の語 (OK) の続きを打っているとき。
        else if (StartsWithParticle(letters) is { } particle && letters[particle.Length..] is { Length: >= 4 } rest && Words.HasPrefix(rest))
        {
            output.Add(new Contribution("Dictionary", 4, 0, $"助詞「{particle}」+ 日本語辞書の語"));
        }
    }

    private static readonly string[] Particles = ["no", "ga", "wo", "ni", "de", "to", "ha", "mo", "wa", "he", "ya"];

    /// <summary>助詞 (の が を に で と は も わ へ や) で始まっていれば、その助詞。</summary>
    public static string? StartsWithParticle(string letters) => Particles.FirstOrDefault(letters.StartsWith);
}
