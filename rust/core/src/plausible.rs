// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! AIの答えが読みと合っているか。合わない答え (繰り返し・直前の文節の混入) は捨てて Mozc に任せる。
//! Windows の Zenzai だけでなく、Gamma・Qwen・Shion・Jinen・kanakanji-edge と shuntelligence も同じ判定を使う。
use std::collections::HashSet;

/// 答えが読みと合っているか (上流 1.6.0 の ZenzaiConverter.IsPlausible と同じ)。
/// 答えのかな (カタカナはひらがなとして) と記号は、読みのその位置の文字と同じでないといけない。
/// 漢字 1 文字と、英字・数字の並び 1 つは、読みの 1 文字以上に当たるとみなす (今日 → きょう)。
/// 読みに無いものを続けた答え (はーどうぇ → ハードウェアウェア) は合わない。
pub fn plausible(reading_text: &str, answer: &str) -> bool {
    fn normalize(c: char) -> char {
        match c {
            'ァ'..='ヴ' if c != 'ヵ' && c != 'ヶ' => {
                std::char::from_u32(c as u32 - 0x60).unwrap_or(c)
            }
            '！'..='～' => std::char::from_u32(c as u32 - 0xFEE0).unwrap_or(c),
            _ => c,
        }
    }
    let reading: Vec<char> = reading_text.chars().map(normalize).collect();
    // (先頭の文字, 文字数, 読みの 1 文字以上に当たるか)
    let mut tokens: Vec<(char, usize, bool)> = Vec::new();
    let chars: Vec<char> = answer.chars().collect();
    let mut at = 0;
    while at < chars.len() {
        let c = normalize(chars[at]);
        if c.is_ascii_alphanumeric() {
            let mut end = at;
            while end < chars.len() && normalize(chars[end]).is_ascii_alphanumeric() {
                end += 1;
            }
            tokens.push((c, end - at, true));
            at = end;
            continue;
        }
        let kana = matches!(c, 'ぁ'..='ゖ' | 'ー' | 'ゝ' | 'ゞ');
        tokens.push((c, 1, !kana && !is_punctuation(c)));
        at += 1;
    }
    // tokens[t] 以降で reading[r] 以降をちょうど作れるか (作れなかった組は覚えて、もう試さない)
    fn matches(
        tokens: &[(char, usize, bool)],
        reading: &[char],
        t: usize,
        r: usize,
        dead: &mut HashSet<(usize, usize)>,
    ) -> bool {
        if t == tokens.len() {
            return r == reading.len();
        }
        if r == reading.len() || dead.contains(&(t, r)) {
            return false;
        }
        let (text, length, wildcard) = tokens[t];
        let single = length == 1 && normalize(text) == reading[r];
        let matched = if !wildcard {
            single && matches(tokens, reading, t + 1, r + 1, dead)
        } else {
            single && matches(tokens, reading, t + 1, r + 1, dead)
                || (r + 1..=reading.len()).any(|end| matches(tokens, reading, t + 1, end, dead))
        };
        if !matched {
            dead.insert((t, r));
        }
        matched
    }
    matches(&tokens, &reading, 0, 0, &mut HashSet::new())
}

/// 記号・句読点・空白か (漢字と区別する。漢字は読みの 1 文字以上に当たる)。
fn is_punctuation(c: char) -> bool {
    c.is_ascii_punctuation()
        || c.is_whitespace()
        || matches!(
            c,
            '、' | '。'
                | '，'
                | '．'
                | '・'
                | '：'
                | '；'
                | '？'
                | '！'
                | '゛'
                | '゜'
                | '「'
                | '」'
                | '『'
                | '』'
                | '（'
                | '）'
                | '〔'
                | '〕'
                | '［'
                | '］'
                | '｛'
                | '｝'
                | '〈'
                | '〉'
                | '《'
                | '》'
                | '【'
                | '】'
                | '…'
                | '‥'
                | '〜'
                | '～'
                | '−'
                | '–'
                | '—'
        )
}

#[cfg(test)]
mod tests {
    use super::plausible;

    #[test]
    fn plausible_answers_follow_the_reading() {
        // 漢字 1 文字・英数字の並びは、読みの 1 文字以上に当たる
        assert!(plausible("きょう", "今日"));
        assert!(plausible("まいくろそふと", "Microsoft"));
        assert!(plausible("きょうはいい", "今日はいい"));
        assert!(plausible("はしを", "橋を"));
        // 読みに無いものを続けた答え (繰り返し・直前の文節の混入) は使わない (#1675・#1511)
        assert!(!plausible("はーどうぇ", "ハードウェアウェアウェア"));
        assert!(!plausible("なまえ", "名前は魚の"));
        // かなは読みと同じでないといけない (づ と ず・カタカナはひらがなと同じ)
        assert!(!plausible("きずく", "気づく"));
        assert!(plausible("きずく", "きずく"));
        // 読みと表記が違う語 (ばいおりん → ヴァイオリン) は、かなが合わないので Mozc の候補で作れるときだけ使う
        assert!(!plausible("ばいおりん", "ヴァイオリン"));
        // 記号は同じ位置に要る
        assert!(plausible("えーあい", "AI"));
        assert!(!plausible("えーあい", "AI。"));
    }
}
