// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
use std::{collections::HashMap, sync::LazyLock};

static NAMES: LazyLock<HashMap<&str, (&str, bool)>> = LazyLock::new(|| {
    include_str!(concat!(env!("OUT_DIR"), "/radical-names.txt"))
        .lines()
        .map(|line| {
            let (name, entry) = line.split_once('\t').unwrap();
            let (part, first) = entry.split_once('\t').unwrap();
            (name, (part, first == "true"))
        })
        .collect()
});

pub(super) fn find(reading: &str) -> Vec<String> {
    let mut rest = reading;
    let mut parts = Vec::new();
    loop {
        // A name can itself contain に, as in にんべん.
        let next = rest.match_indices('に').find_map(|(index, separator)| {
            let (part, first) = NAMES.get(&rest[..index])?;
            (!parts.is_empty() || *first).then_some((*part, &rest[index + separator.len()..]))
        });
        if let Some((part, remaining)) = next {
            parts.push(part);
            rest = remaining;
        } else if !parts.is_empty() {
            let Some(&(last, _)) = NAMES.get(rest) else {
                return Vec::new();
            };
            parts.push(last);
            break;
        } else {
            return Vec::new();
        }
    }
    parts.sort_unstable();
    parts.dedup();
    let mut matches = Vec::new();
    for line in crate::system_data::text("kanji-parts.txt")
        .lines()
        .filter(|s| !s.starts_with('#'))
    {
        let Some((kanji, elements)) = line.split_once('\t') else {
            continue;
        };
        if parts
            .iter()
            .all(|part| elements.split_whitespace().any(|element| element == *part))
        {
            matches.push((elements.split_whitespace().count() - parts.len(), kanji));
        }
    }
    // Stable sort keeps dictionary frequency order for tied matches.
    matches.sort_by_key(|&(extra, _)| extra);
    matches
        .into_iter()
        .take(30)
        .map(|(_, kanji)| kanji.to_owned())
        .collect()
}

pub(super) fn prepend(mut found: Vec<String>, candidates: &mut Vec<String>) {
    if !found.is_empty() {
        candidates.retain(|word| !found.contains(word));
        found.append(candidates);
        *candidates = found;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn radical_names_and_ordinary_phrases() {
        for (reading, expected) in [
            ("ごんべんにふるとり", "誰"),
            ("にんべんにき", "休"),
            ("さんずいにき", "沐"),
        ] {
            assert_eq!(find(reading).first().map(String::as_str), Some(expected));
        }
        for reading in [
            "ごんべん",
            "きにかける",
            "ひとくち",
            "ごんべんに",
            "こころにきめる",
        ] {
            assert!(find(reading).is_empty(), "{reading}");
        }
    }

    #[test]
    fn split_engine_result_remains_available_after_matching_kanji() {
        use crate::session::Clause;
        let mut session = crate::Session::default();
        for c in "gonbennnifurutori".chars() {
            session.key(0, c as i32, 0);
        }
        assert!(session.begin_clauses(vec![
            Clause::new("ごんべんに".into(), vec!["言偏に".into()]),
            Clause::new("ふるとり".into(), vec!["ふるとり".into()]),
        ]));
        let view = session.result(false).view.unwrap();
        assert_eq!(view.text, "誰");
        let position = |word| view.candidates.iter().position(|s| s == word).unwrap();
        assert!(position("言偏にふるとり") > position("讎"));
    }

    #[test]
    fn space_converts_the_entire_radical_description() {
        let mut session = crate::Session::default();
        for c in "gonbennnifurutori".chars() {
            session.key(0, c as i32, 0);
        }
        let view = session.key(32, 32, 0).view.unwrap();
        assert_eq!(view.text, "誰");
        assert!(view.candidates.iter().any(|s| s == "讎"));
        assert_eq!(session.commit().commits[0].text, "誰");
    }
}
