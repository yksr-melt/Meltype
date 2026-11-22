// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 AoneSenbongi
//
// CompositionController.cs から候補の組み立てとその周辺だけを抜き出したもの。単体ではビルドできない。
// _options / _detector / Convert / TimeCandidates / EmojiBlock は元の CompositionController のメンバー。

/// <summary>変換済みの候補を先頭に持ってくる。</summary>
private static void Prefer(Clause clause, string text)
{
    clause.Candidates.Remove(text);
    clause.Candidates.Insert(0, text);
    clause.Index = 0;
}

/// <summary>
/// 文節の候補: 文の中での変換結果 → その文節だけでの変換結果 → 補助辞書の同音異義語 → ひらがな → 全角カタカナ → 半角カタカナ
/// → 日付・時刻 (いま・きょう) → 分野の辞書 → 絵文字・顔文字 (逆順)。
/// 絵文字・顔文字は最後に逆順で並べるので、変換してすぐ ↑ を押すと、いちばんよく使う絵文字になる (えがお → 😊)。
/// </summary>
private List<string> JapaneseCandidates(string reading, string? inContext)
{
    // ユーザー辞書に登録した単語は、文の中での変換結果の次に出す (文節の区切りを変えて読みが一致したときなど)。
    var candidates = Distinct(inContext);
    foreach (var word in _options.UserDictionary?.Lookup(reading) ?? [])
    {
        if (!candidates.Contains(word)) candidates.Add(word);
    }
    if (Convert(reading) is var standalone && !candidates.Contains(standalone)) candidates.Add(standalone);
    foreach (var extra in _options.Candidates?.LookupWords(reading) ?? [])
    {
        if (!candidates.Contains(extra)) candidates.Add(extra);
    }
    var katakana = CompositionText.ToKatakana(reading);
    foreach (var kana in new[] { reading, katakana, CompositionText.ToHalfWidthKatakana(katakana) })
    {
        if (!candidates.Contains(kana)) candidates.Add(kana);
    }
    // 日付・時刻 (いま・なう・きょう) は、かなの後ろに出す (いつもの候補の並びは変えない)
    foreach (var time in TimeCandidates(reading))
    {
        if (!candidates.Contains(time)) candidates.Add(time);
    }
    // 分野の辞書 (病名・駅名・人名など) は、いつもの候補・時刻の後ろに出す。
    // 候補の数が多いので、結果を 1 つ決める変換には使わない。Space で候補を開いたときに並ぶ。
    foreach (var domain in _options.Candidates?.LookupDomain(reading) ?? [])
    {
        if (!candidates.Contains(domain)) candidates.Add(domain);
    }
    foreach (var emoji in EmojiBlock(reading))
    {
        if (!candidates.Contains(emoji)) candidates.Add(emoji);
    }
    return candidates;
}

/// <summary>英語の文節の候補: 打ったまま → 固有名詞の正しい形 (GitHub) → 先頭だけ大文字 → すべて大文字 → 全角。</summary>
private List<string> EnglishCandidates(string raw)
{
    var lower = raw.ToLowerInvariant();
    var capitalized = raw.Length > 0 ? char.ToUpperInvariant(raw[0]) + raw[1..] : raw;
    return Distinct(
    [
        raw,
        _detector.ProperNouns.Canonical(lower),
        capitalized,
        raw.ToUpperInvariant(),
        CompositionText.ToFullWidth(raw),
        .. _options.UserDictionary?.Lookup(lower) ?? [],
    ]);
}

/// <summary>英語と判定した語を、ローマ字として読んだときの候補 (go → 語 ご ゴ)。読み切れなければ空。</summary>
private List<string> RomajiCandidates(string raw)
{
    // 大文字だけで打った語 (TEN) は、最後の n を ん と読む (TEN → てん)。小文字の英文の中の語 (pen) は日本語にしない
    var finalN = _options.LowercaseFinalNCandidates || raw.All(char.IsAsciiLetterUpper);
    if (_detector.Romaji.AnalyzeFragment(raw.ToLowerInvariant()) is not { IsValid: true } analysis ||
        analysis.Partial != "" && !(finalN && analysis.Partial == "n") ||
        analysis.Kana.Length == 0 && analysis.Partial.Length == 0) return [];
    return JapaneseCandidates(analysis.Kana + (analysis.Partial == "n" ? "ん" : ""), null);
}

private static List<string> Distinct(params string?[] candidates)
{
    var list = new List<string>();
    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrEmpty(candidate) && !list.Contains(candidate)) list.Add(candidate);
    }
    return list;
}
