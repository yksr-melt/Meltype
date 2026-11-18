// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
//! Opt-in weighted segmentation, following ScoredSegmentation.cs. Keep the
//! default detector separate: changing this alpha setting must not change OFF.
use super::Dictionary;
use crate::romaji;
use std::sync::OnceLock;

const PARTICLES: &[&str] = &[
    "no", "ga", "wo", "ni", "de", "to", "ha", "mo", "wa", "he", "ya",
];
#[derive(Clone)]
struct Path {
    score: f64,
    english: Vec<(usize, usize, bool)>,
    japanese_start: usize,
}
struct Weights {
    unknown: f64,
    exponent: f64,
    adjacent: f64,
    unreadable: f64,
}
impl Weights {
    fn get() -> &'static Self {
        static WEIGHTS: OnceLock<Weights> = OnceLock::new();
        WEIGHTS.get_or_init(|| {
            let value = |name, fallback| {
                std::env::var(format!("MELTYPE_SCORED_{name}"))
                    .ok()
                    .and_then(|s| s.parse().ok())
                    .unwrap_or(fallback)
            };
            Self {
                unknown: value("UNKNOWN", 0.5),
                exponent: value("EXP", 2.0),
                adjacent: value("ADJ", 4.0),
                unreadable: value("UNREADABLE", 1.0),
            }
        })
    }
}
fn offer(slot: &mut Option<Path>, path: Path) {
    if slot
        .as_ref()
        .is_some_and(|current| current.score >= path.score)
    {
        return;
    }
    *slot = Some(path);
}
fn suru(kana: &str) -> bool {
    [
        "する",
        "すれ",
        "した",
        "して",
        "しま",
        "しな",
        "しよ",
        "しと",
        "しちゃ",
        "しろ",
        "され",
        "させ",
        "せず",
    ]
    .iter()
    .any(|s| kana.starts_with(s))
        && kana
            .chars()
            .all(|c| ('ぁ'..='ゖ').contains(&c) || c == 'ー')
}
fn pedia(lower: &str) -> bool {
    ["pedia", "pedias"].iter().any(|suffix| {
        lower
            .strip_suffix(suffix)
            .is_some_and(|stem| stem.is_empty() || stem.len() >= 3)
    })
}

impl Dictionary {
    pub(crate) fn uses_scored(enabled: bool) -> bool {
        static ENVIRONMENT: OnceLock<bool> = OnceLock::new();
        *ENVIRONMENT.get_or_init(|| std::env::var("MELTYPE_SCORED").is_ok_and(|s| s == "1"))
            || enabled
    }
    fn scored_known(&self, raw: &str) -> bool {
        let lower = raw.to_ascii_lowercase();
        lower.len() >= 3
            && lower.bytes().all(|c| c.is_ascii_lowercase())
            && (self.common_english.contains(&lower)
                || self.proper.contains(&lower)
                || lower.len() >= 4
                    && (self.english.contains(&lower)
                        || self.spell_word(&lower)
                        || self.readable_english.contains(&lower))
                || pedia(&lower))
    }

