// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! 通貨の自動レート。欧州中央銀行 (ECB) の参照レートを取りに行き、データ用の
//! フォルダーに控えておく。入力のスレッドでは待たない (起動後に 1 本だけ動かす)。
//! 取れないときは控えを使い、控えも無ければ同梱のレート (固定) のままになる。
use meltype_core::{currency, storage::Store};
use std::path::PathBuf;
use std::sync::OnceLock;
use std::time::Duration;

const REFRESH: Duration = Duration::from_secs(6 * 60 * 60);
const FILE: &str = "exchange-rates-auto.txt";

/// 取りに行くスレッドを 1 本だけ始める。何度呼んでも増えない。
pub fn start() {
    static STARTED: OnceLock<()> = OnceLock::new();
    STARTED.get_or_init(|| {
        std::thread::spawn(|| loop {
            refresh();
            std::thread::sleep(REFRESH);
        });
    });
}

fn path() -> PathBuf {
    Store::default_directory().join(FILE)
}

fn refresh() {
    let cached = std::fs::read_to_string(path())
        .ok()
        .and_then(|text| currency::Rates::parse(&text));
    if let Some(rates) = &cached {
        currency::set_auto_rates(Some(rates.clone()));
    }
    // 今日のレートがもう手元にあるなら取りに行かない (ECB は平日の 16:00 ごろ更新)
    let today = chrono::Local::now().date_naive();
    if cached.is_some_and(|rates| rates.date >= today) {
        return;
    }
    let Some(xml) = meltype_core::windows_http::get_text(
        "www.ecb.europa.eu",
        "/stats/eurofxref/eurofxref-daily.xml",
        "application/xml",
    ) else {
        return;
    };
    let Some(rates) = currency::ecb_rates(&xml) else {
        return;
    };
    let _ = save(&currency::table(&rates));
    currency::set_auto_rates(Some(rates));
}

/// 控えを書く。書きかけのファイルを残さないよう、別名で書いてから置き換える。
fn save(text: &str) -> std::io::Result<()> {
    let path = path();
    let directory = path.parent().map(PathBuf::from).unwrap_or_default();
    std::fs::create_dir_all(&directory)?;
    let temporary = directory.join(format!(".{FILE}.tmp"));
    std::fs::write(&temporary, text)?;
    std::fs::rename(temporary, path)
}
