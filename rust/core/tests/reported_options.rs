// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! 上流の任意設定・候補要望の再現。
use meltype_core::{dictionary::Dictionary, Session};
use serde_json::json;

#[test]
fn issue_958_optional_numeric_spacing_preserves_identifiers_and_existing_spaces() {
    use meltype_core::session::Clause;
    for (text, expected) in [
        ("この処理を3回実行します", "この処理を 3 回実行します"),
        ("ファイルを5個選択します", "ファイルを 5 個選択します"),
        ("平均1.5回", "平均 1.5 回"),
        ("値1,000円", "値 1,000 円"),
        ("既存 3 回", "既存 3 回"),
        ("ファイルfile3.txtを開く", "ファイルfile3.txtを開く"),
        (
            "値https://example.com/3を参照",
            "値https://example.com/3を参照",
        ),
        ("名前3Dは識別子", "名前3Dは識別子"),
    ] {
        for enabled in [false, true] {
            let mut s = Session::default();
            s.settings
                .set("SpaceAroundNumbers", json!(enabled))
                .unwrap();
            type_text(&mut s, "nihongo");
            assert!(s.begin_clauses(vec![Clause::new("にほんご".into(), vec![text.into()])]));
            assert_eq!(
                s.commit().commits[0].text,
                if enabled { expected } else { text }
            );
        }
    }
    for (text, before, after, expected) in [
        ("回", "3", "", " 回"),
        ("回", "3 ", "", "回"),
        ("第", "", "3", "第 "),
        ("回", "file3", "", "回"),
        ("回", "https://example.com/3", "", "回"),
        ("3", "第", "回", " 3 "),
    ] {
        let mut s = Session::default();
        s.settings.set("SpaceAroundNumbers", json!(true)).unwrap();
        s.set_context(Some(before), Some(after));
        type_text(&mut s, "kai");
        assert!(s.begin_clauses(vec![Clause::new("かい".into(), vec![text.into()])]));
        assert_eq!(s.commit().commits[0].text, expected);
    }
}

fn type_text(s: &mut Session, text: &str) {
    for c in text.chars() {
        s.key(0, c as i32, 0);
    }
}

#[test]
fn issue_1008_z_symbols_can_be_disabled_including_after_an_edit() {
    for raw in [
        "z.",
        "z,",
        "z/",
        "zh",
        "zk",
        "1000Hz.",
        "10oz.",
        "quiz.",
        "jazz.",
        "quartz.",
        "showbiz.",
        "x,y,z,",
        "z-axis",
        "z-yheimen",
        "zkai",
    ] {
        let mut s = Session::default();
        s.settings.set("DisableZShortcuts", json!(true)).unwrap();
        type_text(&mut s, raw);
        let displayed = s.display(false);
        assert!(
            !displayed.contains(['…', '‥', '←', '↑', '→', '↓', '・', '〜']),
            "{raw}: {displayed}"
        );
        assert_eq!(s.raw(), raw);
    }
    let mut s = Session::default();
    type_text(&mut s, "z.");
    assert_eq!(s.display(false), "…");
    let mut s = Session::default();
    s.settings.set("DisableZShortcuts", json!(true)).unwrap();
    // Backspace retains composition units; subsequent z must still obey the setting.
    type_text(&mut s, "kazo");
    s.key(8, 0, 0);
    type_text(&mut s, "z.");
    assert!(!s.display(false).contains('…'));
}

#[test]
fn issue_1113_muhenkan_cycles_width_without_committing_or_changing_default() {
    for separate in [false, true] {
        let mut s = Session::default();
        s.settings.set("SeparateKeyRoles", json!(separate)).unwrap();
        s.settings
            .set("MuhenkanKatakanaCycle", json!(true))
            .unwrap();
        type_text(&mut s, "katakana");
        for expected in ["カタカナ", "ｶﾀｶﾅ", "かたかな", "カタカナ"] {
            let result = s.key(0x1d, 0, 0);
            assert!(result.consumed);
            assert!(result.commits.is_empty());
            assert_eq!(result.view.unwrap().text, expected);
        }
    }
    let mut s = Session::default();
    s.settings.set("SeparateKeyRoles", json!(true)).unwrap();
    type_text(&mut s, "katakana");
    assert_eq!(s.key(0x1d, 0, 0).view.unwrap().text, "katakana");
}

#[test]
fn muhenkan_from_conversion_or_explicit_english_restores_hiragana() {
    for key in [0x20, 0x79] {
        let mut s = Session::default();
        s.settings
            .set("MuhenkanKatakanaCycle", json!(true))
            .unwrap();
        type_text(&mut s, "nihongo");
        s.key(key, 0, 0);
        assert_eq!(s.key(0x1d, 0, 0).view.unwrap().text, "にほんご");
        assert_eq!(s.key(0x1d, 0, 0).view.unwrap().text, "ニホンゴ");
    }
}