    /// Scoring uses actual sound boundaries, rather than every byte position.
    /// Symbol/name special cases still use the shared detector; their complete
    /// original rule ordering is tracked separately in the acceptance ledger.
    pub fn composition_scored(
        &self,
        raw: &str,
        final_input: bool,
        before: &str,
        after: &str,
        level: &str,
    ) -> String {
        self.composition_scored_units(raw, final_input, before, after, level, None)
    }
    pub(crate) fn composition_scored_units(
        &self,
        raw: &str,
        final_input: bool,
        before: &str,
        after: &str,
        level: &str,
        retained: Option<&crate::composition_units::Units>,
    ) -> String {
        if self.prefers_standalone_english(raw, level) {
            return raw.to_owned();
        }
        if !raw.bytes().all(|b| b.is_ascii_alphabetic()) || raw.is_empty() {
            return self.composition(raw, final_input, before, after, level);
        }
        if level != "Manual" {
            if let Some(mixed) = self.english_particle_pair(raw) {
                return mixed;
            }
            if let Some(end) = self.english_compound_end(raw) {
                return format!(
                    "{}{}",
                    &raw[..end],
                    self.composition_scored(&raw[end..], final_input, &raw[..end], after, level)
                );
            }
        }
        if let Some(end) = self.capitalized_split(raw, final_input) {
            return format!(
                "{}{}",
                &raw[..end],
                self.composition_scored(
                    &raw[end..],
                    final_input,
                    raw.get(..end).unwrap_or(before),
                    after,
                    level
                )
            );
        }
        let lower = raw.to_ascii_lowercase();
        let (bounds, pending) = romaji::unit_boundaries(
            &lower,
            |prefix| {
                (0..prefix.len().saturating_sub(2)).any(|start| {
                    let word = &prefix[start..];
                    if !self.scored_known(word) {
                        return false;
                    }
                    // CompositionText.ReadsAsJapaneseWithTsu: an English
                    // spelling such as shat must not break shatsu (しゃつ).
                    if word.ends_with('t') && lower[prefix.len()..].starts_with('s') {
                        let reading =
                            format!("{}つ", romaji::hiragana(&word[..word.len() - 1], true));
                        if self.kana_words.contains(&reading) {
                            return false;
                        }
                    }
                    true
                })
            },
            |prefix| self.scored_known(prefix),
        );
        let end = retained.map_or_else(
            || pending.unwrap_or(raw.len()),
            |u| raw.len() - u.parts().1.len(),
        );
        let mut offsets = vec![0];
        if let Some(units) = retained {
            let mut offset = 0;
            for token in units.parts().0 {
                offset += token.spelling.len();
                offsets.push(offset);
            }
        } else {
            offsets.extend((1..=end).filter(|i| bounds[*i]));
        }
        let n = offsets.len() - 1;
        let kana: Vec<_> = if let Some(units) = retained {
            units.parts().0.iter().map(|t| t.kana.clone()).collect()
        } else {
            offsets
                .windows(2)
                .map(|w| {
                    let unit = &raw[w[0]..w[1]];
                    let analysis = romaji::analyze(&lower[w[0]..], true);
                    analysis
                        .tokens
                        .first()
                        .filter(|t| t.spelling.len() == unit.len())
                        .map_or_else(|| romaji::hiragana(unit, true), |t| t.kana.clone())
                })
                .collect()
        };
        let unreadable: Vec<_> = (0..n)
            .map(|i| {
                let unit = &raw[offsets[i]..offsets[i + 1]];
                unit.len() == 1 && kana[i] == unit.to_ascii_lowercase()
            })
            .collect();
        let mut best: Vec<[Option<Path>; 2]> = (0..=n).map(|_| [None, None]).collect();
        best[0][0] = Some(Path {
            score: 0.0,
            english: vec![],
            japanese_start: 0,
        });
        let weights = Weights::get();
        let extend = |path: &Path, start: usize, end: usize, split: bool| {
            let stop = if split {
                offsets[end] + 1
            } else if end == n {
                raw.len()
            } else {
                offsets[end]
            };
            let word = &raw[offsets[start]..stop];
            let adjacent = path
                .english
                .last()
                .is_some_and(|(_, e, s)| !s && *e == start)
                && path.japanese_start == start
                && start > 0;
            let known = self.scored_known(word)
                || self.common_english.contains(&word.to_ascii_lowercase())
                || self.proper.contains(&word.to_ascii_lowercase())
                || crate::phrase_spacing::is_phrase(&word.to_ascii_lowercase());
            let score = if known {
                (word.len() as f64).powf(weights.exponent)
            } else {
                weights.unknown * word.len() as f64
            };
            let mut next = path.clone();
            next.score += score - if adjacent { weights.adjacent } else { 0.5 };
            next.english.push((start, end, split));
            next.japanese_start = if split { end + 1 } else { end };
            next
        };
        for i in 0..n {
            for e in 0..2 {
                let Some(path) = best[i][e].clone() else {
                    continue;
                };
                let previous = if i == 0 {
                    self.before_evidence(before)
                } else if e == 1 {
                    1
                } else {
                    -1
                };
                // Only an explicit capitalized-word boundary is forced. A
                // growing name such as Goo may still score as Go + お.
                if level != "Manual" && raw.as_bytes()[offsets[i]].is_ascii_uppercase() {
                    let suffix = &raw[offsets[i]..];
                    let stop = self
                        .capitalized_split(suffix, final_input)
                        .map(|end| offsets[i] + end);
                    if let Some(j) = stop.and_then(|stop| offsets.iter().position(|p| *p == stop)) {
                        offer(&mut best[j][1], extend(&path, i, j, false));
                        continue;
                    }
                }
                let mut offered_english = false;
                for j in (i + 1..=n).rev() {
                    let word = &raw[offsets[i]..if j == n { raw.len() } else { offsets[j] }];
                    let next = if j < n {
                        &raw[offsets[j]..if j + 1 == n {
                            raw.len()
                        } else {
                            offsets[j + 1]
                        }]
                    } else {
                        ""
                    };
                    if e == 1 && suru(&kana[i..j].concat()) {
                        continue;
                    }
                    let lone_sokuon = j >= 2 && j < n && kana[j - 1] == "っ" && kana[j - 2] == "ん";
                    if self.scored_english(
                        word,
                        j == n,
                        final_input,
                        previous,
                        if j == n {
                            self.after_evidence(after)
                        } else {
                            -1
                        },
                        i == 0,
                        unreadable[i..j].contains(&true) || lone_sokuon,
                        next,
                        level,
                    ) {
                        offer(&mut best[j][1], extend(&path, i, j, false));
                        offered_english = true;
                    }
                }
                let acronym = raw.as_bytes()[offsets[i]].is_ascii_uppercase()
                    && raw
                        .as_bytes()
                        .get(offsets[i] + 1)
                        .is_some_and(u8::is_ascii_uppercase);
                if !offered_english || !acronym {
                    let mut japanese = path.clone();
                    if unreadable[i] {
                        japanese.score -= weights.unreadable;
                    }
                    offer(&mut best[i + 1][0], japanese);
                }
                for j in (i + 1..n).rev() {
                    let unit = &raw[offsets[j]..offsets[j + 1]];
                    if unit.len() != 3
                        || !unit.as_bytes()[0].is_ascii_lowercase()
                        || b"aiueon".contains(&unit.as_bytes()[0])
                        || !["ha", "wa"].contains(&&unit[1..])
                    {
                        continue;
                    }
                    let covered = (j.saturating_sub(8)..=j).any(|start| {
                        (j + 1..=n.min(j + 9)).any(|end| {
                            let reading = kana[start..end].concat();
                            reading.chars().count() >= 3 && self.kana_words.0.contains(&reading)
                        })
                    });
                    if covered
                        || crate::typo::Corrector::bundled()
                            .is_common_japanese(&lower[offsets[i]..offsets[j + 1]])
                    {
                        continue;
                    }
                    let word = &raw[offsets[i]..offsets[j] + 1];
                    if self.scored_known(word)
                        && !(e == 1 && suru(&kana[i..j].concat()))
                        && self.scored_english(
                            word,
                            false,
                            final_input,
                            previous,
                            -1,
                            i == 0,
                            true,
                            &unit[1..],
                            level,
                        )
                    {
                        offer(&mut best[j + 1][0], extend(&path, i, j, true));
                    }
                }
            }
        }
        let english = best[n][1].as_ref();
        let japanese = best[n][0].as_ref();
        let ends_english = english.is_some_and(|p| japanese.is_none_or(|j| p.score >= j.score));
        let chosen = if ends_english { english } else { japanese }.expect("reachable end");
        let first_english = chosen.english.first().is_some_and(|(s, _, _)| *s == 0);
        if level != "Manual" && !first_english && !self.scored_known(raw) {
            for k in 1..n {
                let stem = &lower[..offsets[k]];
                if stem.len() < 3
                    || romaji::analyze(stem, true).valid
                    || stem.len() >= 5 && self.scored_known(stem)
                {
                    continue;
                }
                let readable = romaji::analyze(stem, false).consumed();
                if readable >= 2 {
                    continue;
                }
                let rest_of_stem = romaji::analyze(&stem[readable + 1..], false);
                if rest_of_stem.valid && rest_of_stem.partial.is_empty() {
                    continue;
                }
                let rest = &lower[offsets[k]..];
                let a = romaji::analyze(rest, false);
                if PARTICLES.iter().any(|p| rest.starts_with(p))
                    && a.valid
                    && ["", "n"].contains(&a.partial.as_str())
                {
                    return format!(
                        "{}{}{}",
                        &raw[..offsets[k]],
                        kana[k..].concat(),
                        romaji::hiragana(&raw[end..], final_input)
                    );
                }
            }
        }
        if level != "Manual" && !first_english && self.unknown_english_word(&lower) {
            let particle_word = PARTICLES
                .iter()
                .find_map(|p| lower.strip_prefix(p))
                .is_some_and(|rest| rest.len() >= 3 && self.scored_known(rest));
            if self.scored_known(raw) || !particle_word {
                return raw.into();
            }
        }
        if level != "Manual" && !self.scored_known(raw) && chosen.english.len() == 1 {
            let (start, stop, split) = chosen.english[0];
            let head = offsets[stop];
            if start == 0
                && !split
                && head <= 3
                && head < raw.len()
                && (kana[stop..]
                    .iter()
                    .any(|part| part.chars().any(|c| c.is_ascii_alphabetic()))
                    || final_input
                        && romaji::hiragana(&raw[end..], true)
                            .chars()
                            .any(|c| c.is_ascii_alphabetic()))
                && (self.common_english.contains(&lower[..head])
                    || self.proper.contains(&lower[..head]))
                && !PARTICLES.iter().any(|p| lower[head..].starts_with(p))
                && !(head + 1..=raw.len()).any(|e| self.scored_known(&lower[..e]))
            {
                return raw.into();
            }
        }
        let mut out = String::new();
        let mut position = 0;
        for &(start, end, split) in &chosen.english {
            out.push_str(&kana[position..start].concat());
            out.push_str(&raw[offsets[start]..offsets[end] + usize::from(split)]);
            if split {
                out.push_str(&romaji::hiragana(
                    &raw[offsets[end] + 1..offsets[end + 1]],
                    true,
                ));
            }
            position = end + usize::from(split);
        }
        out.push_str(&kana[position..].concat());
        if ends_english
            || raw[end..].starts_with(|c: char| c.is_ascii_uppercase())
            || n == 0
                && self.scored_english(
                    &raw[end..],
                    true,
                    final_input,
                    self.before_evidence(before),
                    self.after_evidence(after),
                    true,
                    false,
                    "",
                    level,
                )
        {
            out.push_str(&raw[end..]);
        } else {
            out.push_str(&romaji::hiragana(&raw[end..], final_input));
        }
        out
    }

