// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
use meltype_core::{
    detection::{evaluate, Result as DetectionResult, Verdict},
    settings::Settings,
};
use meltype_core::{romaji, typo::Corrector};
fn classify(raw: &str, settings: &Settings) -> DetectionResult {
    let letters: String = raw
        .to_ascii_lowercase()
        .chars()
        .filter(char::is_ascii_lowercase)
        .collect();
    for end in 1..=letters.len() {
        let keys: Vec<_> = letters[..end]
            .bytes()
            .map(|c| c.to_ascii_uppercase() as i32)
            .collect();
        let result = evaluate(&letters[..end], &keys, end == letters.len(), settings);
        if result.verdict != Verdict::Undecided {
            return result;
        }
    }
    evaluate(&letters, &[], true, settings)
}

#[test]
fn upstream_detection_verdicts_and_early_decisions() {
    let cases: Vec<serde_json::Value> =
        serde_json::from_str(include_str!("../data/upstream-detection.json")).unwrap();
    let settings = Settings::default();
    let mut failures = Vec::new();
    for case in cases {
        let raw = case["typed"].as_str().unwrap();
        let result = classify(raw, &settings);
        if (result.verdict == Verdict::Japanese) != case["japanese"].as_bool().unwrap() {
            failures.push(format!("{raw}: {result:?}"));
        }
    }
    assert!(failures.is_empty(), "{}", failures.join("\n"));
    for (raw, early) in [
        ("hello", "hel"),
        ("the", "th"),
        ("npm", "np"),
        ("konnichiwa", "konn"),
        ("watashi", "wata"),
        ("kyouha", "kyou"),
    ] {
        assert_eq!(classify(raw, &settings).text, early, "{raw}");
    }
}

#[test]
fn detection_settings_control_kana_threshold_and_typo_contributions() {
    let mut settings = Settings::default();
    settings
        .set("JapaneseThreshold", serde_json::json!(12))
        .unwrap();
    assert_ne!(classify("konnichiwa", &settings).verdict, Verdict::Japanese);
    settings = Settings::default();
    let result = evaluate("konic", &[], false, &settings);
    assert_eq!(
        result
            .contributions
            .iter()
            .filter(|c| c.source == "Typo")
            .map(|c| c.japanese)
            .sum::<i32>(),
        3
    );
    settings
        .set("TypoEnabled", serde_json::json!(false))
        .unwrap();
    assert!(evaluate("konic", &[], false, &settings)
        .contributions
        .iter()
        .all(|c| c.source != "Typo"));
    for style in ["Kana"] {
        settings
            .set("InputStyle", serde_json::json!(style))
            .unwrap();
        for key in [0x51, 0x58, 0x56, 0x4c] {
            assert_ne!(
                evaluate(
                    &(key as u8 as char).to_ascii_lowercase().to_string(),
                    &[key],
                    true,
                    &settings
                )
                .verdict,
                Verdict::English
            );
        }
    }
    settings
        .set("InputStyle", serde_json::json!("Kana"))
        .unwrap();
    assert_eq!(
        evaluate("byiaf", &[0x42, 0x59, 0x49, 0x41, 0x46], true, &settings).verdict,
        Verdict::Japanese
    );
    let variants = romaji::spelling_variants("shinbun", 64);
    assert!(variants.iter().any(|s| s == "shinnbunn"));
    assert!(variants.iter().any(|s| s == "sinbun"));
}

#[test]
fn romaji_analysis_distinguishes_detection_from_composition() {
    assert!(!romaji::analyze("hello", false).valid);
    assert!(romaji::analyze("the", true).valid);
    assert!(!romaji::analyze("the", false).valid);
    assert_eq!(romaji::analyze("tanni", true).kana(), "たんい");
    assert_eq!(romaji::analyze("matcha", true).kana(), "まっちゃ");
    assert_eq!(romaji::analyze("onegaishimsu", true).consumed(), 9);
    assert!(!romaji::analyze("ん", true).valid);
    assert_eq!(Corrector::first_unreadable("ky", false), None);
    assert_eq!(Corrector::first_unreadable("ky", true), Some(0));
}

#[test]
fn upstream_romaji_typo_repairs() {
    let fixture: serde_json::Value =
        serde_json::from_str(include_str!("data/upstream-direct-typos.json")).unwrap();
    let cases = fixture["cases"].as_array().unwrap();
    assert_eq!(cases.len(), 20);
    for row in cases {
        let raw = row["raw"].as_str().unwrap();
        let actual = Corrector::bundled()
            .fix(raw, true)
            .map(|f| serde_json::json!({"Wrong":f.wrong,"Right":f.right,"Kana":f.kana}));
        assert_eq!(serde_json::json!(actual), row["expected"], "{raw}");
    }
    // The pure original corrector offers a repair for google. The Session's
    // English-span policy prevents applying it to actual English input.
    let mut session = meltype_core::Session::default();
    session
        .settings
        .set("DetectionLevel", serde_json::json!("Balanced"))
        .unwrap();
    for ch in "google".chars() {
        session.key(ch.to_ascii_uppercase() as i32, ch as i32, 0);
    }
    assert_eq!(session.key(13, 0, 0).commits[0].text, "google");
}

