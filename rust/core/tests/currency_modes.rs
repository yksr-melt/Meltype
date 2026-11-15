// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! 通貨の換算のレートの出どころ (固定・自動・手入力) を、入力から確かめる。
use meltype_core::{currency, Session};
use serde_json::json;

fn convert(settings: &[(&str, serde_json::Value)], raw: &str) -> Vec<String> {
    let mut session = Session::default();
    session
        .settings
        .set("LiveConversion", json!(false))
        .unwrap();
    for (name, value) in settings {
        session.settings.set(name, value.clone()).unwrap();
    }
    for c in raw.chars() {
        session.key(0, c as i32, 0);
    }
    session
        .key(0x20, 0x20, 0)
        .view
        .expect("conversion view")
        .candidates
}

#[test]
fn the_mode_chooses_fixed_auto_or_manual_rates() {
    // 固定: 同梱の配布時のレート。近似の印 (≒) と小数第 1 位
    let fixed = convert(&[], "100どる");
    assert!(
        fixed.iter().any(|c| c.starts_with("≒") && c.contains("円")),
        "{fixed:?}"
    );

    // 手入力: 数値だけなら 1 米ドルの円。≒ は付けない
    let manual = convert(
        &[
            ("CurrencyRateMode", json!("Manual")),
            ("CurrencyManualRates", json!("150")),
        ],
        "100どる",
    );
    assert!(
        manual.contains(&"1.5万円（手入力のレート）".to_owned()),
        "{manual:?}"
    );
    assert!(manual.contains(&"1.5万円".to_owned()), "{manual:?}");

    // 手入力の表が読めないときは、固定の表に戻る
    let broken = convert(
        &[
            ("CurrencyRateMode", json!("Manual")),
            ("CurrencyManualRates", json!("どる 150")),
        ],
        "100どる",
    );
    assert_eq!(broken, fixed);

    // 自動: 取ってきたレートがあればそれを使う。無ければ固定
    let fetched =
        currency::Rates::parse("# 日付: 2026-11-20\nEUR\t1\nJPY\t200\nUSD\t1\nGBP\t0.8\n").unwrap();
    currency::set_auto_rates(Some(fetched));
    let auto = convert(&[("CurrencyRateMode", json!("Auto"))], "100どる");
    assert!(
        auto.contains(&"≒2万円（2026/11/20 のレート）".to_owned()),
        "{auto:?}"
    );
    assert!(auto.contains(&"≒2万円".to_owned()), "{auto:?}");
    currency::set_auto_rates(None);
    assert_eq!(
        convert(&[("CurrencyRateMode", json!("Auto"))], "100どる"),
        fixed
    );
}
