// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! Build-generated FST records. No writable system dictionary is needed at runtime.
use std::sync::OnceLock;
static ARCHIVE: OnceLock<fst::Map<&'static [u8]>> = OnceLock::new();
fn archive() -> &'static fst::Map<&'static [u8]> {
    ARCHIVE.get_or_init(|| {
        fst::Map::new(&include_bytes!(concat!(env!("OUT_DIR"), "/system-records.fst"))[..])
            .expect("build-checked system FST")
    })
}
fn value(address: u64) -> &'static str {
    let data = include_bytes!(concat!(env!("OUT_DIR"), "/system-values.bin"));
    let start = (address >> 32) as usize;
    let length = (address & 0xffffffff) as usize;
    std::str::from_utf8(&data[start..start + length]).expect("UTF-8 system dictionary")
}
pub(crate) fn text(name: &str) -> &'static str {
    value(archive().get(name).expect("required system dictionary"))
}
pub(crate) fn try_text(name: &str) -> Option<&'static str> {
    archive().get(name).map(value)
}
/// Generated in the same sorted order as the archive, without walking its line records.
pub(crate) fn domain_names() -> impl Iterator<Item = &'static str> {
    include_str!(concat!(env!("OUT_DIR"), "/domain-names.txt")).lines()
}
#[cfg(test)]
mod tests {
    #[test]
    fn archive_preserves_every_txt_byte_and_line_order() {
        let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../dictionaries");
        for entry in std::fs::read_dir(root).unwrap() {
            let path = entry.unwrap().path();
            if path.extension().is_some_and(|extension| extension == "txt") {
                let name: &'static str = Box::leak(
                    path.file_name()
                        .unwrap()
                        .to_str()
                        .unwrap()
                        .to_owned()
                        .into_boxed_str(),
                );
                let text = std::fs::read_to_string(&path).unwrap();
                for (index, line) in text.split_inclusive('\n').enumerate() {
                    assert_eq!(
                        super::value(
                            super::archive()
                                .get(format!("{name}\0{index:08x}"))
                                .unwrap()
                        ),
                        line
                    );
                }
                assert_eq!(
                    super::text(name).as_bytes(),
                    std::fs::read(&path).unwrap(),
                    "{}",
                    path.display()
                );
            }
        }
    }
}
