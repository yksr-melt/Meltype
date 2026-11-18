// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! TXT is the reviewed source; shipped system records and prefix indexes are FSTs.
use std::{collections::BTreeSet, env, fs, path::PathBuf};
fn words(text: &str) -> impl Iterator<Item = &str> {
    text.lines()
        .flat_map(|line| line.split('#').next().unwrap_or("").split_whitespace())
}
fn main() {
    let directory =
        PathBuf::from(env::var_os("CARGO_MANIFEST_DIR").unwrap()).join("../../dictionaries");
    let output = PathBuf::from(env::var_os("OUT_DIR").unwrap());
    println!("cargo:rerun-if-changed={}", directory.display());
    let mut texts = std::collections::BTreeMap::new();
    for entry in fs::read_dir(&directory).unwrap() {
        let path = entry.unwrap().path();
        if path.extension().is_none_or(|extension| extension != "txt") {
            continue;
        }
        let name = path.file_name().unwrap().to_str().unwrap().to_owned();
        let text = fs::read_to_string(&path).unwrap();
        texts.insert(name, text);
    }
    // FST addresses refer to one immutable payload. Gloss text is not encoded
    // as automaton keys or copied into a heap cache during initialization.
    let domain_names = texts
        .keys()
        .filter(|name| name.starts_with("domain-"))
        .map(String::as_str)
        .collect::<Vec<_>>()
        .join("\n");
    fs::write(output.join("domain-names.txt"), domain_names).unwrap();
    let mut records = std::collections::BTreeMap::new();
    let mut payload = Vec::new();
    for (name, text) in &texts {
        let start = payload.len();
        assert!(
            start + text.len() <= u32::MAX as usize,
            "system payload exceeds FST address space"
        );
        records.insert(name.clone(), ((start as u64) << 32) | text.len() as u64);
        let mut offset = start;
        for (index, line) in text.split_inclusive('\n').enumerate() {
            records.insert(
                format!("{name}\0{index:08x}"),
                ((offset as u64) << 32) | line.len() as u64,
            );
            offset += line.len();
        }
        payload.extend_from_slice(text.as_bytes());
    }
    let archive = fst::Map::from_iter(records).unwrap();
    fs::write(
        output.join("system-records.fst"),
        archive.as_fst().as_bytes(),
    )
    .unwrap();
    fs::write(output.join("system-values.bin"), payload).unwrap();
    let readings: BTreeSet<_> = ["readings.txt", "readings-extra.txt"]
        .iter()
        .flat_map(|name| texts[*name].lines())
        .filter(|line| !line.starts_with('#'))
        .filter_map(|line| line.split_whitespace().next())
        .collect();
    let kana = fst::Set::from_iter(readings).unwrap();
    fs::write(output.join("kana.fst"), kana.as_fst().as_bytes()).unwrap();
    for name in ["english", "japanese", "common", "proper"] {
        let mut entries = BTreeSet::new();
        let files: &[&str] = match name {
            "english" => &[
                "english.txt",
                "english-words.txt",
                "propernouns.txt",
                "brands.txt",
                "english-readable.txt",
            ],
            "japanese" => &["japanese.txt"],
            "common" => &["english.txt"],
            _ => &["propernouns.txt", "brands.txt"],
        };
        for file in files {
            for word in words(&texts[*file]) {
                if name == "proper" && !word.is_ascii() {
                    continue;
                }
                entries.insert(if matches!(name, "english" | "proper") {
                    word.to_ascii_lowercase()
                } else {
                    word.to_owned()
                });
            }
        }
        let index = fst::Set::from_iter(entries).unwrap();
        fs::write(
            output.join(format!("{name}.fst")),
            index.as_fst().as_bytes(),
        )
        .unwrap();
    }
}
