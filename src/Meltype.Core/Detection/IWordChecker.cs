// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>
/// 普通の英単語として正しい綴りかを調べるもの (OS のスペルチェッカーなど)。
/// Windows: ISpellChecker、Mac: NSSpellChecker、Linux: hunspell などに差し替える。
/// </summary>
public interface IWordChecker
{
    /// <summary>使えるか (使えなければ常に false を返す)。</summary>
    bool IsAvailable { get; }

    /// <summary>小文字の英単語 (a-z だけ) が、英語として正しい綴りか。</summary>
    bool IsWord(string lower);

    /// <summary>よくある打ち間違い (teh → the) なら、OS の自動修正の綴り。無ければ null。</summary>
    string? AutoCorrection(string lower) => null;

    /// <summary>IsWord・AutoCorrection が語と言いうる最も長い語の長さ。</summary>
    int MaxWordLength => int.MaxValue;
}
