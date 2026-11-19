// SPDX-License-Identifier: GPL-3.0-or-later
use meltype_core::session::AsyncConversionPhase;
use meltype_core::Session;
use serde_json::json;

fn type_text(session: &mut Session, text: &str) {
    for c in text.chars() {
        session.key(0, c as i32, 0);
    }
}

#[test]
fn completed_request_applies_and_clears_state() {
    let mut session = Session::default();
    type_text(&mut session, "hashi");
    let id = session.begin_async_conversion("はし", "");
    assert_eq!(
        session.async_conversion().unwrap().phase,
        AsyncConversionPhase::Pending
    );
    assert!(session.complete_async_conversion(id, vec!["橋".into(), "箸".into()]));
    assert_eq!(
        session.async_conversion().unwrap().phase,
        AsyncConversionPhase::Ready
    );
    assert!(session.try_apply_async_conversion("はし", vec!["橋".into(), "箸".into()]));
    assert!(session.is_converting());
    assert!(session.async_conversion().is_none());
}

#[test]
fn stale_reading_is_rejected_without_replacing_newer_input() {
    let mut session = Session::default();
    type_text(&mut session, "hashi");
    let id = session.begin_async_conversion("はし", "");
    assert!(session.complete_async_conversion(id, vec!["橋".into()]));
    assert!(!session.try_apply_async_conversion("かき", vec!["柿".into()]));
    assert_eq!(
        session.async_conversion().unwrap().phase,
        AsyncConversionPhase::Ready
    );
}

#[test]
fn cancellation_and_timeout_never_apply_results() {
    let mut session = Session::default();
    type_text(&mut session, "hashi");
    let id = session.begin_async_conversion("はし", "");
    assert!(session.complete_async_conversion(id, vec!["橋".into()]));
    session.cancel_async_conversion();
    assert!(session.async_conversion().is_none());
    assert!(!session.try_apply_async_conversion("はし", vec!["橋".into()]));

    session.settings.set("ImeTimeoutMs", json!(50)).unwrap();
    type_text(&mut session, "kaki");
    let id = session.begin_async_conversion("かき", "");
    let requested = session.async_conversion().unwrap().requested_ms;
    assert_eq!(
        session.timeout_async_conversion(requested),
        Some(AsyncConversionPhase::Pending)
    );
    assert_eq!(
        session.timeout_async_conversion(requested + 50),
        Some(AsyncConversionPhase::TimedOut)
    );
    assert!(!session.complete_async_conversion(id, vec!["柿".into()]));
    assert!(!session.try_apply_async_conversion("かき", vec!["柿".into()]));
}

#[test]
fn pending_dictionary_lookup_uses_the_requesting_sessions_words() {
    let mut session = Session::default();
    session
        .preferences
        .user_dictionary
        .insert("はし".into(), vec!["登録した橋".into()]);
    let expected = session.dictionary_candidates("はし");
    let lookup = session.prepare_dictionary_candidates("はし".into());
    session.preferences.user_dictionary.clear();
    drop(session);
    let actual = std::thread::spawn(lookup).join().unwrap();
    assert_eq!(actual, expected);
    assert!(actual.contains(&"登録した橋".to_owned()));
}
