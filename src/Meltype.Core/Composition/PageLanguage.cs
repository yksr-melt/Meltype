// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// 入力欄の外 (同じ画面のメッセージ・ページ) の言語 (設定「入力欄の外の言語を判定の手がかりにする (実験的)」。issue #287)。
/// フロントは画面の文字から英語の割合だけを求めて渡し、文字は覚えない。入力欄の前に文字が無い最初の語のときだけ、弱く効かせる
/// (入力欄の前の文字があれば、そちらで決める)。
/// </summary>
public static class PageLanguage
{
    /// <summary>割合を決めるのに要る文字の数 (英字と日本語の文字の数の合計)。少なければ分からない (null)。</summary>
    public const int MinLetters = 40;

    /// <summary>英語寄りにする重み。ReadingLattice の英語の区間の重みから引く。入力欄の前の文字より弱くする。</summary>
    public const double Bias = 0.75;

    /// <summary>text の文字のうち英語の割合 (英字の数 / (英字 + かな・漢字の数))。数えられる文字が少なければ null。</summary>
    public static double? EnglishRatio(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        int english = 0, japanese = 0;
        foreach (var c in text)
        {
            if (char.IsAsciiLetter(c)) english++;
            // 日本語の 1 文字は英字の数文字ぶんの内容があるので、3 文字として数える
            else if (c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or (>= '一' and <= '鿿')) japanese += 3;
        }
        return english + japanese < MinLetters ? null : english / (double)(english + japanese);
    }

    /// <summary>
    /// 英語の割合から、英語の区間に足す重み。英語がほとんど (8 割以上) なら英語寄り、ほかは 0。
    /// 日本語寄りにはしない: 最初の語はもともと日本語寄りに判定するので、さらに寄せると、はっきりした英単語 (nice → にせ) までかなにする。
    /// </summary>
    public static double EnglishBias(double? ratio) => ratio >= 0.8 ? -Bias : 0;
}
