// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
use crate::{session::Clause, Session};

pub struct KanjiDictionary {
    bytes: std::borrow::Cow<'static, [u8]>,
    dimension: usize,
    count: usize,
    records: usize,
    pool: usize,
    max_key: usize,
}

impl KanjiDictionary {
    fn u32(bytes: &[u8], offset: usize) -> usize {
        u32::from_le_bytes(bytes[offset..offset + 4].try_into().unwrap()) as usize
    }
    fn u16(&self, offset: usize) -> usize {
        u16::from_le_bytes(self.bytes[offset..offset + 2].try_into().unwrap()) as usize
    }
    fn text(&self, record: usize, field: usize) -> &str {
        let at = self.records + record * 24 + field;
        let start = self.pool + Self::u32(&self.bytes, at);
        let len = Self::u32(&self.bytes, at + 4);
        std::str::from_utf8(&self.bytes[start..start + len]).unwrap()
    }
    pub fn parse(bytes: Vec<u8>) -> Result<Self, String> {
        Self::from_bytes(bytes.into(), true)
    }

    /// The caller must first verify the bytes against the bundled dictionary digest.
    pub fn from_verified_package(bytes: Vec<u8>) -> Result<Self, String> {
        Self::from_bytes(bytes.into(), false)
    }

    fn from_bytes(
        bytes: std::borrow::Cow<'static, [u8]>,
        verify_records: bool,
    ) -> Result<Self, String> {
        let invalid =
            || "ChromeOS漢字辞書の形式が不正です。拡張機能を全体更新してください。".to_string();
        if bytes.len() < 20 || bytes.len() > 256 * 1024 * 1024 || &bytes[..8] != b"MTKKC001" {
            return Err(invalid());
        }
        let dimension = Self::u32(&bytes, 8);
        let count = Self::u32(&bytes, 12);
        let max_key = Self::u32(&bytes, 16);
        if dimension == 0
            || dimension > 8192
            || count == 0
            || count > 5_000_000
            || max_key == 0
            || max_key > 4096
        {
            return Err(invalid());
        }
        let records = 20 + dimension * dimension * 2;
        let pool = records + count * 24;
        if pool > bytes.len() {
            return Err(invalid());
        }
        let dictionary = Self {
            bytes,
            dimension,
            count,
            records,
            pool,
            max_key,
        };
        if !verify_records {
            return Ok(dictionary);
        }
        let bytes = dictionary.bytes.as_ref();
        for index in 0..count {
            let at = records + index * 24;
            for field in [0, 8] {
                let start = Self::u32(bytes, at + field);
                let len = Self::u32(bytes, at + field + 4);
                if len == 0
                    || start > bytes.len() - pool
                    || len > bytes.len() - pool - start
                    || std::str::from_utf8(&bytes[pool + start..pool + start + len]).is_err()
                    || field == 0 && len > max_key
                {
                    return Err(invalid());
                }
            }
            if dictionary.u16(at + 16) >= dimension
                || dictionary.u16(at + 18) >= dimension
                || index > 0 && dictionary.text(index - 1, 0) > dictionary.text(index, 0)
            {
                return Err(invalid());
            }
        }
        Ok(dictionary)
    }
    fn connection(&self, right: usize, left: usize) -> i64 {
        self.u16(20 + (right * self.dimension + left) * 2) as i64
    }
    fn matching(&self, reading: &str) -> std::ops::Range<usize> {
        let bound = |upper: bool| {
            let (mut lo, mut hi) = (0, self.count);
            while lo < hi {
                let mid = lo + (hi - lo) / 2;
                let key = self.text(mid, 0);
                if key < reading || upper && key == reading {
                    lo = mid + 1;
                } else {
                    hi = mid;
                }
            }
            lo
        };
        bound(false)..bound(true)
    }
    fn convert(&self, reading: &str) -> Vec<String> {
        // Spaceで明示的に変換したときだけ、上限付きのビーム探索を行う。
        // 長すぎる入力は従来のCore候補を保つ。
        let boundaries: Vec<_> = reading
            .char_indices()
            .map(|(i, _)| i)
            .chain(std::iter::once(reading.len()))
            .collect();
        if boundaries.len() > 257 || reading.is_empty() {
            return vec![];
        }
        #[derive(Clone)]
        struct Path {
            cost: i64,
            right: usize,
            text: String,
        }
        fn insert(paths: &mut Vec<Path>, path: Path) {
            if let Some(index) = paths
                .iter()
                .position(|p| p.right == path.right && p.text == path.text)
            {
                if paths[index].cost <= path.cost {
                    return;
                }
                paths.remove(index);
            }
            let at = paths.partition_point(|p| p.cost <= path.cost);
            if at < 32 {
                paths.insert(at, path);
                paths.truncate(32);
            }
        }
        let mut paths = vec![Vec::new(); boundaries.len()];
        paths[0].push(Path {
            cost: 0,
            right: 0,
            text: String::new(),
        });
        for start in 0..boundaries.len() - 1 {
            let previous = std::mem::take(&mut paths[start]);
            for end in start + 1..boundaries.len() {
                let key = &reading[boundaries[start]..boundaries[end]];
                if key.len() > self.max_key {
                    break;
                }
                for index in self.matching(key) {
                    let at = self.records + index * 24;
                    let left = self.u16(at + 16);
                    let right = self.u16(at + 18);
                    let cost =
                        i32::from_le_bytes(self.bytes[at + 20..at + 24].try_into().unwrap()) as i64;
                    for path in &previous {
                        insert(
                            &mut paths[end],
                            Path {
                                cost: path.cost + cost + self.connection(path.right, left),
                                right,
                                text: format!("{}{}", path.text, self.text(index, 8)),
                            },
                        );
                    }
                }
            }
            // 未登録の文字・英数字・絵文字を落とさず、既知語の前後へ残す。
            for path in previous {
                insert(
                    &mut paths[start + 1],
                    Path {
                        cost: path.cost + 12000,
                        right: 0,
                        text: format!(
                            "{}{}",
                            path.text,
                            &reading[boundaries[start]..boundaries[start + 1]]
                        ),
                    },
                );
            }
        }
        let mut final_paths = paths.pop().unwrap();
        final_paths.sort_by_key(|p| p.cost + self.connection(p.right, 0));
        let mut candidates = Vec::new();
        for path in final_paths {
            if !candidates.contains(&path.text) {
                candidates.push(path.text);
            }
            if candidates.len() == 16 {
                break;
            }
        }
        candidates
    }
}