#[test]
fn issue_460_empty_numpad_passes_through_but_composing_numpad_is_input() {
    // 上流1.5.1: 何も打っていないときのテンキーは変換ボックスに入れない。
    for (vk, c) in [
        (0x60, '0'),
        (0x69, '9'),
        (0x6a, '*'),
        (0x6b, '+'),
        (0x6d, '-'),
        (0x6e, '.'),
        (0x6f, '/'),
    ] {
        let mut s = Session::default();
        let result = s.key(vk, c as i32, 0);
        assert!(!result.consumed, "numpad {vk:#x} must pass through");
        assert!(result.commits.is_empty());
        assert!(s.raw().is_empty());
    }
    // 入力中は変換ボックスに入れる (3 + ji → 3時)。
    let mut s = Session::default();
    type_text(&mut s, "kyou");
    let result = s.key(0x63, '3' as i32, 0);
    assert!(result.consumed);
    assert_eq!(s.raw(), "kyou3");
    // 上の段の数字は今までどおり変換ボックスに入れる。
    let mut s = Session::default();
    assert!(s.key(0x31, '1' as i32, 0).consumed);
    assert_eq!(s.raw(), "1");
}

#[test]
fn issue_935_slash_dates_and_today_include_weekdays_and_validate_leap_days() {
    use meltype_core::time_candidates as time;
    for raw in ["10/20", "10月20日", "10がつ20にち"] {
        let values =
            time::typed(raw, chrono::NaiveDate::from_ymd_opt(2026, 10, 9).unwrap()).unwrap();
        for expected in ["10月20日", "10/20(火)", "10月20日(火)", "2026/10/20(火)"] {
            assert!(values.contains(&expected.into()), "{raw}: {expected}");
            assert!(time::is_dynamic_choice(raw, expected));
        }
    }
    assert_eq!(
        time::typed(
            "2/29",
            chrono::NaiveDate::from_ymd_opt(2026, 10, 9).unwrap()
        )
        .unwrap()[5],
        "2028/02/29"
    );
    assert_eq!(
        time::typed(
            "2/29",
            chrono::NaiveDate::from_ymd_opt(2024, 2, 29).unwrap()
        )
        .unwrap()[0],
        "2/29"
    );
    assert!(time::typed(
        "10/20/26",
        chrono::NaiveDate::from_ymd_opt(2026, 10, 9).unwrap()
    )
    .is_none());
    assert!(time::typed(
        "https://example.com",
        chrono::NaiveDate::from_ymd_opt(2026, 10, 9).unwrap()
    )
    .is_none());
    let now = chrono::NaiveDate::from_ymd_opt(2026, 10, 20)
        .unwrap()
        .and_hms_opt(0, 0, 0)
        .unwrap();
    let today = time::candidates("きょう", now, "Asia/Tokyo");
    assert!(today.contains(&"2026/10/20(火)".into()));
    assert!(today.contains(&"10/20(火)".into()));
}

#[test]
fn issues_959_1020_name_current_version_and_requested_numeric_candidate() {
    let d = Dictionary::bundled();
    assert!(d
        .candidates("めるたいぷ", "Asia/Tokyo")
        .contains(&"Meltype".into()));
    assert!(d
        .candidates("めるたいぷ", "Asia/Tokyo")
        .contains(&"Meltype-FLASH".into()));
    assert!(d.candidates("ばーじょん", "Asia/Tokyo").contains(&format!(
        "Meltype-FLASH {} ({})",
        env!("CARGO_PKG_VERSION"),
        meltype_core::version::default_front()
    )));
    assert!(d
        .candidates("いいよこいよ", "Asia/Tokyo")
        .contains(&"114514".into()));
}

#[test]
fn issue_1107_control_enter_confirms_once_and_does_not_send_the_key() {
    let mut s = Session::default();
    type_text(&mut s, "kyou");
    let result = s.key(13, 0, 2);
    assert!(result.consumed);
    assert_eq!(result.commits[0].text, "きょう");
    assert!(s.raw().is_empty());
    assert!(!s.key(13, 0, 2).consumed);
}

#[test]
fn issue_1016_page_keys_move_candidates_without_confirming_or_passing_through() {
    use meltype_core::session::Clause;
    for count in [1, 8, 9, 10, 18, 23] {
        let mut s = Session::default();
        type_text(&mut s, "kouho");
        assert!(s.begin_clauses(vec![Clause::new(
            "こうほ".into(),
            (0..count).map(|n| format!("候補{n}")).collect()
        )]));
        let first = s.result(false).view.unwrap();
        let length = first.candidates.len();
        let mut expected = first.selected_index;
        for vk in [0x22, 0x22, 0x22, 0x21, 0x21, 0x21] {
            let pages = length.div_ceil(9);
            let page = if vk == 0x22 {
                (expected / 9 + 1) % pages
            } else {
                (expected / 9 + pages - 1) % pages
            };
            expected = (page * 9 + expected % 9).min(length - 1);
            let result = s.key(vk, 0, 0);
            assert!(result.consumed);
            assert!(result.commits.is_empty());
            let view = result.view.unwrap();
            assert!(view.converting);
            assert_eq!(view.selected_index, expected);
            assert_eq!(view.text, view.candidates[expected]);
        }
    }
    assert!(!Session::default().key(0x22, 0, 0).consumed);
}

