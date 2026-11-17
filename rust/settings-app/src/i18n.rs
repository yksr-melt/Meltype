// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! Settings-window translation. The IME itself is unchanged; only the
//! settings labels, descriptions, choices, and navigation are translated.
use serde_json::Value;
use std::cell::Cell;
use std::sync::OnceLock;

const JAPANESE: u8 = 0;
const ENGLISH: u8 = 1;
// 表示中の画面の言語を、別スレッドの画面や試験から変更しない。
thread_local! {
    static LANGUAGE: Cell<u8> = const { Cell::new(JAPANESE) };
}

fn data() -> &'static Value {
    static DATA: OnceLock<Value> = OnceLock::new();
    DATA.get_or_init(|| {
        serde_json::from_str(include_str!("../i18n-en.json")).expect("English settings translation")
    })
}

fn system_is_english() -> bool {
    #[cfg(windows)]
    {
        // The Windows UI language is more reliable than LANG for GUI apps.
        const ENGLISH: u16 = 0x09;
        let lang = unsafe { windows_sys::Win32::Globalization::GetUserDefaultUILanguage() };
        (lang & 0x3ff) == ENGLISH
    }
    #[cfg(not(windows))]
    {
        let locale = std::env::var("LC_ALL")
            .or_else(|_| std::env::var("LANG"))
            .unwrap_or_default();
        locale.to_ascii_lowercase().starts_with("en")
    }
}

pub fn set(setting: &str) {
    let english = match setting {
        "English" => true,
        "Japanese" => false,
        _ => system_is_english(),
    };
    LANGUAGE.set(if english { ENGLISH } else { JAPANESE });
}

pub fn english() -> bool {
    LANGUAGE.get() == ENGLISH
}

/// Translate a fixed UI string. Untranslated strings stay in Japanese.
pub fn t(text: &str) -> &str {
    if !english() {
        return text;
    }
    data()["ui"]
        .get(text)
        .and_then(Value::as_str)
        .unwrap_or(text)
}

pub fn category(text: &str) -> &str {
    if !english() {
        return text;
    }
    data()["categories"]
        .get(text)
        .and_then(Value::as_str)
        .unwrap_or(text)
}

pub fn label<'a>(key: &str, fallback: &'a str) -> &'a str {
    if !english() {
        return fallback;
    }
    data()["fields"]
        .get(key)
        .and_then(|field| field["label"].as_str())
        .unwrap_or(fallback)
}

pub fn description<'a>(key: &str, fallback: &'a str) -> &'a str {
    if !english() {
        return fallback;
    }
    data()["fields"]
        .get(key)
        .and_then(|field| field["description"].as_str())
        .filter(|text| !text.is_empty())
        .unwrap_or(fallback)
}

pub fn choice<'a>(kind: &str, value: &str, fallback: &'a str) -> &'a str {
    if !english() {
        return fallback;
    }
    data()["enums"]
        .get(kind)
        .and_then(|values| values.get(value))
        .and_then(Value::as_str)
        .unwrap_or(fallback)
}

pub fn window_title() -> &'static str {
    if english() {
        "Meltype-FLASH Settings"
    } else {
        "Meltype-FLASH 設定"
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rendering_threads_keep_their_selected_language() {
        set("Japanese");
        std::thread::spawn(|| {
            set("English");
            assert_eq!(t("設定"), "Settings");
        })
        .join()
        .unwrap();
        assert_eq!(t("設定"), "設定");
    }

    #[test]
    fn english_translates_navigation_fields_and_choices() {
        set("English");
        assert_eq!(t("設定"), "Settings");
        assert_eq!(category("1. 全般"), "1. General");
        assert_eq!(label("Enabled", "Meltype を有効にする"), "Enable Meltype");
        assert_eq!(choice("UiLanguage", "Auto", "自動"), "Auto");
        assert_eq!(choice("DetectionLevel", "Manual", "手動"), "Manual");
        set("Japanese");
        assert_eq!(t("設定"), "設定");
    }
}
