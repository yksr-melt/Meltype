// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

/// <summary>
/// JIS かな入力の判定 (設計書 §14)。IME が閉じている状態で打たれた仮想キー列を
/// JIS かな配列として読み替え、日本語としての妥当性 (辞書との前方一致) を評価する。
/// ローマ字入力かかな入力かを厳密に識別する必要はなく、日本語らしさの加点だけを返す。
/// </summary>
public sealed class KanaDetector
{
    // JIS かな配列 (Shift なし)。仮想キーコードは日本語 106/109 キーボードのもの。
    private static readonly Dictionary<int, char> JisKana = new()
    {
        [0x31] = 'ぬ', [0x32] = 'ふ', [0x33] = 'あ', [0x34] = 'う', [0x35] = 'え', [0x36] = 'お',
        [0x37] = 'や', [0x38] = 'ゆ', [0x39] = 'よ', [0x30] = 'わ', [0xBD] = 'ほ', [0xDE] = 'へ', [0xDC] = 'ー',
        [0x51] = 'た', [0x57] = 'て', [0x45] = 'い', [0x52] = 'す', [0x54] = 'か', [0x59] = 'ん',
        [0x55] = 'な', [0x49] = 'に', [0x4F] = 'ら', [0x50] = 'せ', [0xC0] = '゛', [0xDB] = '゜',
        [0x41] = 'ち', [0x53] = 'と', [0x44] = 'し', [0x46] = 'は', [0x47] = 'き', [0x48] = 'く',
        [0x4A] = 'ま', [0x4B] = 'の', [0x4C] = 'り', [0xBB] = 'れ', [0xBA] = 'け', [0xDD] = 'む',
        [0x5A] = 'つ', [0x58] = 'さ', [0x43] = 'そ', [0x56] = 'ひ', [0x42] = 'こ', [0x4E] = 'み',
        [0x4D] = 'も', [0xBC] = 'ね', [0xBE] = 'る', [0xBF] = 'め', [0xE2] = 'ろ',
    };

    // Shift を押したときに別の文字になるキー (小書き文字・を・句読点・かぎかっこ)。
    private static readonly Dictionary<int, char> JisKanaShift = new()
    {
        [0x33] = 'ぁ', [0x34] = 'ぅ', [0x35] = 'ぇ', [0x36] = 'ぉ', [0x37] = 'ゃ', [0x38] = 'ゅ', [0x39] = 'ょ', [0x30] = 'を',
        [0x45] = 'ぃ', [0x5A] = 'っ', [0xDB] = '「', [0xDD] = '」', [0xBC] = '、', [0xBE] = '。', [0xBF] = '・',
    };

    private const string Dakuten = "かがきぎくぐけげこごさざしじすずせぜそぞただちぢつづてでとどはばひびふぶへべほぼうゔ";
    private const string Handakuten = "はぱひぴふぷへぺほぽ";

    private readonly HashSet<string> _words = new(StringComparer.Ordinal);
    private readonly HashSet<string> _prefixes = new(StringComparer.Ordinal);

    public KanaDetector(IEnumerable<string> canonicalRomajiWords, RomajiDetector romaji)
    {
        foreach (var word in canonicalRomajiWords)
        {
            var analysis = romaji.AnalyzeWord(word);
            if (!analysis.IsValid || analysis.Partial.Length > 0) continue;
            var kana = analysis.Kana;
            if (!_words.Add(kana)) continue;
            for (var i = 1; i <= kana.Length; i++) _prefixes.Add(kana[..i]);
        }
    }

    public static bool IsKanaKey(int vk) => JisKana.ContainsKey(vk);

    /// <summary>JIS かな配列でそのキーが入力するかな (濁点・半濁点のキーは ゛ ゜)。かなのキーでなければ null。</summary>
    public static char? KanaForKey(int vk, bool shift) =>
        shift && JisKanaShift.TryGetValue(vk, out var shifted) ? shifted : JisKana.TryGetValue(vk, out var kana) ? kana : null;

    /// <summary>濁点・半濁点を直前のかなに付けた文字 (か + ゛ → が)。付けられなければ null。</summary>
    public static char? Combine(char previous, char mark)
    {
        var table = mark == '゛' ? Dakuten : mark == '゜' ? Handakuten : null;
        if (table is null) return null;
        var index = table.IndexOf(previous);
        return index >= 0 && index % 2 == 0 ? table[index + 1] : null;
    }

    /// <summary>辞書 (japanese.txt) の日本語の語のかなか。</summary>
    public bool IsJapaneseWord(string kana) => _words.Contains(kana);


    /// <summary>仮想キー列をかな文字列にする。濁点・半濁点キーは直前の文字に合成する。</summary>
    public static string? ToKana(IReadOnlyList<int> keys)
    {
        var chars = new List<char>(keys.Count);
        foreach (var vk in keys)
        {
            if (!JisKana.TryGetValue(vk, out var kana)) return null;
            if (kana is '゛' or '゜')
            {
                var table = kana == '゛' ? Dakuten : Handakuten;
                if (chars.Count == 0) return null;
                var index = table.IndexOf(chars[^1]);
                if (index < 0 || index % 2 != 0) return null;
                chars[^1] = table[index + 1];
                continue;
            }
            chars.Add(kana);
        }
        return new string([.. chars]);
    }

    /// <summary>かなとして妥当なら true を返し、加点を output に積む。</summary>
    public bool Evaluate(IReadOnlyList<int> keys, List<Contribution> output)
    {
        var kana = ToKana(keys);
        if (kana is null)
        {
            output.Add(new Contribution("Kana", 0, 0, "かな配列として読めない"));
            return false;
        }
        if (kana.Length == 0) return true;
        if (kana[0] is 'ん' or 'ー')
        {
            output.Add(new Contribution("Kana", 0, 0, $"「{kana}」は語頭として不自然"));
            return false;
        }

        if (kana.Length >= 2 && _words.Contains(kana))
        {
            output.Add(new Contribution("Kana", 5, 0, $"かな入力「{kana}」が辞書の語と一致"));
        }
        else if (kana.Length >= 3 && _prefixes.Contains(kana))
        {
            output.Add(new Contribution("Kana", 4, 0, $"かな入力「{kana}」が辞書の語の先頭と一致"));
        }
        else if (kana.Length == 2 && _prefixes.Contains(kana))
        {
            output.Add(new Contribution("Kana", 2, 0, $"かな入力「{kana}」"));
        }
        else if (kana.Length >= 3)
        {
            // 辞書に無い 3 文字以上のかな列は日本語としての根拠が弱い。
            return false;
        }
        return true;
    }
}