#[test]
fn issue_1076_slash_defaults_to_middle_dot_and_explicit_slash_is_retained() {
    assert!(Dictionary::bundled()
        .candidates("/", "Asia/Tokyo")
        .contains(&"・".into()));
    let mut s = Session::default();
    let result = s.key(0xbf, '/' as i32, 0);
    let mut committed: String = result.commits.iter().map(|e| e.text.as_str()).collect();
    if !result.consumed {
        committed.push('/');
    }
    committed.extend(s.commit().commits.iter().map(|e| e.text.as_str()));
    // 本家1.4.0: 通常入力 / → ・。明示候補では / と ・ を両方残す。
    assert_eq!(committed, "・");
    let mut s = Session::default();
    s.key(0xbf, '/' as i32, 0);
    let result = s.key(32, 32, 0);
    assert_eq!(result.view.unwrap().candidates, ["/", "・", "･", "／"]);
    assert_eq!(s.commit().commits[0].text, "/");
    // 直接入力 / → / は維持する。
    let mut s = Session::default();
    s.set_direct(true);
    assert!(!s.key(0xbf, '/' as i32, 0).consumed);
    assert!(s.commit().commits.is_empty());
}

#[test]
fn issue_1011_continued_reading_does_not_keep_short_dictionary_expansion() {
    let mut s = Session::default();
    let phrase = "返信が遅くなり、誠に申し訳ありません。";
    s.preferences
        .user_dictionary
        .insert("へん".into(), vec![phrase.into()]);
    type_text(&mut s, "hennkannkouritugayokunai");
    assert!(!s.display(false).contains(phrase));
    assert!(!s.commit().commits[0].text.contains(phrase));
}

#[test]
fn japanese_spellings_and_english_words_remain_distinguishable() {
    let mut errors = Vec::new();
    for (raw, expected) in [
        ("kye", "きぇ"),
        ("gwi", "ぐぃ"),
        ("zwa", "ずぁ"),
        ("fya", "ふゃ"),
        ("mye", "みぇ"),
        ("syi", "しぃ"),
        ("ryi", "りぃ"),
        ("hwa", "ふぁ"),
        ("hye", "ひぇ"),
        ("swe", "すぇ"),
        ("gwo", "ぐぉ"),
        ("dwo", "どぉ"),
    ]
    .into_iter()
    .chain(
        [
            "trying", "swan", "swine", "highway", "pathway", "hyena", "babying", "studying", "two",
            "network", "bye", "rye",
        ]
        .map(|word| (word, word)),
    ) {
        let mut s = Session::default();
        type_text(&mut s, raw);
        let shown = s.display(false);
        let committed = s.commit().commits[0].text.clone();
        if shown != expected || committed != expected {
            errors.push((raw, shown, committed));
        }
    }
    assert!(errors.is_empty(), "{errors:?}");
}

#[test]
fn japanese_irekata_keeps_its_reading_before_particles() {
    for (raw, reading) in [
        ("irekatawo", "いれかたを"),
        ("irekataga", "いれかたが"),
        ("irete", "いれて"),
        ("fire", "fire"),
    ] {
        let mut session = Session::default();
        type_text(&mut session, raw);
        assert_eq!(session.display(false), reading, "{raw}");
    }
}

#[cfg(feature = "bundled-mozc")]
#[test]
fn mobile_offline_dictionary_converts_and_commits_without_an_engine() {
    let mut session = Session::default();
    type_text(&mut session, "irekatawo");
    let result = session.key(32, 32, 0);
    assert_eq!(result.view.unwrap().text, "入れ方を");
    assert_eq!(session.commit().commits[0].text, "入れ方を");
}

#[test]
fn editing_a_particle_preserves_the_english_word_before_it() {
    for (stem, expected) in [("ashitanomeeting", "あしたのmeeting"), ("bug", "bug")] {
        let mut session = Session::default();
        type_text(&mut session, &format!("{stem}wa"));
        assert_eq!(session.display(false), format!("{expected}わ"));
        session.key(8, 0, 0);
        type_text(&mut session, "wo");
        assert_eq!(session.display(false), format!("{expected}を"));
        assert_eq!(session.commit().commits[0].text, format!("{expected}を"));
    }
}
