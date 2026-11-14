// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
use crate::romaji;
use std::{
    collections::{HashMap, HashSet},
    sync::{Arc, OnceLock},
};
mod composition;
mod lattice;
mod packed_words;
mod scored;

#[derive(Clone)]
pub struct Dictionary {
    custom_names: HashMap<String, String>,
    custom_kana: Option<PrefixIndex>,
    english: Arc<HashSet<String>>,
    japanese: Arc<HashSet<String>>,
    candidates: Arc<HashMap<String, Vec<String>>>,
    // 分野の辞書 (dictionaries/domain-*.txt。医療・駅名・人名など)。ふだん使わない人には
    // 誤った候補になりうるので、ほかの候補と分けて持ち、ひらがな・カタカナの後ろに並べる (#1650)。
    domain: Arc<HashMap<String, Vec<String>>>,
    head_only_phrases: HashSet<String>,
    emoji: Arc<HashMap<String, Vec<String>>>,
    kana_words: Arc<HashSet<String>>,
    max_kana_word_bytes: usize,
    common_english: Arc<HashSet<String>>,
    readable_english: Arc<HashSet<String>>,
    standalone_english: Arc<HashSet<String>>,
    proper: Arc<HashSet<String>>,
    japanese_origin: Arc<HashSet<String>>,
    english_index: PrefixIndex,
    japanese_index: PrefixIndex,
    common_index: PrefixIndex,
    proper_index: PrefixIndex,
}

/// Immutable FST prefix queries share prebuilt system bytes across profiles.
#[derive(Clone)]
struct PrefixIndex(std::sync::Arc<fst::Set<std::borrow::Cow<'static, [u8]>>>);
impl Default for PrefixIndex {
    fn default() -> Self {
        Self::new(&HashSet::new())
    }
}
impl PrefixIndex {
    fn new(words: &HashSet<String>) -> Self {
        let mut keys: Vec<_> = words.iter().collect();
        keys.sort_unstable();
        let set = fst::Set::from_iter(keys).expect("sorted dictionary keys");
        Self(std::sync::Arc::new(
            fst::Set::new(std::borrow::Cow::Owned(set.into_fst().into_inner())).unwrap(),
        ))
    }
    fn bundled(bytes: &'static [u8]) -> Self {
        Self(std::sync::Arc::new(
            fst::Set::new(std::borrow::Cow::Borrowed(bytes)).expect("build-checked prefix FST"),
        ))
    }
    fn contains(&self, prefix: &str) -> bool {
        let fst = self.0.as_fst();
        let mut node = fst.root();
        for byte in prefix.bytes() {
            let Some(index) = node.find_input(byte) else {
                return false;
            };
            node = fst.node(node.transition_addr(index));
        }
        node.is_final() || !node.is_empty()
    }
}

fn words(text: &str) -> impl Iterator<Item = &str> {
    text.lines()
        .flat_map(|line| line.split('#').next().unwrap_or("").split_whitespace())
}

/// 日本語の語のローマ字の書き方 (japanese.txt を広げたもの)。国名の語を絞り込むのに使う。
fn japanese_romaji() -> &'static HashSet<String> {
    static SPELLINGS: OnceLock<HashSet<String>> = OnceLock::new();
    SPELLINGS.get_or_init(|| {
        words(crate::system_data::text("japanese.txt"))
            .flat_map(|word| romaji::spelling_variants(word, 64))
            .collect()
    })
}

/// 国名の語 (dictionaries/countries.txt)。英語として扱う固有名詞に足す (#1615)。
/// ローマ字として最後まで読める語 (Oman・San・Mayotte) は入れない (ワークアラウンド #1869)。
/// 固有名詞を語の頭でも英語と判定するので、日本語の語の頭とぶつかる (omanjuu → oman中)。
/// #1869 で判定を直したら、この絞り込みを外して国名の語をすべて入れる。
pub fn country_words() -> &'static [&'static str] {
    static WORDS: OnceLock<Vec<&'static str>> = OnceLock::new();
    WORDS.get_or_init(|| {
        words(crate::system_data::text("countries.txt"))
            .filter(|word| word.bytes().all(|b| b.is_ascii_alphanumeric()))
            // 2 文字の略 (St・El) は、英単語として扱うとローマ字の語 (ost) の区切りを変えてしまうため入れない (#1123)。
            // 大文字だけの略 (SAR・UK・DRC) も、国名として打つ語ではないので入れない
            .filter(|word| word.len() >= 3 && !word.bytes().all(|b| b.is_ascii_uppercase()))
            .filter(|word| {
                let lower = word.to_ascii_lowercase();
                let analysis = romaji::analyze(&lower, false);
                if analysis.valid && (analysis.partial.is_empty() || analysis.partial == "n") {
                    return false;
                }
                // 日本語の語のローマ字の途中と重なる語 (hong は nihongo、sar は saru) は入れない。
                // 語の途中で英語にすると、もとの日本語の区切りが変わる (#1648 の凍結試験)
                !japanese_romaji()
                    .iter()
                    .any(|spelling| spelling.contains(&lower))
            })
            .collect()
    })
}