#[test]
fn typo_repairs_flow_through_session_and_respect_protected_english_and_settings() {
    use meltype_core::Session;
    for (raw, expected) in [
        ("onegaishimsu", "おねがいします"),
        ("arigatpu", "ありがとう"),
        ("arigtou", "ありがとう"),
        ("sumimasne", "すみません"),
        ("shitmeasu", "してます"),
        ("gozaimsu", "ございます"),
        ("yorosikuy", "よろしく"),
        ("kinouhaamegafuttemshita", "きのうはあめがふってました"),
        ("googlede", "googleで"),
        ("hello", "hello"),
        (
            "https://example.com/onegaishimsu",
            "https://example.com/onegaishimsu",
        ),
        ("@onegaishimsu", "@onegaishimsu"),
    ] {
        let mut s = Session::default();
        // Exercise typo repair of protected composition rather than the
        // separately tested SigilWordsDirect passthrough route.
        s.settings
            .set("SigilWordsDirect", serde_json::json!(false))
            .unwrap();
        for c in raw.chars() {
            s.key(0, c as i32, 0);
        }
        assert_eq!(s.commit().commits[0].text, expected, "{raw}");
    }
    let mut s = Session::default();
    s.settings
        .set("CorrectTypos", serde_json::json!(false))
        .unwrap();
    for c in "onegaishimsu".chars() {
        s.key(0, c as i32, 0);
    }
    assert_eq!(s.commit().commits[0].text, "おねがいしmす");
}

fn direct_typing(raw: &str, mut s: meltype_core::Session) -> (String, meltype_core::Session) {
    let mut document = String::new();
    for (index, c) in raw.chars().enumerate() {
        s.set_context(Some(&document), None);
        // Test the input behavior at known inter-key times. Cold dictionary
        // initialization or parallel test load must not simulate a 700ms pause.
        // The separate native smoke test exercises the real monotonic clock.
        let result = s.key_at(
            c.to_ascii_uppercase() as i32,
            c as i32,
            0,
            index as u64 * 10,
        );
        for edit in result.commits {
            if edit.delete_before > 0 {
                let expected = edit.expect.unwrap();
                assert!(document.ends_with(&expected));
                assert_eq!(expected.encode_utf16().count(), edit.delete_before);
                document.truncate(document.len() - expected.len());
            }
            document.push_str(&edit.text);
        }
        if !result.consumed {
            document.push(c);
        }
    }
    for edit in s.commit().commits {
        document.push_str(&edit.text);
    }
    (document, s)
}
fn direct_session() -> meltype_core::Session {
    let mut s = meltype_core::Session::default();
    s.direct = true;
    s.direct_detection = true;
    s.can_delete = true;
    s
}

#[test]
fn direct_detection_replaces_only_a_verified_prefix_and_keeps_english_and_protected_tokens() {
    let (text, s) = direct_typing("konnichiwa", direct_session());
    assert_eq!(text, "こんいちわ"); // Composition spelling consumes both n keys.
    assert_eq!(
        direct_typing("konnnichiwa", direct_session()).0,
        "こんにちわ"
    );
    assert!(!s.direct);
    for raw in [
        "hello",
        "github",
        "typescript",
        "@konnichiwa",
        "/konnichiwa",
        "$konnichiwa",
        "x_konnichiwa",
        "https://x.test/konnichiwa",
    ] {
        let (text, s) = direct_typing(raw, direct_session());
        assert_eq!(text, raw);
        assert!(s.direct, "{raw}");
    }
    let mut s = direct_session();
    s.can_delete = false;
    assert_eq!(direct_typing("konnichiwa", s).0, "konnichiwa");
    let mut s = direct_session();
    s.private = true;
    assert_eq!(direct_typing("konnichiwa", s).0, "konnichiwa");
    let mut s = direct_session();
    s.settings
        .set("DirectModeAutoDetect", serde_json::json!(false))
        .unwrap();
    assert_eq!(direct_typing("konnichiwa", s).0, "konnichiwa");
    let mut s = direct_session();
    s.settings
        .set("DetectionLevel", serde_json::json!("Manual"))
        .unwrap();
    assert_eq!(direct_typing("konnichiwa", s).0, "konnichiwa");
    let mut s = direct_session();
    s.settings
        .set("InputStyle", serde_json::json!("Both"))
        .unwrap();
    assert_eq!(s.settings.text("InputStyle"), "Romaji");
    assert_eq!(direct_typing("byiaf", s).0, "byiaf");
    let mut s = direct_session();
    assert!(!s.key(0x4e, 'n' as i32, 0).consumed);
    assert!(s.direct);
}

#[test]
fn kana_editing_transforms_and_undo_keep_physical_key_history() {
    let mut s = meltype_core::Session::default();
    s.settings
        .set("InputStyle", serde_json::json!("Kana"))
        .unwrap();
    s.key(0x54, 't' as i32, 0);
    s.key(0xc0, '@' as i32, 0);
    assert_eq!(s.display(false), "が");
    assert_eq!(s.key(8, 0, 0).view.unwrap().text, "か");
    s.key(0xc0, '@' as i32, 0);
    assert_eq!(s.key(0x76, 0, 0).view.unwrap().text, "ガ");
    assert_eq!(s.key(0x77, 0, 0).view.unwrap().text, "ｶﾞ");
    let text = s.commit().commits[0].text.clone();
    s.can_delete = true;
    s.set_context(Some(&text), None);
    assert_eq!(s.undo_commit().view.unwrap().text, "が");
    assert_eq!(s.key(8, 0, 0).view.unwrap().text, "か");
    s.reset();
    s.settings
        .set("InputStyle", serde_json::json!("Both"))
        .unwrap();
    for key in [0x42, 0x59, 0x49, 0x41, 0x46] {
        s.key(key, (key as u8 as char).to_ascii_lowercase() as i32, 0);
    }
    // In 1.3.0 the removed Both choice is migrated to Romaji.
    assert_eq!(s.settings.text("InputStyle"), "Romaji");
    assert_eq!(s.display(false), "びぃあf"); // Romaji: byi → びぃ (#1464).
}
