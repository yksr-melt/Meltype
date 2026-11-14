// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! Regression coverage for the upstream Meltype 1.5.0 features ported to the
//! shared Rust core: prediction continuation, bracket families, Markdown
//! markers, slash dates and version filtering.
use meltype_core::{
    linguistics::Linguistics,
    session::Clause,
    storage::{State, Store},
    time_candidates as time, Session,
};
use serde_json::json;

fn type_text(session: &mut Session, text: &str) {
    for c in text.chars() {
        session.key(0, c as i32, 0);
    }
}

fn session_with_phrases(phrases: serde_json::Value) -> (tempfile::TempDir, Store, Session) {
    let directory = tempfile::tempdir().unwrap();
    std::fs::write(
        directory.path().join("state.json"),
        serde_json::to_vec(&json!({ "phrases": phrases })).unwrap(),
    )
    .unwrap();
    let store = Store::new(directory.path());
    let session = Session::open(store.clone()).unwrap();
    (directory, store, session)
}

fn convert(raw: &str, settings: &[(&str, bool)]) -> (String, Vec<String>) {
    let mut session = Session::default();
    for (name, value) in settings {
        session.settings.set(name, json!(value)).unwrap();
    }
    type_text(&mut session, raw);
    let result = session.key(0x20, 0x20, 0);
    let view = result.view.expect("conversion view");
    assert!(view.converting, "{raw} should convert");
    (view.text, view.candidates)
}

#[test]
fn v150_greetings_are_predicted_before_any_learning_and_after_history() {
    let mut session = Session::default();
    session
        .settings
        .set("PredictiveCandidates", json!(true))
        .unwrap();
    type_text(&mut session, "ohayo");
    let predictions = session.predictions();
    assert_eq!(predictions, ["おはようございます"]);

    let (_directory, _store, mut learned) = session_with_phrases(json!([
        { "reading": "おはようございます", "text": "おはようございます！", "count": 5, "used": 10 }
    ]));
    learned
        .settings
        .set("PredictiveCandidates", json!(true))
        .unwrap();
    type_text(&mut learned, "ohayo");
    let predictions = learned.predictions();
    assert_eq!(
        predictions[0], "おはようございます！",
        "learned phrase wins"
    );
    assert!(predictions.contains(&"おはようございます".into()));
}

#[test]
fn v150_last_clause_prediction_keeps_the_shown_prefix_and_types_commit_it() {
    let (_directory, store, mut session) = session_with_phrases(json!([
        { "reading": "とることができる", "text": "取ることができる", "count": 3, "used": 10 }
    ]));
    for name in ["LiveConversion", "PredictiveCandidates"] {
        session.settings.set(name, json!(true)).unwrap();
    }
    type_text(&mut session, "tan'iwotoru");
    assert!(session.update_live_conversion(vec![
        Clause::new("たんいを".into(), vec!["単位を".into()]),
        Clause::new("とる".into(), vec!["取る".into()]),
    ]));
    let view = session.result(false).view.unwrap();
    assert_eq!(view.text, "単位を取る");
    assert_eq!(view.predictions, ["取ることができる"]);

    let view = session.key(0x09, 0, 0).view.unwrap();
    assert_eq!(view.text, "単位を取ることができる");
    assert_eq!(view.selected_prediction, 0);

    let result = session.key(0x41, 'a' as i32, 0);
    assert_eq!(
        result
            .commits
            .iter()
            .map(|c| c.text.as_str())
            .collect::<Vec<_>>(),
        ["単位を取ることができる"]
    );
    assert_eq!(
        result.view.as_ref().map(|view| view.text.as_str()),
        Some("あ")
    );
    let learned = store
        .load()
        .unwrap()
        .phrases
        .into_iter()
        .find(|phrase| phrase.text == "取ることができる")
        .unwrap();
    assert_eq!(learned.reading, "とることができる");
    assert_eq!(learned.count, 4);
}

#[test]
fn v150_version_numbers_are_not_predicted_or_learned() {
    for text in [
        "Meltype-1.4.0",
        "Mozc-3.34.6239.102+24.11.oss",
        "Meltype-FLASH 1.4.0",
    ] {
        assert!(Linguistics::is_version_text(text), "{text}");
    }
    for text in ["Meltype", "Mozc", "Meltype-FLASH", "バージョン"] {
        assert!(!Linguistics::is_version_text(text), "{text}");
    }
    let mut state = State::default();
    state.phrases.push(meltype_core::storage::Phrase {
        reading: "ばーじょん".into(),
        text: "Meltype-1.4.0".into(),
        count: 99,
        used: 99,
    });
    assert!(Linguistics::bundled()
        .predict("ばーじょ", &state)
        .iter()
        .all(|word| word != "Meltype-1.4.0"));

    let (_directory, _store, mut session) = session_with_phrases(json!([]));
    type_text(&mut session, "ba-jonn");
    let view = session.key(0x20, 0x20, 0).view.unwrap();
    let current = format!(
        "Meltype-FLASH {} ({})",
        env!("CARGO_PKG_VERSION"),
        meltype_core::version::front(&session.settings)
    );
    assert!(
        view.candidates.contains(&current),
        "{current} が無い: {:?}",
        view.candidates
    );
}