impl Dictionary {
    pub(super) fn nasal_continues(lower: &str, next: &str) -> bool {
        lower
            .as_bytes()
            .last()
            .is_some_and(|c| b"aiueo".contains(c))
            && next
                .as_bytes()
                .first()
                .is_some_and(|c| c.eq_ignore_ascii_case(&b'n'))
            && next
                .as_bytes()
                .get(1)
                .is_none_or(|c| !b"aiueoy".contains(&c.to_ascii_lowercase()))
    }
    fn prefers_standalone_english(&self, raw: &str, level: &str) -> bool {
        // Capitalized spellings already use the ordinary English rules. Keep
        // the hot lower-case lookup allocation-free for unrelated inputs.
        level != "Manual"
            && self.standalone_english.contains(raw)
            && crate::language_memory::get(raw) != Some(false)
    }
    #[cfg(feature = "filesystem")]
    pub(crate) fn profile(
        directory: &std::path::Path,
        romaji: Option<std::sync::Arc<romaji::CustomTable>>,
    ) -> Option<Self> {
        let files = ["english.txt", "japanese.txt", "propernouns.txt"];
        if !files.iter().any(|name| directory.join(name).is_file()) {
            return None;
        }
        let mut dictionary = Self::bundled().clone();
        for name in files {
            let Some(text) = std::fs::read(directory.join(name)).ok().and_then(|bytes| {
                crate::user_dictionary::decode(&bytes)
                    .ok()
                    .map(|(text, _)| text)
            }) else {
                continue;
            };
            for word in text
                .lines()
                .flat_map(|line| {
                    line.split('#')
                        .next()
                        .unwrap_or("")
                        .split([' ', '\t', '\r', ','])
                })
                .filter(|word| !word.is_empty())
            {
                let lower = word.to_ascii_lowercase();
                if name == "propernouns.txt" {
                    if !lower.bytes().all(|c| c.is_ascii_alphanumeric()) {
                        continue;
                    }
                    if !dictionary.proper.contains(&lower) {
                        dictionary
                            .custom_names
                            .entry(lower.clone())
                            .or_insert_with(|| word.into());
                    }
                    Arc::make_mut(&mut dictionary.proper).insert(lower.clone());
                    Arc::make_mut(&mut dictionary.common_english).insert(lower.clone());
                    Arc::make_mut(&mut dictionary.english).insert(lower);
                } else if lower.bytes().all(|c| c.is_ascii_lowercase()) {
                    if name == "english.txt" {
                        Arc::make_mut(&mut dictionary.common_english).insert(lower.clone());
                        Arc::make_mut(&mut dictionary.english).insert(lower);
                    } else {
                        Arc::make_mut(&mut dictionary.japanese).insert(lower);
                    }
                }
            }
        }
        dictionary.english_index = PrefixIndex::new(&dictionary.english);
        dictionary.common_index = PrefixIndex::new(&dictionary.common_english);
        dictionary.proper_index = PrefixIndex::new(&dictionary.proper);
        dictionary.japanese_index = PrefixIndex::new(&dictionary.japanese);
        let _romaji = romaji::Scope::new(romaji);
        dictionary.custom_kana = Some(PrefixIndex::new(
            &dictionary
                .japanese
                .iter()
                .map(|word| romaji::hiragana(word, true))
                .collect(),
        ));
        Some(dictionary)
    }
    pub(crate) fn english_candidates(&self, raw: &str) -> Vec<String> {
        static CANONICAL: OnceLock<HashMap<String, String>> = OnceLock::new();
        let names = CANONICAL.get_or_init(|| {
            let mut names = HashMap::new();
            for word in words(crate::system_data::text("propernouns.txt"))
                .flat_map(|s| s.split(','))
                .chain(country_words().iter().copied())
                .filter(|s| !s.is_empty() && s.bytes().all(|b| b.is_ascii_alphanumeric()))
            {
                names
                    .entry(word.to_ascii_lowercase())
                    .or_insert_with(|| word.to_owned());
            }
            names
        });
        let mut values = vec![raw.to_owned()];
        if let Some(name) = names
            .get(&raw.to_ascii_lowercase())
            .or_else(|| self.custom_names.get(&raw.to_ascii_lowercase()))
        {
            values.push(name.clone());
        }
        let mut capitalized = raw.to_owned();
        if let Some(first) = capitalized.get_mut(..1) {
            first.make_ascii_uppercase();
        }
        values.extend([
            capitalized,
            raw.to_ascii_uppercase(),
            crate::kana::fullwidth(raw),
        ]);
        let mut unique = vec![];
        for value in values {
            if !unique.contains(&value) {
                unique.push(value);
            }
        }
        unique
    }
    pub fn bundled() -> &'static Self {
        static DICT: OnceLock<Dictionary> = OnceLock::new();
        DICT.get_or_init(|| {
            let _builtins = romaji::Scope::new(None);
            let mut dict = Self {
                custom_names: HashMap::new(),
                custom_kana: None,
                english: Arc::default(),
                english_index: PrefixIndex::default(),
                japanese_index: PrefixIndex::default(),
                common_index: PrefixIndex::default(),
                proper_index: PrefixIndex::default(),
                japanese_origin: Arc::new({
                    let mut section = false;
                    let mut result = HashSet::new();
                    for line in crate::system_data::text("english.txt").lines() {
                        if line.starts_with("# ---") {
                            section = line.contains("日本語由来");
                        } else if line.trim().is_empty() {
                            section = false;
                        } else if section && !line.starts_with('#') {
                            result.extend(words(line).map(str::to_owned));
                        }
                    }
                    result
                }),
                proper: Arc::new(
                    words(crate::system_data::text("propernouns.txt"))
                        .chain(words(crate::system_data::text("brands.txt")))
                        .chain(country_words().iter().copied())
                        .filter(|word| word.is_ascii())
                        .map(str::to_ascii_lowercase)
                        .collect(),
                ),
                japanese: Arc::new(
                    words(crate::system_data::text("japanese.txt"))
                        .map(str::to_owned)
                        .collect(),
                ),
                candidates: Arc::default(),
                domain: Arc::default(),
                head_only_phrases: HashSet::new(),
                emoji: Arc::default(),
                max_kana_word_bytes: 0,
                kana_words: Arc::new(
                    crate::system_data::text("readings.txt")
                        .lines()
                        .chain(crate::system_data::text("readings-extra.txt").lines())
                        .filter(|l| !l.starts_with('#'))
                        .filter_map(|l| l.split_whitespace().next())
                        .map(str::to_owned)
                        .collect(),
                ),
                common_english: Arc::new(
                    words(crate::system_data::text("english.txt"))
                        .map(str::to_owned)
                        .collect(),
                ),
                readable_english: Arc::new(
                    words(crate::system_data::text("english-readable.txt"))
                        .map(str::to_owned)
                        .collect(),
                ),
                standalone_english: Arc::new(
                    words(crate::system_data::text("english-standalone.txt"))
                        .map(str::to_owned)
                        .collect(),
                ),
            };
            for source in [
                crate::system_data::text("english.txt"),
                crate::system_data::text("english-words.txt"),
                crate::system_data::text("propernouns.txt"),
                crate::system_data::text("brands.txt"),
            ] {
                Arc::make_mut(&mut dict.english).extend(words(source).map(str::to_ascii_lowercase));
            }
            Arc::make_mut(&mut dict.english).extend(dict.readable_english.iter().cloned());
            for source in [
                crate::system_data::text("candidates.txt"),
                crate::system_data::text("loanwords.txt"),
            ] {
                for line in source.lines() {
                    let fields: Vec<_> = line
                        .split('#')
                        .next()
                        .unwrap_or("")
                        .split_whitespace()
                        .collect();
                    if fields.len() > 1 {
                        dict.add(fields[0], fields[1..].iter().copied());
                    }
                }
            }
            // 分野の辞書 (domain-*.txt)。形式は「読み<Tab>表記<Tab>表記 …」、行頭が # の行はコメント
            for name in crate::system_data::names("domain-") {
                for line in crate::system_data::text(&name).lines() {
                    if line.starts_with('#') {
                        continue;
                    }
                    // 読みの後ろはタブ区切り。表記の中の空白は語の一部なので、行全体をタブで分ける
                    let mut fields = line.split('\t');
                    let Some(reading) = fields.next().map(str::trim) else {
                        continue;
                    };
                    let values: Vec<_> = fields
                        .map(str::trim)
                        .filter(|word| !word.is_empty())
                        .collect();
                    if reading.is_empty() || values.is_empty() {
                        continue;
                    }
                    let entry = Arc::make_mut(&mut dict.domain)
                        .entry(reading.to_owned())
                        .or_insert_with(|| Vec::with_capacity(values.len()));
                    for value in values {
                        if !entry.iter().any(|existing| existing == value) {
                            entry.push(value.to_owned());
                        }
                    }
                }
            }
            for (source, emoji) in [
                (crate::system_data::text("phrases.txt"), false),
                (crate::system_data::text("emoji.txt"), true),
                (crate::system_data::text("emoji-cldr.txt"), true),
            ] {
                for line in source.lines().filter(|l| !l.starts_with('#')) {
                    let fields: Vec<_> = line.split('\t').collect();
                    if fields.len() > 1 {
                        if emoji {
                            let entries = Arc::make_mut(&mut dict.emoji)
                                .entry(fields[0].to_owned())
                                .or_insert_with(|| Vec::with_capacity(fields.len() - 1));
                            for word in &fields[1..] {
                                if !word.is_empty() && !entries.iter().any(|s| s == word) {
                                    entries.push((*word).to_owned());
                                }
                            }
                        } else {
                            let reading = fields[0].trim_start_matches('^').replace('|', "");
                            if fields[0].starts_with('^') {
                                dict.head_only_phrases.insert(reading.clone());
                            }
                            let values: Vec<_> = fields[1..]
                                .iter()
                                .map(|word| word.replace('|', ""))
                                .collect();
                            dict.add(&reading, values.iter().map(String::as_str));
                        }
                    }
                }
            }
            dict.english_index =
                PrefixIndex::bundled(include_bytes!(concat!(env!("OUT_DIR"), "/english.fst")));
            dict.japanese_index =
                PrefixIndex::bundled(include_bytes!(concat!(env!("OUT_DIR"), "/japanese.fst")));
            dict.common_index =
                PrefixIndex::bundled(include_bytes!(concat!(env!("OUT_DIR"), "/common.fst")));
            dict.proper_index =
                PrefixIndex::bundled(include_bytes!(concat!(env!("OUT_DIR"), "/proper.fst")));
            dict.max_kana_word_bytes = dict.kana_words.iter().map(String::len).max().unwrap_or(0);
            dict
        })
    }

    fn add<'a>(&mut self, reading: &str, values: impl Iterator<Item = &'a str>) {
        // System rows already tell us their initial candidate count. Avoid Vec's
        // four-element minimum allocation for the common one-candidate row.
        let entry = Arc::make_mut(&mut self.candidates)
            .entry(reading.to_owned())
            .or_insert_with(|| Vec::with_capacity(values.size_hint().0));
        for value in values.filter(|s| !s.is_empty()) {
            if !entry.iter().any(|v| v == value) {
                entry.push(value.to_owned());
            }
        }
    }

    pub fn candidates(&self, reading: &str, timezone: &str) -> Vec<String> {
        self.candidates_with_front(reading, timezone, &crate::version::default_front())
    }

    /// `candidates` と同じだが、「ばーじょん」の候補に OS とフロントを書く (#1868)。
    pub fn candidates_with_front(&self, reading: &str, timezone: &str, front: &str) -> Vec<String> {
        let words = self
            .candidates
            .get(reading)
            .map(Vec::as_slice)
            .unwrap_or(&[]);
        let mut values: Vec<_> = words
            .iter()
            .filter(|s| {
                s.chars()
                    .any(|c| crate::linguistics::kana(c) || crate::linguistics::kanji(c))
            })
            .cloned()
            .collect();
        let converted = self.convert(reading);
        if converted != reading && !values.contains(&converted) {
            values.insert(0, converted);
        }
        let katakana = romaji::katakana(reading);
        for value in [
            reading.to_owned(),
            katakana.clone(),
            crate::kana::halfwidth(&katakana),
        ] {
            if !values.contains(&value) {
                values.push(value);
            }
        }
        // 分野の辞書の語 (病名・駅名など) は、使わない人には要らない候補なので、
        // ふだんの候補 (変換エンジンの候補・ひらがな・カタカナ) を 1 つも後ろに押さないよう、その後ろに並べる。
        // こうすれば誰にとっても困らないので、分野ごとに外す設定は作らない (#1650)。
        for word in self.domain_words(reading) {
            if !values.contains(&word) {
                values.push(word);
            }
        }
        values.extend(crate::time_candidates::candidates(
            reading,
            chrono::Local::now().naive_local(),
            timezone,
        ));
        values.extend(crate::currency::candidates(reading));
        if reading == "ばーじょん" {
            values.push(crate::version::candidate(front));
        }
        for word in words {
            if !values.contains(word) {
                values.push(word.clone());
            }
        }
        for word in self.emoji(reading) {
            if !values.contains(&word) {
                values.push(word);
            }
        }
        values
    }

    /// 分野の辞書の候補 (病名・駅名・人名など)。文節の読みは助詞などを含む (せんたんきょだいしょうの) ので、
    /// 辞書にある最長の先頭部分を置き換え、残りはかなのまま付ける (先端巨大症の)。
    pub fn domain_words(&self, reading: &str) -> Vec<String> {
        // 語の後ろに付いていてよい助詞・「だ」など
        const ENDINGS: &[&str] = &[
            "", "を", "が", "は", "に", "で", "と", "も", "へ", "の", "や", "な", "だ", "です",
            "から", "まで", "より", "って", "とか", "さ", "ね", "よ",
        ];
        for end in reading.char_indices().map(|(i, c)| i + c.len_utf8()).rev() {
            // 1 文字の読み (い → 位 胃 …) は、その読みだけの文節のときだけ (いきを → 位きを にしない)
            if reading[..end].encode_utf16().count() == 1 && end < reading.len() {
                break;
            }
            let suffix = &reading[end..];
            // 残りが助詞など (はし|を) のときだけ。語の途中 (ふく|ざつな) では使わない
            if !ENDINGS.contains(&suffix) {
                continue;
            }
            if let Some(words) = self.domain.get(&reading[..end]) {
                return words.iter().map(|word| format!("{word}{suffix}")).collect();
            }
        }
        Vec::new()
    }

    pub fn emoji(&self, reading: &str) -> Vec<String> {
        self.emoji
            .get(reading)
            .map(|words| words.iter().rev().cloned().collect())
            .unwrap_or_default()
    }

    /// Deterministic reverse lookup for selection reconversion. Ambiguous
    /// readings prefer the shortest reading, then lexical order.
    pub fn reading_for(&self, text: &str) -> Option<String> {
        self.candidates
            .iter()
            .chain(self.emoji.iter())
            .filter(|(_, words)| words.iter().any(|word| word == text))
            .map(|(reading, _)| reading)
            .min_by(|a, b| {
                a.chars()
                    .count()
                    .cmp(&b.chars().count())
                    .then_with(|| a.cmp(b))
            })
            .cloned()
    }
    /// Dictionary fallback, used when no system kana-kanji converter is attached.
    /// Longest readings win; this is intentionally not a replacement for Mozc.
    pub fn convert(&self, reading: &str) -> String {
        let mut rest = reading;
        let mut out = String::new();
        let mut boundary = true;
        while !rest.is_empty() {
            let longest = rest
                .char_indices()
                .map(|(i, c)| i + c.len_utf8())
                .rev()
                .find_map(|end| {
                    self.candidates
                        .get(&rest[..end])
                        .filter(|_| boundary || !self.head_only_phrases.contains(&rest[..end]))
                        .filter(|_| rest[..end].chars().count() >= 2)
                        .and_then(|values| {
                            values
                                .iter()
                                .find(|s| {
                                    s.chars().any(|c| {
                                        crate::linguistics::kana(c) || crate::linguistics::kanji(c)
                                    })
                                })
                                .map(|s| (end, s))
                        })
                });
            if let Some((end, text)) = longest {
                out.push_str(text);
                rest = &rest[end..];
                boundary = true;
            } else {
                let c = rest.chars().next().unwrap();
                out.push(c);
                rest = &rest[c.len_utf8()..];
                boundary = false;
            }
        }
        out
    }

    pub fn mixed(&self, raw: &str, final_input: bool) -> String {
        let first = raw
            .split_whitespace()
            .next()
            .unwrap_or("")
            .to_ascii_lowercase();
        let english_sentence = raw.contains(' ')
            && [
                "i", "the", "please", "what", "how", "this", "my", "do", "let", "can", "make",
                "git", "npm", "good", "thank", "see", "run", "open", "meeting",
            ]
            .contains(&first.as_str());
        self.mixed_context(raw, final_input, english_sentence)
    }
    pub fn mixed_context(&self, raw: &str, final_input: bool, english_context: bool) -> String {
        self.mixed_context_evidence(
            raw,
            final_input,
            if english_context { 2 } else { 0 },
            0,
            "Balanced",
        )
    }
    fn mixed_context_evidence(
        &self,
        raw: &str,
        final_input: bool,
        before: i32,
        after: i32,
        level: &str,
    ) -> String {
        let english_context = before + after > 0;
        let mut out = String::new();
        let mut start = 0;
        // Preserve URLs, email, file names, and agent commands as whole spans.
        for token in raw.split_inclusive(char::is_whitespace) {
            let word = token.trim_end_matches(char::is_whitespace);
            if word.contains("://")
                || word.bytes().any(|c| c.is_ascii_digit())
                    && word.bytes().any(|c| c.is_ascii_alphabetic())
                    && word.bytes().all(|c| c.is_ascii_alphanumeric())
                    && !self.number_with_japanese_unit(word)
                || word.contains('@')
                || word.starts_with(['/', '$'])
                || word
                    .split_once('\\')
                    .is_some_and(|(_, tail)| !tail.is_empty())
                || word.get(1..).is_some_and(|tail| tail.starts_with(":\\"))
                || word.contains('_')
                || word.split_once('.').is_some_and(|(left, right)| {
                    !left.is_empty()
                        && !right.is_empty()
                        && left.bytes().all(|c| c.is_ascii_alphanumeric() || c == b'-')
                        && right
                            .bytes()
                            .all(|c| c.is_ascii_alphanumeric() || c == b'.')
                })
            {
                out.push_str(token);
            } else {
                let mut run = String::new();
                for c in token.chars().chain(std::iter::once('\0')) {
                    if c.is_ascii_alphabetic() || c == '\'' {
                        run.push(c);
                    } else {
                        if !run.is_empty() {
                            let preceding = if out.is_empty() {
                                before
                            } else if out
                                .chars()
                                .next_back()
                                .is_some_and(crate::linguistics::kana)
                            {
                                -1
                            } else {
                                before
                            };
                            let after_long_vowel =
                                out.ends_with('ー') && !self.known_english(&run) && {
                                    let analysis = romaji::analyze(&run, true);
                                    analysis.valid
                                        && (analysis.partial.is_empty()
                                            || final_input && analysis.partial == "n")
                                };
                            if after_long_vowel {
                                // #1091: the English prefix "this" inside
                                // pa-thisyon is part of the same loanword.
                                out.push_str(&romaji::hiragana(&run, final_input));
                            } else if english_context
                                && self.scored_english(
                                    &run,
                                    true,
                                    final_input,
                                    preceding,
                                    after,
                                    true,
                                    false,
                                    "",
                                    level,
                                )
                            {
                                out.push_str(&run);
                            } else {
                                out.push_str(&self.mixed_word(&run, final_input));
                            }
                            run.clear();
                        }
                        if c != '\0' {
                            out.push_str(&romaji::hiragana(&c.to_string(), true));
                        }
                    }
                }
            }
            start += token.len();
        }
        debug_assert_eq!(start, raw.len());
        out
    }

    /// Capitalized words following an ordinary lower-case English word are
    /// separate clauses. Preserve canonical names and structured identifiers.
    pub fn english_parts<'a>(&self, raw: &'a str) -> Vec<&'a str> {
        if !raw.is_ascii()
            || !raw.starts_with(|c: char| c.is_ascii_lowercase())
            || self.known_english(raw)
            || raw
                .as_bytes()
                .windows(3)
                .filter(|w| {
                    w[0].is_ascii_lowercase()
                        && w[1].is_ascii_uppercase()
                        && w[2].is_ascii_lowercase()
                })
                .count()
                >= 2
        {
            return vec![raw];
        }
        for i in 1..raw.len() {
            if raw.as_bytes()[i].is_ascii_uppercase()
                && raw.as_bytes()[i - 1].is_ascii_lowercase()
                && self.known_english(&raw[..i])
            {
                return vec![&raw[..i], &raw[i..]];
            }
        }
        vec![raw]
    }
    pub(crate) fn rendered_english_tail(&self, text: &str, level: &str) -> bool {
        let start = text
            .rfind(|c: char| !c.is_ascii_alphabetic())
            .map_or(0, |i| i + text[i..].chars().next().unwrap().len_utf8());
        let parts = self.english_parts(&text[start..]);
        let tail = parts.last().copied().unwrap_or("");
        if tail.is_empty() {
            return false;
        }
        self.scored_english(
            tail,
            true,
            true,
            if parts.len() > 1 {
                1
            } else if start == 0 {
                0
            } else {
                -1
            },
            0,
            start == 0,
            !romaji::analyze(&tail.to_ascii_lowercase(), true).valid,
            "",
            level,
        )
    }
    pub fn english_context(&self, before: &str) -> bool {
        let sentence = before
            .rsplit(['。', '！', '？', '\n', '\r'])
            .next()
            .unwrap_or("");
        if sentence
            .chars()
            .any(|c| crate::linguistics::kana(c) || crate::linguistics::kanji(c))
        {
            return false;
        }
        let words: Vec<_> = sentence
            .split_whitespace()
            .map(|s| s.trim_matches(|c: char| c.is_ascii_punctuation()))
            .filter(|s| !s.is_empty())
            .collect();
        let Some(last) = words.last() else {
            return false;
        };
        if !last.bytes().all(|c| c.is_ascii_alphabetic()) {
            return false;
        }
        if self.proper.contains(&last.to_ascii_lowercase()) {
            return words[..words.len() - 1].iter().any(|word| {
                [
                    "i", "we", "you", "they", "the", "this", "my", "is", "are", "a", "an",
                    "please", "use", "have", "want", "like",
                ]
                .contains(&word.to_ascii_lowercase().as_str())
            });
        }
        true
    }
    fn number_with_japanese_unit(&self, word: &str) -> bool {
        let Some(start) = word.find(|c: char| c.is_ascii_digit()) else {
            return false;
        };
        let suffix = word[start..].trim_start_matches(|c: char| c.is_ascii_digit());
        if start >= 2 && word.as_bytes()[start - 1].is_ascii_uppercase() {
            let stem = &word[..start - 1];
            let reading = romaji::analyze(stem, true);
            if stem.bytes().all(|c| c.is_ascii_lowercase())
                && reading.valid
                && reading.partial.is_empty()
                && !self.known_english(stem)
            {
                return true;
            }
        }
        let units = [
            "ji", "fun", "pun", "gatsu", "nichi", "nen", "jikan", "en", "nin", "sai", "mai", "hon",
            "bon", "ko", "kai", "kakeru", "waru", "tasu", "hiku", "purasu", "mainasu", "iko-ru",
        ];
        if let Some(next) = suffix.find(|c: char| c.is_ascii_digit()) {
            let tail = suffix[next..].trim_start_matches(|c: char| c.is_ascii_digit());
            if !tail.is_empty() && !units.iter().any(|unit| tail.starts_with(unit)) {
                return false;
            }
        }
        units.iter().any(|unit| suffix.starts_with(unit))
    }
    fn mixed_word(&self, raw: &str, final_input: bool) -> String {
        if self.prefers_standalone_english(raw, "Balanced") {
            return raw.to_owned();
        }
        if let Some(end) = self.english_compound_end(raw) {
            return format!(
                "{}{}",
                &raw[..end],
                self.mixed_word(&raw[end..], final_input)
            );
        }
        let lower = raw.to_ascii_lowercase();
        if self.starts_with_japanese_reading(raw) {
            let upper = raw.bytes().position(|c| c.is_ascii_uppercase()).unwrap();
            return format!(
                "{}{}",
                romaji::hiragana(&raw[..upper], true),
                self.mixed_word(&raw[upper..], final_input)
            );
        }
        let canonical_case = Self::allows_mixed_case(raw);
        if let Some(end) = self.capitalized_split(raw, final_input) {
            return format!(
                "{}{}",
                &raw[..end],
                romaji::hiragana(&raw[end..], final_input)
            );
        }
        // Preserve a product's internal capitals before an attached particle.
        for particle in [
            "no", "ga", "wo", "ni", "de", "to", "ha", "mo", "wa", "he", "ya",
        ] {
            if let Some(prefix) = raw.strip_suffix(particle) {
                if prefix.len() >= 3
                    && !self.english.contains(&lower)
                    && !self.starts_with_japanese_reading(prefix)
                    && prefix.bytes().skip(1).any(|b| b.is_ascii_uppercase())
                    && prefix.bytes().any(|b| b.is_ascii_lowercase())
                {
                    return format!("{}{}", prefix, romaji::hiragana(particle, true));
                }
            }
        }
        // CompositionDetector.IsEnglishSpan: a growing proper-name prefix at
        // the start of input is English unless it is also a Japanese prefix.
        if !final_input
            && canonical_case
            && lower.len() >= 4
            && (self.proper_index.contains(&lower)
                || !romaji::analyze(&lower, false).valid && self.common_index.contains(&lower))
            && !self.japanese_prefix(&lower)
            && (romaji::analyze(&lower, false).valid || {
                let fragment = romaji::analyze(&lower, true);
                !fragment.valid || !fragment.partial.is_empty()
            })
        {
            return raw.to_owned();
        }
        if raw.starts_with(|c: char| c.is_ascii_uppercase()) {
            return raw.to_owned();
        }
        // A trailing capital name follows IsTrailingCapitalName's common-
        // Japanese exclusion (macO, but koreX -> これX).
        if raw.len() >= 4
            && raw.as_bytes().last().is_some_and(u8::is_ascii_uppercase)
            && !self.starts_with_japanese_reading(raw)
            && raw[..raw.len() - 1].bytes().all(|c| c.is_ascii_lowercase())
            && !self.japanese.contains(&lower[..lower.len() - 1])
            && !crate::typo::Corrector::bundled().is_common_japanese(&lower[..lower.len() - 1])
        {
            return raw.to_owned();
        }
        // FindSpans tries each later sound boundary after rejecting a
        // common-Japanese whole stem with a final capital (tanniG -> たnniG).
        // The remaining suffix can itself satisfy IsTrailingCapitalName.
        if raw.len() >= 4
            && raw.as_bytes().last().is_some_and(u8::is_ascii_uppercase)
            && !self.starts_with_japanese_reading(raw)
            && raw[..raw.len() - 1].bytes().all(|c| c.is_ascii_lowercase())
        {
            let (bounds, _) = romaji::unit_boundaries(
                &lower,
                |word| self.known_english(word),
                |word| self.known_english(word),
            );
            for start in 1..raw.len().saturating_sub(3) {
                let stem = &lower[start..lower.len() - 1];
                if bounds[start]
                    && !self.japanese.contains(stem)
                    && !crate::typo::Corrector::bundled().is_common_japanese(stem)
                {
                    return format!("{}{}", romaji::hiragana(&raw[..start], true), &raw[start..]);
                }
            }
        }
        if ["no", "ya", "repo", "to"].contains(&lower.as_str()) {
            return romaji::hiragana(raw, final_input);
        }
        let suffix = if lower.ends_with("pedias") {
            6
        } else if lower.ends_with("pedia") {
            5
        } else {
            0
        };
        if suffix > 0 && (lower.len() == suffix || lower.len() >= suffix + 3) {
            return raw.to_owned();
        }
        // Original IsEnglishSpan accepts known c-row English spellings before
        // matching their kana against Japanese readings (camera vs kamera).
        if (lower.len() >= 5 || lower.len() >= 4 && self.common_english.contains(&lower))
            && lower.contains('c')
            && !lower.contains("ch")
            && self.known_english(&lower)
            && !self.japanese_prefix(&lower)
            && !self.proper.contains(&lower)
            && romaji::analyze(&lower, true).valid
        {
            return raw.to_owned();
        }
        if self.japanese.contains(&lower) {
            return romaji::hiragana(raw, final_input);
        }
        let reading = romaji::hiragana(raw, final_input);
        if !self.readable_english.contains(&lower) && self.kana_words.contains(&reading) {
            return reading;
        }
        if canonical_case && self.english.contains(&lower) {
            let fragment = romaji::analyze(&lower, true);
            let strict = romaji::analyze(&lower, false);
            if !self.readable_english.contains(&lower)
                && !self.proper.contains(&lower)
                && fragment.valid
                && (strict.valid && fragment.partial == "n"
                    || !final_input && !fragment.partial.is_empty() && self.japanese_prefix(&lower))
            {
                return romaji::hiragana(raw, final_input);
            }
            if lower.len() < 6
                && fragment.valid
                && fragment.partial.is_empty()
                && !romaji::analyze(&lower, false).valid
                && !self.common_english.contains(&lower)
                && !self.proper.contains(&lower)
                && !self.readable_english.contains(&lower)
            {
                return romaji::hiragana(raw, final_input);
            }
            if lower.len() <= 3
                && fragment.valid
                && fragment.partial.is_empty()
                && !self.readable_english.contains(&lower)
            {
                return romaji::hiragana(raw, final_input);
            }
            return raw.to_owned();
        }
        if canonical_case && self.spell_word(&lower) {
            let analysis = romaji::analyze(&lower, false);
            let fragment = romaji::analyze(&lower, true);
            let small_kana =
                !analysis.valid && fragment.valid && fragment.partial.is_empty() && lower.len() < 6;
            if !small_kana
                && (!fragment.valid
                    || !analysis.partial.is_empty()
                        && analysis.partial != "n"
                        && analysis.partial != "nn")
            {
                return raw.to_owned();
            }
        }
        // An English word on each side supplies evidence for a Japanese particle,
        // even when the same letters also form a valid Japanese syllable sequence.
        for index in 2..raw.len() {
            for particle in [
                "no", "ga", "wo", "ni", "de", "to", "ha", "mo", "wa", "he", "ya",
            ] {
                if let Some(right) = lower[index..].strip_prefix(particle) {
                    if right.len() >= 2
                        && self.english_atom(&lower[..index])
                        && self.english_atom(right)
                        && (!self.is_japanese(&lower[..index])
                            || self.proper.contains(&lower[..index]))
                        && (!self.is_japanese(right) || self.proper.contains(right))
                    {
                        return format!(
                            "{}{}{}",
                            &raw[..index],
                            romaji::hiragana(particle, true),
                            &raw[index + particle.len()..]
                        );
                    }
                }
            }
        }
        // Shortest-path segmentation over ASCII byte boundaries. Unknown syllables
        // remain kana; dictionary words can form English islands inside Japanese.
        let n = raw.len();
        let (boundaries, pending_start) = romaji::unit_boundaries(
            &lower,
            |prefix| {
                (0..prefix.len().saturating_sub(2)).any(|start| {
                    let word = &prefix[start..];
                    self.common_english.contains(word)
                        || self.proper.contains(word)
                        || self.readable_english.contains(word)
                        || word.len() >= 4 && (self.is_english(word) || self.spell_word(word))
                })
            },
            |prefix| self.known_english(prefix),
        );
        let mut costs = vec![f64::INFINITY; n + 1];
        let mut paths = vec![(0, false); n + 1];
        costs[0] = 0.0;
        // Repeated syllables/words share probes within this composition only.
        // Never retain results across custom romaji or spelling scopes. Avoid
        // the map overhead on short interactive inputs.
        let mut analyses = HashMap::new();
        let probe_cache = romaji::probes::Cache::current();
        for i in 0..n {
            for end in i + 1..=n.min(i + 40) {
                let piece = &lower[i..end];
                let computed;
                let shared;
                let (piece_reading, strict) = if let Some(cache) = &probe_cache {
                    shared = cache.get(piece);
                    (&shared.reading, &shared.strict)
                } else if n >= 64 {
                    let probe = analyses.entry(piece).or_insert_with(|| {
                        (romaji::hiragana(piece, true), romaji::analyze(piece, false))
                    });
                    (&probe.0, &probe.1)
                } else {
                    computed = (romaji::hiragana(piece, true), romaji::analyze(piece, false));
                    (&computed.0, &computed.1)
                };
                let jp = !raw[i..end].bytes().any(|c| c.is_ascii_uppercase())
                    && !((piece.len() >= 5
                        || piece.len() >= 4 && self.common_english.contains(piece))
                        && piece.contains('c')
                        && !piece.contains("ch")
                        && self.known_english(piece)
                        && !self.japanese_prefix(piece)
                        && !self.proper.contains(piece)
                        && romaji::analyze(piece, true).valid)
                    && (self.japanese.contains(piece)
                        || self.kana_words.contains(piece_reading)
                        || [
                            "ha", "wa", "wo", "ga", "no", "ni", "de", "to", "mo", "he", "ya",
                        ]
                        .contains(&piece));
                let acronym =
                    piece.len() >= 2 && raw[i..end].bytes().all(|c| c.is_ascii_uppercase());
                let growing_english = !final_input
                    && end == n
                    && piece.len() >= 4
                    && !strict.valid
                    && (self.common_index.contains(piece) || self.proper_index.contains(piece))
                    && {
                        let fragment = romaji::analyze(piece, true);
                        !fragment.valid || !fragment.partial.is_empty()
                    };
                let capitalized = end == n
                    && raw.as_bytes()[i].is_ascii_uppercase()
                    && !(piece.len() > 2
                        && !self.known_english(piece)
                        && [
                            "kara", "made", "yori", "ga", "wo", "ni", "de", "no", "to", "mo", "ha",
                            "wa", "ya",
                        ]
                        .iter()
                        .any(|p| piece[1..].starts_with(p))
                        && romaji::analyze(&piece[1..], true).valid);
                let english_piece = boundaries[i]
                    && boundaries[end]
                    && !(self.readable_english.contains(piece)
                        && Self::nasal_continues(piece, &raw[end..]))
                    && !(end < n && raw.as_bytes()[end-1] == raw.as_bytes()[end]
                        && raw.as_bytes()[end].is_ascii_alphabetic()
                        && !b"aeioun".contains(&raw.as_bytes()[end]))
                    // In FindSpans, a span ending at the final completed unit
                    // must include Pending. It cannot stop just before it.
                    && (pending_start != Some(end) || end == n)
                    && Self::allows_mixed_case(&raw[i..end])
                    && ((piece.len() >= 3
                        && !jp
                        && (self.english.contains(piece) || self.spell_word(piece)))
                        // A listed trailing word with a pending consonant
                        // remains English even when it is only two letters
                        // (gakuse|id). Completed two-letter syllables keep the
                        // ordinary Japanese/context rules.
                        || end == n
                            && self.common_english.contains(piece)
                            && strict.valid
                            && !strict.partial.is_empty()
                            && strict.partial != "n"
                            && (final_input || !self.japanese_prefix(piece))
                        // Chat abbreviations are independent single-letter
                        // words only at the end, immediately after English.
                        || end == n && i > 0 && paths[i].1 && ["a", "i", "u", "r"].contains(&piece)
                        || acronym
                        || growing_english
                        || capitalized);
                // IsEnglishSpan keeps a short composition-only spelling in
                // Japanese when its next sound continues it (hosu|thin|gu).
                let japanese_continuation = english_piece
                    && i > 0
                    && !paths[i].1
                    && end < n
                    && piece.len() <= 4
                    && !strict.valid
                    && {
                        let fragment = romaji::analyze(piece, true);
                        fragment.valid
                            && ["", "n"].contains(&fragment.partial.as_str())
                            && (end + 1..=n)
                                .find(|next| boundaries[*next])
                                .is_some_and(|next| romaji::analyze(&lower[i..next], true).valid)
                    };
                let partial_japanese = english_piece
                    && (end == n || strict.partial == "n")
                    && !raw.as_bytes()[i].is_ascii_uppercase()
                    && strict.valid
                    && !strict.partial.is_empty()
                    && !self.proper.contains(piece)
                    && !self.readable_english.contains(piece)
                    && !(strict.partial == "n"
                        && end < n
                        && raw.as_bytes()[end] == b'n'
                        && self.common_english.contains(piece)
                        && boundaries[end])
                    && (strict.partial == "n" && (end < n || !paths[i].1)
                        || !final_input && !paths[i].1 && self.japanese_prefix(piece)
                        || i > 0 && !paths[i].1 && !self.common_english.contains(piece));
                // Readable English candidates after Japanese need the same
                // contextual evidence as IsEnglishSpan (kono|tea -> このてあ).
                let ambiguous_japanese = english_piece
                    && i > 0
                    && !paths[i].1
                    && strict.valid
                    && strict.partial.is_empty()
                    && {
                        let next = (end + 1..=n).find(|p| boundaries[*p]).unwrap_or(n);
                        let next = if pending_start == Some(next) { n } else { next };
                        !self.scored_english(
                            &raw[i..end],
                            end == n,
                            final_input,
                            -1,
                            if end == n { 0 } else { -1 },
                            false,
                            false,
                            &raw[end..next],
                            "Balanced",
                        )
                    };
                let en = english_piece
                    && !japanese_continuation
                    && !partial_japanese
                    && !ambiguous_japanese;
                // A listed c-row word is an English span before kana-word
                // matching. Keep that decision when another letter follows;
                // cheap Japanese subspans must not turn camera|a back to kana.
                let separates_n_particle = en
                    && piece.len() >= 4
                    && strict.partial == "n"
                    && self.common_english.contains(piece)
                    && ["ni", "no"]
                        .iter()
                        .any(|particle| lower[end..].starts_with(particle));
                let cost = if separates_n_particle
                    || en
                        && piece.len() >= 4
                        && piece.contains('c')
                        && !piece.contains("ch")
                        && self.common_english.contains(piece)
                        && !self.japanese_prefix(piece)
                {
                    0.1
                } else if jp {
                    0.3
                } else if en {
                    let absorbs_particle = i > 0
                        && [
                            "no", "ga", "wo", "ni", "de", "to", "ha", "mo", "wa", "he", "ya",
                        ]
                        .iter()
                        .any(|p| {
                            piece
                                .strip_prefix(p)
                                .is_some_and(|rest| rest.len() >= 3 && self.english_atom(rest))
                        });
                    if absorbs_particle {
                        1.5
                    } else if !self.english.contains(piece) {
                        1.8
                    } else {
                        1.0
                    }
                } else if end == i + 1 {
                    2.0
                } else {
                    continue;
                };
                if costs[i] + cost < costs[end] {
                    costs[end] = costs[i] + cost;
                    paths[end] = (i, en);
                }
            }
        }
        let mut pieces = Vec::new();
        let mut end = n;
        while end > 0 {
            let (begin, en) = paths[end];
            pieces.push((begin, end, en));
            end = begin;
        }
        pieces.reverse();
        let first_english = pieces.first().is_some_and(|(_, _, english)| *english);
        if !first_english && !self.known_english(&lower) {
            for end in 3..lower.len() {
                let stem = &lower[..end];
                if romaji::analyze(stem, true).valid || end >= 5 && self.known_english(stem) {
                    continue;
                }
                let read = romaji::analyze(stem, false).consumed();
                if read >= 2 {
                    continue;
                }
                let after_invalid = romaji::analyze(&stem[read + 1..], false);
                if after_invalid.valid && after_invalid.partial.is_empty() {
                    continue;
                }
                let rest = &lower[end..];
                let analysis = romaji::analyze(rest, false);
                if [
                    "kara", "made", "yori", "ga", "wo", "ni", "de", "no", "to", "mo", "ha", "wa",
                    "ya",
                ]
                .iter()
                .any(|p| rest.starts_with(p))
                    && analysis.valid
                    && ["", "n"].contains(&analysis.partial.as_str())
                {
                    return format!(
                        "{}{}",
                        &raw[..end],
                        romaji::hiragana(&raw[end..], final_input)
                    );
                }
            }
        }
        // CompositionDetector.Segment keeps an unknown word whole when its
        // first unreadable spelling is within the initial three characters.
        if !first_english && self.unknown_english_word(&lower) {
            let particle_then_word = [
                "kara", "made", "yori", "ga", "wo", "ni", "de", "no", "to", "mo", "ha", "wa", "ya",
            ]
            .iter()
            .find_map(|p| lower.strip_prefix(p))
            .is_some_and(|rest| rest.len() >= 3 && self.known_english(rest));
            if self.known_english(&lower) || !particle_then_word {
                return raw.to_owned();
            }
        }
        // HasListedEnglishPrefixSplit: a short listed head followed only by
        // unrecognised Latin letters is an unknown English word, rather than
        // an English prefix with an accidental Japanese reading.
        if let Some(&(0, head_end, true)) = pieces.first() {
            if head_end <= 3
                && head_end < n
                && (self.common_english.contains(&lower[..head_end])
                    || self.proper.contains(&lower[..head_end]))
                && pieces.iter().skip(1).all(|(_, _, en)| !en)
                && romaji::hiragana(&raw[head_end..], final_input)
                    .chars()
                    .any(|c| c.is_ascii_alphabetic())
                && ![
                    "kara", "made", "yori", "ga", "wo", "ni", "de", "no", "to", "mo", "ha", "wa",
                    "ya",
                ]
                .iter()
                .any(|p| lower[head_end..].starts_with(p))
                && !(head_end + 1..=n).any(|end| self.known_english(&lower[..end]))
            {
                return raw.to_owned();
            }
        }
        // Merge adjacent Japanese spans before reading n / doubled consonants.
        let mut result = String::new();
        let mut japanese = String::new();
        for (begin, end, en) in pieces {
            if en {
                result.push_str(&romaji::hiragana(&japanese, true));
                japanese.clear();
                result.push_str(&raw[begin..end]);
            } else {
                japanese.push_str(&raw[begin..end]);
            }
        }
        result.push_str(&romaji::hiragana(&japanese, final_input));
        result
    }
    fn starts_with_japanese_reading(&self, raw: &str) -> bool {
        let Some(upper) = raw.bytes().position(|c| c.is_ascii_uppercase()) else {
            return false;
        };
        if upper < 3 {
            return false;
        }
        let head = &raw[..upper];
        head.bytes().all(|c| c.is_ascii_lowercase())
            && [
                "kara", "made", "yori", "ga", "wo", "ni", "de", "no", "to", "mo", "ha", "wa", "ya",
            ]
            .iter()
            .any(|p| head.len() > p.len() && head.ends_with(p))
            && {
                let fragment = romaji::analyze(head, true);
                fragment.valid && fragment.partial.is_empty()
            }
            && !self.known_english(head)
    }
    // CapitalizedWordEnd runs before ordinary English span detection. In
    // particular, Shift does not absorb an attached particle (Google|de), or
    // a consonant label before a common Japanese word (T|shatsu).
    fn capitalized_split(&self, raw: &str, final_input: bool) -> Option<usize> {
        let lower_start = raw.len() >= 2
            && raw.as_bytes()[0].is_ascii_lowercase()
            && raw.as_bytes()[1].is_ascii_uppercase();
        if !raw.starts_with(|c: char| c.is_ascii_uppercase()) && !lower_start {
            return None;
        }
        let lower = raw.to_ascii_lowercase();
        if self.common_english.contains(&lower) || self.proper.contains(&lower) {
            return None;
        }
        let pedia = if lower.ends_with("pedias") {
            6
        } else if lower.ends_with("pedia") {
            5
        } else {
            0
        };
        if pedia > 0 && (lower.len() == pedia || lower.len() >= pedia + 3) {
            return None;
        }
        let (bounds, pending) = romaji::unit_boundaries(
            &lower,
            |word| self.known_english(word),
            |word| self.known_english(word),
        );
        for end in (1..raw.len()).rev() {
            if !bounds[end] || pending == Some(end) {
                continue;
            }
            let head = &raw[..end];
            let head_lower = &lower[..end];
            let listed =
                self.common_english.contains(head_lower) || self.proper.contains(head_lower);
            if lower_start && !(end >= 3 && listed) {
                continue;
            }
            let upper = head.bytes().take_while(u8::is_ascii_uppercase).count();
            if upper >= 2
                && upper < head.len()
                && head[upper..].bytes().all(|c| c.is_ascii_lowercase())
                && !listed
            {
                continue;
            }
            let rest = &raw[end..];
            let particle = [
                "kara", "made", "yori", "ga", "wo", "ni", "de", "no", "to", "mo", "ha", "wa", "ya",
            ]
            .iter()
            .find(|p| rest.starts_with(**p));
            if rest.len() < 3 && particle.is_none_or(|p| rest != *p)
                || !rest.bytes().all(|c| c.is_ascii_lowercase())
            {
                continue;
            }
            let acronym = head.len() >= 2
                && head.as_bytes()[head.len() - 1].is_ascii_uppercase()
                && !(head.len() < 4 && head.as_bytes()[head.len() - 2].is_ascii_lowercase());
            let known = acronym
                || end >= 3 && listed
                || end >= 4 && (self.english.contains(head_lower) || self.spell_word(head_lower));
            if known && particle.is_some() {
                return Some(end);
            }
            let fragment = romaji::analyze(rest, true);
            if !fragment.valid || final_input && !["", "n"].contains(&fragment.partial.as_str()) {
                continue;
            }
            if known || end == 1 && particle.is_some() {
                return Some(end);
            }
            if end == 1
                && !b"AIUEON".contains(&raw.as_bytes()[0])
                && !romaji::analyze(&lower, true).valid
            {
                let strict = romaji::analyze(rest, false);
                let reading: String = strict.tokens.iter().map(|t| t.kana.as_str()).collect();
                if strict.valid
                    && ["", "n"].contains(&strict.partial.as_str())
                    && reading.chars().count() >= 3
                    && crate::typo::Corrector::bundled().is_common_japanese(rest)
                {
                    return Some(end);
                }
            }
        }
        None
    }
    fn allows_mixed_case(raw: &str) -> bool {
        !raw.starts_with(|c: char| c.is_ascii_lowercase())
            || !raw.bytes().skip(1).any(|c| c.is_ascii_uppercase())
            || words(crate::system_data::text("propernouns.txt")).any(|name| name == raw)
    }
    pub(crate) fn unknown_english_word(&self, lower: &str) -> bool {
        if lower.len() < 4 || !lower.bytes().all(|b| b.is_ascii_alphabetic()) {
            return false;
        }
        let analysis = romaji::analyze(lower, false);
        if analysis.valid || romaji::analyze(lower, true).valid {
            return false;
        }
        let invalid = analysis.consumed();
        if invalid >= 3 {
            return false;
        }
        let rest = &lower[invalid + 1..];
        if rest.len() >= 3 && romaji::analyze(rest, true).valid {
            return false;
        }
        !crate::detection::japanese_typo(lower)
    }
    fn english_atom(&self, word: &str) -> bool {
        if self.english.contains(word) {
            return true;
        }
        if word.len() >= 3
            && word
                .bytes()
                .all(|b| b.is_ascii_lowercase() && !b"aiueo".contains(&b))
            && !romaji::analyze(word, true).valid
        {
            return true;
        }
        if !word.is_ascii() {
            return false;
        }
        let particles = [
            "no", "ga", "wo", "ni", "de", "to", "ha", "mo", "wa", "he", "ya",
        ];
        (2..word.len().saturating_sub(1)).any(|i| {
            self.english.contains(&word[..i])
                && self.english.contains(&word[i..])
                && (i >= 3
                    || word[..i].bytes().all(|b| b"aiueo".contains(&b))
                    || word[..i].bytes().all(|b| !b"aiueo".contains(&b)))
                && (word.len() - i >= 3
                    || word[i..].bytes().all(|b| b"aiueo".contains(&b))
                    || word[i..].bytes().all(|b| !b"aiueo".contains(&b)))
                && !particles.contains(&&word[..i])
                && !particles.contains(&&word[i..])
                && (!self.is_japanese(&word[..i])
                    || i == 2 && self.common_english.contains(&word[..i]))
                && (!self.is_japanese(&word[i..])
                    || word.len() - i == 2 && self.common_english.contains(&word[i..]))
                && ![
                    "no", "ga", "wo", "ni", "de", "to", "ha", "mo", "wa", "he", "ya",
                ]
                .iter()
                .any(|p| {
                    word[i..]
                        .strip_prefix(p)
                        .is_some_and(|tail| tail.len() >= 3 && self.english.contains(tail))
                })
        })
    }
    pub fn spell_word(&self, raw: &str) -> bool {
        crate::spelling::is_word(raw)
    }
    pub fn known_english(&self, raw: &str) -> bool {
        self.is_english(raw) || self.spell_word(raw)
    }
    /// english-readable.txt の「ローマ字としても読めるが英語で打つことの多い語」。
    pub fn is_readable_english(&self, raw: &str) -> bool {
        self.readable_english.contains(&raw.to_ascii_lowercase())
    }
    pub fn is_english(&self, raw: &str) -> bool {
        self.english.contains(&raw.to_ascii_lowercase())
    }
    pub fn is_japanese_origin(&self, raw: &str) -> bool {
        self.japanese_origin.contains(&raw.to_ascii_lowercase())
    }
    pub fn is_common_english(&self, raw: &str) -> bool {
        self.common_english.contains(&raw.to_ascii_lowercase())
    }
    pub(crate) fn splits_known_kana_word(&self, text: &str, registered_bytes: usize) -> bool {
        // #487: 登録名「ちな」を既知語「ちなみに」の途中へ強制挿入しない。
        // 辞書の最長語を上限にし、長い入力全体の走査は避ける。
        text.char_indices()
            .map(|(at, ch)| at + ch.len_utf8())
            .take_while(|end| *end <= self.max_kana_word_bytes)
            .any(|end| end > registered_bytes && self.kana_words.contains(&text[..end]))
    }
    pub fn is_japanese(&self, raw: &str) -> bool {
        self.japanese.contains(&raw.to_ascii_lowercase())
            || self.kana_words.contains(&romaji::hiragana(raw, true))
    }
    pub fn english_prefix(&self, raw: &str) -> bool {
        let raw = raw.to_ascii_lowercase();
        self.english_index.contains(&raw)
    }
    pub fn japanese_prefix(&self, raw: &str) -> bool {
        let raw = raw.to_ascii_lowercase();
        self.japanese_index.contains(&raw)
    }
    pub fn predict_english(&self, raw: &str) -> Vec<String> {
        if raw.len() < 3 || !raw.bytes().all(|c| c.is_ascii_alphabetic()) {
            return vec![];
        }
        let lower = raw.to_ascii_lowercase();
        use fst::{IntoStreamer, Streamer};
        // Enumerate only the prefix range and retain the five best words.
        // Rank exactly as before: common words, length, then lexical order.
        let mut stream = self.english_index.0.range().gt(&lower).into_stream();
        let mut words: Vec<(bool, usize, String)> = Vec::with_capacity(5);
        while let Some(key) = stream.next() {
            if !key.starts_with(lower.as_bytes()) {
                break;
            }
            let word = std::str::from_utf8(key).expect("dictionary key is UTF-8");
            let rank = (!self.common_english.contains(word), word.len(), word);
            let at = words
                .partition_point(|(uncommon, len, text)| (*uncommon, *len, text.as_str()) < rank);
            if at < 5 {
                if words.len() == 5 {
                    words.pop();
                }
                words.insert(at, (rank.0, rank.1, word.to_owned()));
            }
        }
        let uppercase = raw.bytes().all(|c| c.is_ascii_uppercase());
        words
            .into_iter()
            .map(|(_, _, mut word)| {
                if uppercase {
                    word.make_ascii_uppercase();
                } else if raw.as_bytes()[0].is_ascii_uppercase() {
                    word[..1].make_ascii_uppercase();
                }
                word
            })
            .collect()
    }
}

