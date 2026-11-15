// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! Regression coverage for the upstream Meltype 1.6.0 features ported to the
//! shared Rust core: domain dictionaries, English country names, currency
//! rounding and the version candidate that names the OS and front.
use meltype_core::{dictionary::Dictionary, Session};

fn type_and_commit(raw: &str) -> String {
    let mut session = Session::default();
    let mut text = String::new();
    for c in raw.chars() {
        let result = session.key(if c == ' ' { 32 } else { 0 }, c as i32, 0);
        for commit in result.commits {
            text.push_str(&commit.text);
        }
        if !result.consumed {
            text.push(c);
        }
    }
    for commit in session.commit().commits {
        text.push_str(&commit.text);
    }
    text
}

#[test]
fn v160_domain_dictionaries_offer_terms() {
    // #1650: 分野の辞書 (医療・情報・駅名・固有名詞・人名) の語を候補に出す
    let dictionary = Dictionary::bundled();
    for (reading, expected) in [
        ("せんたんきょだいしょう", "先端巨大症"), // 医療 (JMED-DICT)
        ("いくせいしゃけん", "育成者権"),         // 情報 (IDiME)
        ("あいづかのうえき", "会津加納駅"),       // 駅名 (SKK)
        ("せんたんきょだいしょうの", "先端巨大症の"), // 助詞が付いても
        ("どくにんせい", "独任制"),               // 一般の語 (SudachiDict)
        ("ちゅうえつぱるぷ", "中越パルプ"),       // 固有名詞 (SudachiDict)
        (
            "あまみぐんとうしんこうかいはつとくべつそちほう",
            "奄美群島振興開発特別措置法", // 法令名 (e-Gov)
        ),
    ] {
        assert!(
            dictionary
                .domain_words(reading)
                .iter()
                .any(|word| word == expected),
            "{reading} → {:?}",
            dictionary.domain_words(reading)
        );
    }
    // 法令名: 題名の大きい ツ (マツサージ) は、ふだん打つ読み (まっさーじ) でも引ける。
    // 読みが改題前の題名のままの法律は入れない
    assert!(!dictionary
        .domain_words("あんままっさーじしあつしはりしきゆうしとうにかんするほうりつ")
        .is_empty());
    assert!(dictionary
        .domain_words(
            "おきなわけんにおけるちゅうりゅうぐんようちのへんかんにともなうとくべつそちにかんするほうりつ"
        )
        .is_empty());
    // 異表記 (洗ひざらし) は入れない
    assert!(!dictionary
        .domain_words("あらいざらし")
        .contains(&"洗ひざらし".into()));
    // 逆向き: よく使う語の読みには分野の語を足さない
    assert!(dictionary.domain_words("きょう").is_empty());
    assert!(dictionary.domain_words("あい").is_empty());
}

#[test]
fn v160_domain_terms_come_after_everyday_candidates() {
    // 分野の語は、使わない人には要らない候補なので、ふだんの候補 (ひらがな・カタカナ) を後ろに押さない
    let candidates = Dictionary::bundled().candidates("せんたんきょだいしょう", "Asia/Tokyo");
    let position = |word: &str| {
        candidates
            .iter()
            .position(|candidate| candidate == word)
            .unwrap_or_else(|| panic!("{word} が無い: {candidates:?}"))
    };
    let domain = position("先端巨大症");
    assert!(position("せんたんきょだいしょう") < domain);
    assert!(position("センタンキョダイショウ") < domain);
    assert!(position("ｾﾝﾀﾝｷｮﾀﾞｲｼｮｳ") < domain);
}

#[test]
fn v160_english_country_names_are_english_and_japanese_words_stay_japanese() {
    // #1615: CLDR の国・地域名は、日本語の文の中でも英語として扱う
    for word in [
        "Bermuda",
        "Puerto Rico",
        "Central African Republic",
        "Clipperton Island",
        "Palestinian Territories",
        "Congo - Kinshasa",
    ] {
        assert_eq!(type_and_commit(word), word, "{word}");
    }
    // 逆向き: 国名の語に似たローマ字の日本語は日本語のまま。
    // ローマ字として読める国名の語 (Oman) は国名の語に入れない
    for (typed, expected) in [
        ("tanakasan", Some("たなかさん")),
        ("iijan", Some("いいじゃん")),
        ("rikanojugyou", Some("りかのじゅぎょう")),
        ("iranai", Some("いらない")),
        ("omanjuutabeta", Some("おまんじゅうたべた")),
        // ん の打ち方 (nn) はローマ字の表で決まるので、日本語のままなことだけを見る (上流の試験と同じ)
        ("sannindekita", None),
        ("janakatta", Some("じゃなかった")),
    ] {
        let actual = type_and_commit(typed);
        assert!(
            !actual.contains(|c: char| c.is_ascii_alphabetic()),
            "{typed} → {actual} (日本語のままのはず)"
        );
        if let Some(expected) = expected {
            assert_eq!(actual, expected, "{typed}");
        }
    }
    assert!(!meltype_core::dictionary::country_words().contains(&"Oman"));
}

#[test]
fn v160_version_candidate_names_the_os_and_front() {
    let version = env!("CARGO_PKG_VERSION");
    // 辞書だけを使う所では OS 名だけ
    let dictionary = Dictionary::bundled();
    assert!(dictionary
        .candidates("ばーじょん", "Asia/Tokyo")
        .contains(&format!(
            "Meltype-FLASH {version} ({})",
            meltype_core::version::default_front()
        )));
    // 入力欄では設定の動作モードからフロントも書く
    let mut session = Session::default();
    for c in "ba-jonn".chars() {
        session.key(0, c as i32, 0);
    }
    let view = session.key(0x20, 0x20, 0).view.expect("conversion view");
    let expected = format!(
        "Meltype-FLASH {version} ({})",
        meltype_core::version::front(&session.settings)
    );
    assert!(
        view.candidates.contains(&expected),
        "{expected} が無い: {:?}",
        view.candidates
    );
}