    #[allow(clippy::too_many_arguments)]
    pub(super) fn scored_english(
        &self,
        raw: &str,
        at_end: bool,
        final_input: bool,
        before: i32,
        after: i32,
        start: bool,
        unreadable: bool,
        next: &str,
        level: &str,
    ) -> bool {
        if raw.is_empty() {
            return false;
        }
        let lower = raw.to_ascii_lowercase();
        let first_upper = raw.as_bytes()[0].is_ascii_uppercase();
        let next_letter = next.as_bytes().first().is_some_and(u8::is_ascii_alphabetic);
        if level != "Manual"
            && raw.len() >= 4
            && raw.as_bytes().last().is_some_and(u8::is_ascii_uppercase)
            && raw[..raw.len() - 1].bytes().all(|b| b.is_ascii_lowercase())
            && !next_letter
            && !self.japanese.contains(&lower[..lower.len() - 1])
            && !crate::typo::Corrector::bundled().is_common_japanese(&lower[..lower.len() - 1])
        {
            return true;
        }
        if !first_upper
            && raw.bytes().any(|b| b.is_ascii_uppercase())
            && !Self::allows_mixed_case(raw)
        {
            return false;
        }
        if lower.len() <= 4
            && next.as_bytes().first().map(u8::to_ascii_lowercase)
                == lower.as_bytes().last().copied()
            && !b"aiueon".contains(lower.as_bytes().last().unwrap())
        {
            let stem = romaji::analyze(&lower[..lower.len() - 1], true);
            if stem.valid && stem.partial.is_empty() {
                return false;
            }
        }
        let proper = self.proper.contains(&lower);
        let strong = self.common_english.contains(&lower) || proper;
        let spelling = self.english.contains(&lower) || self.spell_word(&lower);
        let readable = lower.len() >= 3 && self.readable_english.contains(&lower);
        let learned = crate::language_memory::get(&lower);
        let leading = PARTICLES.iter().find(|p| lower.starts_with(**p)).copied();
        if before < 0 && !strong && leading.is_some_and(|p| self.scored_known(&lower[p.len()..])) {
            return false;
        }
        let growing = at_end && !final_input;
        let prefix = || self.common_index.contains(&lower) || self.proper_index.contains(&lower);
        if lower.len() >= 2
            && !first_upper
            && !strong
            && !spelling
            && !proper
            && !readable
            && learned.is_none()
            && !pedia(&lower)
            && !(growing && prefix())
        {
            return false;
        }
        let analysis = romaji::analyze(&lower, false);
        let fragment = romaji::analyze(&lower, true);
        let small = !analysis.valid
            && fragment.valid
            && fragment.partial.is_empty()
            && !(lower.len() >= 6 && spelling);
        let spell_word = !strong && !small && spelling;
        let exact = strong || spell_word && !analysis.valid;
        let conservative = level == "Conservative";
        let prefix = growing && lower.len() >= 4 && !conservative && !small && prefix();
        if first_upper && (exact || prefix || at_end) {
            return true;
        }
        if level == "Manual" {
            return false;
        }
        if readable && Self::nasal_continues(&lower, next) {
            return false;
        }
        if readable {
            return true;
        }
        if let Some(choice) = learned {
            if !(choice
                && before < 0
                && lower.len() <= 3
                && fragment.valid
                && fragment.partial.is_empty())
            {
                return choice;
            }
        }
        if lower.len() >= 5
            && !conservative
            && spelling
            && !self.japanese_prefix(&lower)
            && !proper
            && fragment.valid
            && !(next_letter
                && !crate::typo::Corrector::bundled().is_common_japanese(&lower)
                && crate::typo::Corrector::bundled().is_common_japanese(&format!("{lower}{next}")))
        {
            if lower.contains('c') && !lower.contains("ch") {
                return true;
            }
            if fragment.kana().contains(['ぢ', 'づ'])
                && !crate::typo::Corrector::bundled().is_common_japanese(&lower)
            {
                return true;
            }
        }
        if pedia(&lower) && !crate::typo::Corrector::bundled().is_common_japanese(&lower) {
            return true;
        }
        if !(start && at_end) && lower.contains('c') {
            let c_row = lower
                .replace("ca", "ka")
                .replace("ci", "si")
                .replace("cu", "ku")
                .replace("ce", "se")
                .replace("co", "ko");
            let english_particle = (strong || spell_word)
                && lower.len() >= 4
                && before >= 0
                && PARTICLES.iter().any(|p| next.starts_with(p));
            let a = romaji::analyze(&c_row, false);
            if english_particle {
                return true;
            }
            if a.valid && ["", "n"].contains(&a.partial.as_str()) {
                return false;
            }
        }
        if raw.len() == 1 {
            return ["a", "i", "u", "r"].contains(&lower.as_str()) && before >= 1 && !next_letter;
        }
        if proper
            && lower.len() >= 4
            && next_letter
            && analysis.valid
            && analysis.partial.is_empty()
            && !PARTICLES.iter().any(|p| next.starts_with(p))
        {
            return false;
        }
        if proper
            && lower.len() >= 4
            && !self.japanese.contains(&lower)
            && (if conservative {
                !analysis.valid
            } else {
                at_end && before >= 0 || lower.len() >= 5 || !analysis.valid
            })
        {
            return true;
        }
        if growing
            && before >= 0
            && lower.len() >= 4
            && !conservative
            && !small
            && self.proper_index.contains(&lower)
            && !self.japanese_prefix(&lower)
        {
            return true;
        }
        if unreadable
            && before < 0
            && leading.is_some_and(|p| {
                lower.len() - p.len() <= 2 && romaji::analyze(&lower[p.len()..], true).valid
            })
        {
            return false;
        }
        if strong && lower.len() >= 4 && analysis.sokuon > 0 && !self.japanese_prefix(&lower) {
            return true;
        }
        if unreadable && (exact || spell_word) && (lower.len() >= 3 || strong) {
            return true;
        }
        let ambiguous = match level {
            "Aggressive" => {
                exact
                    || spell_word
                    || start
                        && at_end
                        && growing
                        && lower.len() >= 3
                        && self.common_index.contains(&lower)
            }
            "Conservative" => (exact || spell_word) && (lower.len() >= 3 || before >= 2),
            _ if start && at_end => {
                exact
                    || spell_word
                    || lower.len() == 1
                    || growing && !small && self.common_index.contains(&lower)
            }
            _ => (exact || spell_word) && lower.len() >= 3,
        };
        let mut needed = if conservative
            || level != "Aggressive"
                && lower.len() <= 2
                && analysis.valid
                && ["", "n"].contains(&analysis.partial.as_str())
        {
            2
        } else {
            1
        };
        if spell_word && analysis.valid && analysis.partial.is_empty() {
            needed = needed.max(2);
        }
        if ambiguous && (if after < 0 { before.min(1) } else { before }) + after >= needed {
            return true;
        }
        if !exact && !prefix && !spell_word {
            return false;
        }
        if !analysis.valid {
            if !exact && !prefix {
                return false;
            }
            if before < 0
                && !at_end
                && lower.len() <= 4
                && next_letter
                && fragment.valid
                && ["", "n"].contains(&fragment.partial.as_str())
                && romaji::analyze(&format!("{lower}{next}"), true).valid
            {
                return false;
            }
            if !exact && small || small && lower.len() <= 2 && before < 0 {
                return false;
            }
            return at_end && (exact || !conservative) || raw.len() >= 3;
        }
        at_end
            && (exact && (!conservative || lower.len() >= 3) || spell_word && before >= 0)
            && !analysis.partial.is_empty()
            && analysis.partial != "n"
            && (final_input || !self.japanese_prefix(&lower))
    }
}