#[cfg(test)]
mod prefix_tests {
    use super::*;
    #[test]
    fn v140_scored_pending_capitalized_word() {
        let d = Dictionary::bundled();
        assert_eq!(
            d.composition_scored("Goog", false, "", "", "Balanced"),
            "Goおg"
        );
        assert_eq!(
            d.composition_scored("Goog", true, "", "", "Balanced"),
            "Goog"
        );
    }
    #[test]
    fn cloned_dictionary_shares_system_data_and_detaches_only_modified_records() {
        let bundled = Dictionary::bundled();
        let mut profile = bundled.clone();
        assert!(Arc::ptr_eq(&bundled.english, &profile.english));
        assert!(Arc::ptr_eq(&bundled.candidates, &profile.candidates));
        assert!(Arc::ptr_eq(&bundled.emoji, &profile.emoji));
        assert!(Arc::ptr_eq(&bundled.kana_words, &profile.kana_words));
        let reading = "__profile_only__";
        profile.add(reading, std::iter::once("個別候補"));
        assert!(!bundled.candidates.contains_key(reading));
        assert_eq!(profile.candidates[reading], ["個別候補"]);
        assert!(!Arc::ptr_eq(&bundled.candidates, &profile.candidates));
        assert!(Arc::ptr_eq(&bundled.english, &profile.english));
        assert!(Arc::ptr_eq(&bundled.emoji, &profile.emoji));
    }
    #[test]
    fn candidate_storage_preserves_order_without_four_slot_minimum() {
        let mut dictionary = Dictionary::bundled().clone();
        let reading = "__compact_candidate_test__";
        dictionary.add(reading, std::iter::once("候補一"));
        assert_eq!(dictionary.candidates[reading], ["候補一"]);
        assert_eq!(dictionary.candidates[reading].capacity(), 1);
        dictionary.add(reading, ["候補一", "候補二", "候補二"].into_iter());
        assert_eq!(dictionary.candidates[reading], ["候補一", "候補二"]);
    }
    #[test]
    #[ignore = "manual performance comparison; run with --release --ignored --nocapture"]
    fn prefix_lookup_timing() {
        use fst::{IntoStreamer, Streamer};
        use std::{hint::black_box, time::Instant};
        let index = PrefixIndex::bundled(include_bytes!(concat!(env!("OUT_DIR"), "/english.fst")));
        let mut stream = index.0.stream();
        let mut probes = Vec::new();
        while let Some(word) = stream.next() {
            let word = std::str::from_utf8(word).unwrap();
            if word.is_ascii() && word.len() >= 3 {
                probes.push(word[..3].to_owned());
                probes.push(format!("{}!", &word[..3]));
            }
            if probes.len() >= 20000 {
                break;
            }
        }
        let mut rows = Vec::new();
        for round in 0..4 {
            for direct in if round % 2 == 0 {
                [false, true]
            } else {
                [true, false]
            } {
                let start = Instant::now();
                let mut hits = 0usize;
                for _ in 0..10 {
                    for probe in &probes {
                        let found = if direct {
                            index.contains(black_box(probe))
                        } else {
                            index
                                .0
                                .range()
                                .ge(black_box(probe))
                                .into_stream()
                                .next()
                                .is_some_and(|key| key.starts_with(probe.as_bytes()))
                        };
                        hits += usize::from(found);
                    }
                }
                rows.push(serde_json::json!({"round":round,"method":if direct {"node"} else {"range"},"queries":probes.len()*10,"hits":hits,"ms":start.elapsed().as_secs_f64()*1000.0}));
            }
        }
        assert!(rows.windows(2).all(|r| r[0]["hits"] == r[1]["hits"]));
        println!("PREFIX_BENCH={}", serde_json::json!(rows));
    }
    #[test]
    fn predictions_match_full_dictionary_ranking() {
        let reference = |dictionary: &Dictionary, raw: &str| {
            if raw.len() < 3 || !raw.bytes().all(|c| c.is_ascii_alphabetic()) {
                return Vec::new();
            }
            let lower = raw.to_ascii_lowercase();
            let mut words: Vec<_> = dictionary
                .english
                .iter()
                .filter(|s| s.starts_with(&lower) && s.len() > lower.len())
                .collect();
            words.sort_by_key(|s| (!dictionary.common_english.contains(*s), s.len(), *s));
            words
                .into_iter()
                .take(5)
                .map(|s| {
                    let mut value = s.clone();
                    if raw.bytes().all(|c| c.is_ascii_uppercase()) {
                        value.make_ascii_uppercase();
                    } else if raw.as_bytes()[0].is_ascii_uppercase() {
                        value[..1].make_ascii_uppercase();
                    }
                    value
                })
                .collect::<Vec<_>>()
        };
        let bundled = Dictionary::bundled();
        let mut custom = bundled.clone();
        for word in ["hel", "helzz", "helcustom", "helcommon", "zzzxcustom"] {
            Arc::make_mut(&mut custom.english).insert(word.into());
        }
        Arc::make_mut(&mut custom.common_english).insert("helcommon".into());
        custom.english_index = PrefixIndex::new(&custom.english);
        for dictionary in [bundled, &custom] {
            for raw in [
                "",
                "he",
                "hel",
                "Hel",
                "HEL",
                "hEl",
                "hello",
                "com",
                "con",
                "pro",
                "rea",
                "mel",
                "google",
                "zzzx",
                "qqqzzz",
                "日本語",
                "hel1",
            ] {
                assert_eq!(
                    dictionary.predict_english(raw),
                    reference(dictionary, raw),
                    "{raw}"
                );
            }
        }
    }
    #[test]
    fn node_prefix_matches_range_lookup() {
        use fst::{IntoStreamer, Streamer};
        for words in [vec![], vec!["", "a", "abc", "abd", "日本語", "😊"]] {
            let index = PrefixIndex::new(&words.iter().map(|w| w.to_string()).collect());
            let mut probes = vec![
                "".to_string(),
                "missing".into(),
                "日本".into(),
                "😊x".into(),
            ];
            for word in words {
                for end in word.char_indices().map(|(i, c)| i + c.len_utf8()) {
                    probes.push(word[..end].into());
                    probes.push(format!("{}?", &word[..end]));
                }
            }
            for probe in probes {
                let expected = index
                    .0
                    .range()
                    .ge(&probe)
                    .into_stream()
                    .next()
                    .is_some_and(|key| key.starts_with(probe.as_bytes()));
                assert_eq!(index.contains(&probe), expected, "{probe}");
            }
        }
    }
}