#[test]
fn v150_brackets_convert_only_with_their_family() {
    for (raw, text, candidates) in [
        ("[", "「", &["「", "｢", "『", "［", "[", "【", "〔"][..]),
        ("]", "」", &["」", "｣", "』", "］", "]", "】", "〕"][..]),
        (
            "[]",
            "「」",
            &["「」", "｢｣", "『』", "［］", "[]", "【】", "〔〕"][..],
        ),
        ("(", "(", &["(", "（"][..]),
        ("()", "()", &["()", "（）"][..]),
        ("{", "{", &["{", "｛"][..]),
        ("<>", "<>", &["<>", "＜＞", "〈〉", "《》"][..]),
    ] {
        let (converted, actual) = convert(raw, &[]);
        assert_eq!(converted, text, "{raw}");
        assert!(
            actual
                .iter()
                .map(String::as_str)
                .eq(candidates.iter().copied()),
            "{raw}: {actual:?}"
        );
    }
    for (raw, text, candidates) in [
        ("[", "［", &["［", "[", "「", "『", "【", "〔", "｢"][..]),
        ("()", "（）", &["（）", "()"][..]),
    ] {
        let (converted, actual) = convert(
            raw,
            &[(
                if raw == "()" {
                    "FullWidthParentheses"
                } else {
                    "SquareBrackets"
                },
                true,
            )],
        );
        assert_eq!(converted, text, "{raw}");
        assert!(
            actual
                .iter()
                .map(String::as_str)
                .eq(candidates.iter().copied()),
            "{raw}: {actual:?}"
        );
    }
    let (_, actual) = convert("z[z]", &[("SquareBrackets", true)]);
    assert_eq!(actual[0], "『』");
    assert_eq!(actual.len(), 7);

    // An unmatched pair is an ordinary composition, not a bracket family.
    let (_, actual) = convert("「)", &[]);
    assert_eq!(actual[0], "「)");
    assert!(actual.len() < 7, "{actual:?}");
}

#[test]
fn v150_markdown_markers_commit_half_width_and_rules_keep_the_spelling() {
    for marker in ["-", ">", "*", "+", "#", "######"] {
        let mut session = Session::default();
        type_text(&mut session, marker);
        let result = session.key(0x20, 0x20, 0);
        assert_eq!(
            result
                .commits
                .iter()
                .map(|c| c.text.as_str())
                .collect::<Vec<_>>(),
            [format!("{marker} ")],
            "{marker}"
        );
    }
    let mut session = Session::default();
    type_text(&mut session, "#######");
    let result = session.key(0x20, 0x20, 0);
    assert!(result.commits.is_empty(), "seven hashes exceed headings");
    assert!(result.view.unwrap().converting);

    for (raw, first) in [("---", "---"), ("**", "**")] {
        let mut session = Session::default();
        type_text(&mut session, raw);
        assert_eq!(session.display(false), first);
        let view = session.key(0x20, 0x20, 0).view.unwrap();
        assert!(view.converting);
        assert_eq!(view.candidates[0], first);
    }
}

#[test]
fn v150_today_has_eight_digits_and_slash_dates_pick_the_upcoming_year() {
    let now = chrono::NaiveDate::from_ymd_opt(2026, 10, 9)
        .unwrap()
        .and_hms_opt(17, 22, 0)
        .unwrap();
    assert_eq!(
        time::candidates("きょう", now, "Asia/Tokyo")
            .last()
            .map(String::as_str),
        Some("20261009")
    );
    let date = |year, month, day| chrono::NaiveDate::from_ymd_opt(year, month, day).unwrap();
    assert_eq!(
        time::typed("10/20", date(2026, 10, 9)).unwrap(),
        [
            "10/20",
            "10/20",
            "10月20日",
            "10/20(火)",
            "10月20日(火)",
            "2026/10/20",
            "2026/10/20(火)",
            "2026年10月20日"
        ]
    );
    assert_eq!(
        time::typed("2/29", date(2026, 10, 9)).unwrap()[5],
        "2028/02/29"
    );
    assert_eq!(
        time::typed("1/5", date(2026, 12, 20)).unwrap()[5],
        "2027/01/05"
    );
    assert_eq!(
        time::typed("12/28", date(2026, 1, 5)).unwrap()[5],
        "2025/12/28"
    );
}

#[test]
fn v150_delete_forgets_the_selected_learned_prediction() {
    let (_directory, store, mut session) = session_with_phrases(json!([
        { "reading": "おはようございます", "text": "おはようございます！", "count": 5, "used": 10 }
    ]));
    session
        .settings
        .set("PredictiveCandidates", json!(true))
        .unwrap();
    type_text(&mut session, "ohayo");
    session.key(0x09, 0, 0);
    let result = session.key(0x2e, 0, 0);
    assert!(result.consumed);
    assert!(session
        .predictions()
        .iter()
        .all(|word| word != "おはようございます！"));
    assert!(store
        .load()
        .unwrap()
        .phrases
        .iter()
        .all(|phrase| phrase.text != "おはようございます！"));
}

#[test]
fn issue_1620_conversion_can_start_at_the_last_clause() {
    for (enabled, expected) in [(false, 0usize), (true, 1usize)] {
        let mut session = Session::default();
        session
            .settings
            .set("StartConversionAtLastClause", json!(enabled))
            .unwrap();
        type_text(&mut session, "kyouhaii");
        assert!(session.begin_clauses(vec![
            Clause::new("きょうは".into(), vec!["今日は".into(), "きょうは".into()]),
            Clause::new("いい".into(), vec!["いい".into(), "良い".into()]),
        ]));
        let view = session.result(false).view.unwrap();
        assert_eq!(view.selected_clause, expected, "enabled={enabled}");
        assert_eq!(view.selected_index, 0);
        assert_eq!(view.text, "今日はいい");
    }
}
