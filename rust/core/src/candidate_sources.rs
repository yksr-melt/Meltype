// SPDX-License-Identifier: GPL-3.0-or-later
//! Ordered auxiliary candidates and context rules, owned by a profile.
use std::{
    collections::{BTreeMap, BTreeSet},
    sync::{Arc, OnceLock},
};
#[cfg(feature = "filesystem")]
use std::{io, path::Path};
#[derive(Clone, Default)]
pub(crate) struct Sources {
    words: BTreeMap<String, Vec<String>>,
    emoji: BTreeMap<String, Vec<String>>,
    contexts: BTreeMap<String, Vec<(String, Vec<String>)>>,
    imported: BTreeMap<String, Vec<String>>,
    phrases: BTreeMap<String, Vec<String>>,
    head_only: BTreeSet<String>,
    phrase_parts: BTreeMap<(String, String), Vec<(String, String)>>,
}
fn add(
    map: &mut BTreeMap<String, Vec<String>>,
    reading: &str,
    values: impl Iterator<Item = String>,
) {
    let words = map.entry(reading.into()).or_default();
    for word in values {
        if !word.is_empty() && !words.contains(&word) {
            words.push(word);
        }
    }
}
fn lookup(map: &BTreeMap<String, Vec<String>>, reading: &str) -> Vec<String> {
    for end in reading.char_indices().map(|(i, c)| i + c.len_utf8()).rev() {
        if reading[..end].encode_utf16().count() == 1 && end < reading.len() {
            break;
        }
        let suffix = &reading[end..];
        if ![
            "", "を", "が", "は", "に", "で", "と", "も", "へ", "の", "や", "な", "だ", "です",
            "から", "まで", "より", "って", "とか", "さ", "ね", "よ",
        ]
        .contains(&suffix)
        {
            continue;
        }
        if let Some(words) = map.get(&reading[..end]) {
            return words.iter().map(|w| format!("{w}{suffix}")).collect();
        }
    }
    vec![]
}
impl Sources {
    pub fn bundled() -> Arc<Self> {
        static SOURCES: OnceLock<Arc<Sources>> = OnceLock::new();
        SOURCES
            .get_or_init(|| {
                let mut source = Self::default();
                for text in [
                    crate::system_data::text("candidates.txt"),
                    crate::system_data::text("loanwords.txt"),
                    crate::system_data::text("brands.txt"),
                ] {
                    source.add_words(text);
                }
                for text in [
                    crate::system_data::text("emoji.txt"),
                    crate::system_data::text("emoji-cldr.txt"),
                ] {
                    source.add_emoji(text);
                }
                source.add_contexts(crate::system_data::text("contexts.txt"));
                for line in crate::system_data::text("phrases.txt")
                    .lines()
                    .filter(|line| !line.starts_with('#'))
                {
                    let mut parts = line.split('\t');
                    if let (Some(reading), Some(word)) = (parts.next(), parts.next()) {
                        let head_only = reading.starts_with('^');
                        let reading = reading.trim_start_matches('^');
                        let readings: Vec<_> = reading.split('|').collect();
                        let words: Vec<_> = word.split('|').collect();
                        if readings.len() != words.len()
                            || readings.iter().any(|s| s.is_empty())
                            || words.iter().any(|s| s.is_empty())
                        {
                            continue;
                        }
                        let reading = readings.concat();
                        let word = words.concat();
                        if head_only {
                            source.head_only.insert(reading.clone());
                        }
                        if readings.len() > 1 {
                            source.phrase_parts.insert(
                                (reading.clone(), word.clone()),
                                readings
                                    .into_iter()
                                    .zip(words)
                                    .map(|(r, w)| (r.to_owned(), w.to_owned()))
                                    .collect(),
                            );
                        }
                        add(&mut source.phrases, &reading, std::iter::once(word));
                    }
                }
                Arc::new(source)
            })
            .clone()
    }
    #[cfg(feature = "filesystem")]
    pub fn load(directory: &Path) -> io::Result<Arc<Self>> {
        let files = ["candidates.txt", "emoji.txt", "contexts.txt"];
        let mut plists: Vec<_> = std::fs::read_dir(directory)
            .into_iter()
            .flatten()
            .filter_map(Result::ok)
            .map(|entry| entry.path())
            .filter(|path| path.extension().is_some_and(|ext| ext == "plist"))
            .collect();
        plists.sort();
        if plists.is_empty() && !files.iter().any(|name| directory.join(name).exists()) {
            return Ok(Self::bundled());
        }
        let mut source = (*Self::bundled()).clone();
        for name in files {
            if let Ok(bytes) = std::fs::read(directory.join(name)) {
                let Ok((text, _)) = crate::user_dictionary::decode(&bytes) else {
                    continue;
                };
                match name {
                    "candidates.txt" => source.add_words(&text),
                    "emoji.txt" => source.add_emoji(&text),
                    _ => source.add_contexts(&text),
                }
            }
        }
        for path in plists {
            if let Some(import) = std::fs::read(path)
                .ok()
                .and_then(|bytes| crate::user_dictionary::parse(&bytes).ok())
            {
                for (reading, word) in import.words {
                    add(&mut source.imported, &reading, std::iter::once(word));
                }
            }
        }
        Ok(Arc::new(source))
    }
    pub fn english_names(reading: &str) -> Vec<String> {
        if reading.chars().count() < 3 {
            return vec![];
        }
        type Name = (&'static str, &'static str, bool, usize);
        static NAMES: OnceLock<Vec<Name>> = OnceLock::new();
        let names = NAMES.get_or_init(|| {
            let mut names = Vec::new();
            for (file, brand) in [("brands.txt", true), ("loanwords.txt", false)] {
                for line in crate::system_data::text(file).lines() {
                    let mut parts = line.split('#').next().unwrap_or("").split_whitespace();
                    if let Some(kana) = parts.next() {
                        for word in parts {
                            if word.bytes().any(|c| c.is_ascii_alphabetic()) {
                                names.push((kana, word, brand, names.len()));
                            }
                        }
                    }
                }
            }
            names.sort_by_key(|n| (n.0, n.3));
            names
        });
        let start = names.partition_point(|n| n.0 < reading);
        let mut matches: Vec<_> = names[start..]
            .iter()
            .take_while(|n| n.0.starts_with(reading))
            .collect();
        matches.sort_by_key(|n| (!n.2, n.0.chars().count(), n.3));
        let mut result = Vec::new();
        for n in matches {
            if !result.iter().any(|v| v == n.1) {
                result.push(n.1.to_owned());
                if result.len() == 5 {
                    break;
                }
            }
        }
        result
    }
    pub fn abbreviations(&self, reading: &str) -> Vec<String> {
        lookup(&self.words, reading)
    }
    pub fn registered(&self, reading: &str) -> Vec<String> {
        self.imported
            .get(reading)
            .into_iter()
            .flatten()
            .chain(self.phrases.get(reading).into_iter().flatten())
            .cloned()
            .collect()
    }
    pub fn head_only(&self, reading: &str) -> bool {
        self.head_only.contains(reading)
    }
    pub fn phrase_parts(&self, reading: &str, word: &str) -> Option<&[(String, String)]> {
        self.phrase_parts
            .get(&(reading.to_owned(), word.to_owned()))
            .map(Vec::as_slice)
    }
    pub fn phrase_groups(&self) -> impl Iterator<Item = &[(String, String)]> {
        self.phrase_parts.values().map(Vec::as_slice)
    }
    pub fn registrations(&self) -> BTreeMap<String, (String, bool)> {
        let mut words: BTreeMap<_, _> = self
            .phrases
            .iter()
            .filter_map(|(reading, words)| {
                words
                    .first()
                    .map(|word| (reading.clone(), (word.clone(), false)))
            })
            .collect();
        for (reading, entries) in &self.imported {
            if let Some(word) = entries.first() {
                words.insert(reading.clone(), (word.clone(), true));
            }
        }
        words
    }
    fn add_words(&mut self, text: &str) {
        for line in text.lines() {
            let words: Vec<_> = line
                .split('#')
                .next()
                .unwrap_or("")
                .split([' ', '\t', '\r', '　'])
                .filter(|w| !w.is_empty())
                .collect();
            if words.len() > 1 {
                add(
                    &mut self.words,
                    words[0],
                    words[1..].iter().map(|w| w.to_string()),
                );
            }
        }
    }
    fn add_emoji(&mut self, text: &str) {
        for line in text
            .lines()
            .filter(|line| !line.trim_start().starts_with('#'))
        {
            let words: Vec<_> = line
                .split('\t')
                .map(str::trim)
                .filter(|w| !w.is_empty())
                .collect();
            if words.len() > 1 {
                add(
                    &mut self.emoji,
                    words[0],
                    words[1..].iter().map(|w| w.to_string()),
                );
            }
        }
    }
    fn add_contexts(&mut self, text: &str) {
        for line in text.lines() {
            if let Some((head, cues)) = line.split('#').next().unwrap_or("").split_once(':') {
                let head: Vec<_> = head.split_whitespace().collect();
                let cues: Vec<_> = cues.split_whitespace().map(str::to_owned).collect();
                if head.len() == 2 && !cues.is_empty() {
                    self.contexts
                        .entry(head[0].into())
                        .or_default()
                        .push((head[1].into(), cues));
                }
            }
        }
    }
    pub fn context(&self, reading: &str, surrounding: &str) -> Option<String> {
        for end in reading.char_indices().map(|(i, c)| i + c.len_utf8()).rev() {
            if let Some(rules) = self.contexts.get(&reading[..end]) {
                let mut best = None;
                let mut score = 0;
                for (candidate, cues) in rules {
                    let hits = cues
                        .iter()
                        .filter(|cue| surrounding.contains(cue.as_str()))
                        .count();
                    if hits > score {
                        score = hits;
                        best = Some(format!("{candidate}{}", &reading[end..]));
                    }
                }
                return best;
            }
        }
        None
    }
    pub fn candidates(
        &self,
        reading: &str,
        timezone: &str,
        currency: &crate::currency::Currency,
    ) -> Vec<String> {
        let mut words = lookup(&self.words, reading);
        let katakana = crate::romaji::katakana(reading);
        words.extend([
            reading.into(),
            katakana.clone(),
            crate::kana::halfwidth(&katakana),
        ]);
        words.extend(crate::time_candidates::candidates(
            reading,
            chrono::Local::now().naive_local(),
            timezone,
        ));
        words.extend(currency.candidates(reading));
        words.extend(self.emoji(reading));
        words
    }
    pub fn emoji(&self, reading: &str) -> Vec<String> {
        lookup(&self.emoji, reading).into_iter().rev().collect()
    }
}
