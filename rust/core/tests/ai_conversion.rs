// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
use meltype_core::{ai_model::AiModel, Session};
use serde_json::json;

fn converting(raw: &str) -> Session {
    let mut s = Session::default();
    s.settings.set("LiveConversion", json!(false)).unwrap();
    AiModel::Shunti.select(&mut s.settings, true);
    for c in raw.chars() {
        s.key(0, c as i32, 0);
    }
    s.key(32, 32, 0);
    s
}

#[test]
fn selected_ai_candidates_commit_and_stale_results_cannot_replace_new_input() {
    let mut s = converting("kyouhaiitenkidesu");
    let (reading, _) = s.ai_request().expect("Japanese conversion request");
    assert!(s.apply_ai_candidates(&reading, vec!["今日はいい天気です".into()]));
    assert_eq!(s.commit().commits[0].text, "今日はいい天気です");
    assert!(!s.apply_ai_candidates(&reading, vec!["古い応答".into()]));
    s.key(0, 'a' as i32, 0);
    assert!(!s.apply_ai_candidates(&reading, vec!["古い応答".into()]));
}

#[test]
fn disabled_engine_and_invalid_candidates_leave_standard_conversion_untouched() {
    let mut s = converting("nihongo");
    let (reading, _) = s.ai_request().unwrap();
    let previous = s.display(false);
    for invalid in [
        vec![],
        vec!["\n秘密".into()],
        vec!["a".repeat(1025)],
        vec!["候補".into(); 9],
    ] {
        assert!(!s.apply_ai_candidates(&reading, invalid));
        assert_eq!(s.display(false), previous);
    }
    AiModel::None.select(&mut s.settings, false);
    assert!(s.ai_request().is_none());
    assert!(!s.apply_ai_candidates(&reading, vec!["日本語".into()]));
    assert_eq!(s.display(false), previous);
}

#[test]
fn english_and_numeric_conversion_are_not_sent_to_ai() {
    for raw in ["hello", "https://example.com", "12345"] {
        assert!(converting(raw).ai_request().is_none(), "{raw}");
    }
}

#[test]
fn privacy_change_between_request_and_response_discards_ai_result() {
    let mut s = converting("nihongo");
    let (reading, _) = s.ai_request().unwrap();
    s.private = true;
    assert!(s.ai_request().is_none());
    assert!(!s.apply_ai_candidates(&reading, vec!["秘密の候補".into()]));
}

#[test]
fn explicit_user_dictionary_registration_is_not_replaced_by_ai() {
    let mut s = Session::default();
    s.settings.set("LiveConversion", json!(false)).unwrap();
    AiModel::Shunti.select(&mut s.settings, true);
    s.preferences
        .user_dictionary
        .insert("にほんご".into(), vec!["私の登録語".into()]);
    for c in "nihongo".chars() {
        s.key(0, c as i32, 0);
    }
    s.key(32, 32, 0);
    assert!(s.ai_request().is_none());
    assert!(!s.apply_ai_candidates("にほんご", vec!["日本語".into()]));
    assert_eq!(s.commit().commits[0].text, "私の登録語");
}

#[test]
fn answers_that_do_not_follow_the_reading_are_dropped_and_mozc_is_kept() {
    let mut s = converting("kyouhaiitenkidesu");
    let (reading, _) = s.ai_request().unwrap();
    let ordinary = s.display(false);
    // 繰り返し続けた答えは使わない。Zenzai と同じ判定を shuntelligence の候補にも当てる
    assert!(!s.apply_ai_candidates(
        &reading,
        vec!["今日はいい天気です今日はいい天気です".into()]
    ));
    assert_eq!(s.display(false), ordinary);
    // 読みと合う候補だけを残す
    assert!(s.apply_ai_candidates(
        &reading,
        vec![
            "今日はいい天気です".into(),
            "今日はいい天気です今日はいい天気です".into()
        ]
    ));
    assert_eq!(s.commit().commits[0].text, "今日はいい天気です");
}
