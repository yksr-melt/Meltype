// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>
/// 明らかな英語・技術用語を検出する (設計書 §16)。
/// 辞書には「ローマ字としても読める英単語」と日本語由来の外来語 (sushi, kana …) を重点的に入れてある。
/// ローマ字として成立しない綴り (th, l, v, 子音連続 …) は RomajiDetector 側で英語扱いになる。
/// </summary>
public sealed class EnglishDetector
{
    public WordList Words { get; }

    public EnglishDetector(IEnumerable<string> words) => Words = new(words);

    public bool IsPrefix(string letters) => letters.Length >= 2 && Words.HasPrefix(letters);

    public void Evaluate(string letters, List<Contribution> output)
    {
        if (letters.Length < 2) return;
        if (Words.ContainsWord(letters))
        {
            output.Add(new Contribution("English", 0, 4, "英語辞書の語と一致"));
        }
        else if (Words.HasPrefix(letters))
        {
            output.Add(new Contribution("English", 0, 3, "英語辞書の語の先頭と一致"));
        }
    }
}