impl KanjiDictionary {
    /// Preserve English/protected runs and apply shared phrase registrations
    /// before the browser's local converter, as native converter hosts do.
    pub fn clauses(&self, session: &Session, reading: &str) -> Vec<Clause> {
        let mut clauses = Vec::new();
        let protected = crate::protection::scan(reading);
        let mut cursor = 0;
        while cursor < reading.len() {
            if let Some(span) = protected.iter().find(|span| span.start == cursor) {
                clauses.push(Clause::literal(reading[cursor..span.end].into(), false));
                cursor = span.end;
                continue;
            }
            let english = reading[cursor..]
                .chars()
                .next()
                .unwrap()
                .is_ascii_alphabetic();
            let end = reading[cursor..]
                .char_indices()
                .skip(1)
                .find(|(offset, ch)| {
                    ch.is_ascii_alphabetic() != english
                        || protected.iter().any(|span| span.start == cursor + offset)
                })
                .map_or(reading.len(), |(offset, _)| cursor + offset);
            let run = &reading[cursor..end];
            if english {
                clauses.push(Clause::literal(run.into(), true));
            } else {
                let plan = session
                    .registered_conversion_plan(run, &[])
                    .unwrap_or_else(|| vec![(run.to_owned(), None)]);
                for (part, registered) in plan {
                    let mut candidates = self.convert(&part);
                    if let Some(word) = registered {
                        candidates.insert(0, word);
                    }
                    clauses.push(Clause::new(part, candidates));
                }
            }
            cursor = end;
        }
        clauses
    }
}

#[cfg(any(target_os = "android", target_os = "ios", feature = "bundled-mozc"))]
pub(crate) fn bundled() -> &'static KanjiDictionary {
    static DICTIONARY: std::sync::OnceLock<KanjiDictionary> = std::sync::OnceLock::new();
    DICTIONARY.get_or_init(|| {
        // Built from the pinned Mozc data. Let the OS page in only the records used.
        KanjiDictionary::from_bytes(
            (&include_bytes!(concat!(env!("OUT_DIR"), "/kanji.bin"))[..]).into(),
            false,
        )
        .expect("bundled Mozc dictionary")
    })
}
