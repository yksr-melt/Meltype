// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! 金額と通貨の読みに、別の通貨へ換算した額の候補を出す。
//!
//! レートは三つの出どころから選ぶ (設定 `CurrencyRateMode`)。
//! - 固定: 同梱した欧州中央銀行 (ECB) の参照レート。`tools/make-exchange-rates.mjs` で作った
//!   `dictionaries/exchange-rates.txt` を読む。
//! - 自動: 同じ ECB の参照レートをホストが取ってきて `set_auto_rates` に入れたもの。
//!   取れていないときは固定を使う。
//! - 手入力: 設定 `CurrencyManualRates` に書いた「1 単位あたりの円」。
//!
//! どれも使うときにネットへはつながない (取得はホストが入力の外で行う)。
use crate::settings::Settings;
use chrono::NaiveDate;
use std::collections::BTreeMap;
use std::sync::{Arc, OnceLock, RwLock};

/// 通貨の読み → 通貨コード・候補に書く名前。長い読みを先に置く (かなだどる を どる より先に)。
pub const CURRENCIES: &[(&str, &str, &str)] = &[
    ("おーすとらりあどる", "AUD", "豪ドル"),
    ("しんがぽーるどる", "SGD", "シンガポールドル"),
    ("じんみんげん", "CNY", "人民元"),
    ("ほんこんどる", "HKD", "香港ドル"),
    ("すいすふらん", "CHF", "スイスフラン"),
    ("かなだどる", "CAD", "カナダドル"),
    ("ごうどる", "AUD", "豪ドル"),
    ("べいどる", "USD", "米ドル"),
    ("ゆーろ", "EUR", "ユーロ"),
    ("ぽんど", "GBP", "ポンド"),
    ("うぉん", "KRW", "ウォン"),
    ("ばーつ", "THB", "バーツ"),
    ("るぴー", "INR", "ルピー"),
    ("どる", "USD", "ドル"),
    ("げん", "CNY", "元"),
    ("えん", "JPY", "円"),
];

/// 1 ユーロあたりの各通貨の額と、その日付。
#[derive(Clone, Debug)]
pub struct Rates {
    pub date: NaiveDate,
    pub rates: BTreeMap<String, f64>,
}

impl Rates {
    /// `# 日付: yyyy-MM-dd` と `通貨コード<TAB>レート` の行を読む。読めなければ None。
    pub fn parse(text: &str) -> Option<Self> {
        let mut date = None;
        let mut rates = BTreeMap::new();
        for raw in text.lines() {
            let line = raw.trim();
            if let Some(rest) = line.strip_prefix("# 日付:") {
                date = NaiveDate::parse_from_str(rest.trim(), "%Y-%m-%d").ok();
                continue;
            }
            if line.is_empty() || line.starts_with('#') {
                continue;
            }
            let Some((code, value)) = line.split_once('\t') else {
                continue;
            };
            if code.len() != 3 {
                continue;
            }
            let Ok(rate) = value.parse::<f64>() else {
                continue;
            };
            if rate.is_finite() && rate > 0.0 {
                rates.insert(code.to_owned(), rate);
            }
        }
        if rates.len() > 1 {
            Some(Self { date: date?, rates })
        } else {
            None
        }
    }

    /// amount (from の通貨) を to の通貨にした額。どちらかのレートが無ければ None。
    pub fn convert(&self, amount: f64, from: &str, to: &str) -> Option<f64> {
        let from = *self.rates.get(from)?;
        let to = *self.rates.get(to)?;
        if !amount.is_finite()
            || amount < 0.0
            || !from.is_finite()
            || from <= 0.0
            || !to.is_finite()
            || to <= 0.0
        {
            return None;
        }
        let value = amount / from * to;
        value.is_finite().then_some(value)
    }
}

/// 同梱したレート。読めなければ None。
pub fn bundled() -> Option<&'static Rates> {
    static RATES: OnceLock<Option<Rates>> = OnceLock::new();
    RATES
        .get_or_init(|| Rates::parse(crate::system_data::text("exchange-rates.txt")))
        .as_ref()
}

/// レートの出どころ。設定 `CurrencyRateMode` の値。
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Mode {
    /// 同梱した配布時のレート。
    #[default]
    Fixed,
    /// ホストが取ってきた最新のレート。取れていないときは固定。
    Auto,
    /// 設定で入れた「1 単位あたりの円」。
    Manual,
}

impl Mode {
    pub const ALL: [Self; 3] = [Self::Fixed, Self::Auto, Self::Manual];
    pub fn parse(text: &str) -> Option<Self> {
        Self::ALL
            .into_iter()
            .find(|mode| mode.value().eq_ignore_ascii_case(text))
    }
    pub fn value(self) -> &'static str {
        match self {
            Self::Fixed => "Fixed",
            Self::Auto => "Auto",
            Self::Manual => "Manual",
        }
    }
    pub fn label(self) -> &'static str {
        match self {
            Self::Fixed => "固定（同梱のレート）",
            Self::Auto => "自動（ネットから取得）",
            Self::Manual => "手入力",
        }
    }
}

/// ホスト (Windows の WinHTTP など) が取ってきた最新のレート。入力の外で入れる。
pub fn set_auto_rates(rates: Option<Rates>) {
    if let Ok(mut auto) = auto_rates_lock().write() {
        *auto = rates.map(Arc::new);
    }
}
pub fn auto_rates() -> Option<Arc<Rates>> {
    auto_rates_lock().read().ok().and_then(|auto| auto.clone())
}
fn auto_rates_lock() -> &'static RwLock<Option<Arc<Rates>>> {
    static AUTO: OnceLock<RwLock<Option<Arc<Rates>>>> = OnceLock::new();
    AUTO.get_or_init(|| RwLock::new(None))
}

/// 換算に使うレート一式。設定から作る。
#[derive(Clone)]
pub struct Currency {
    mode: Mode,
    manual: Option<Rates>,
    auto: Option<Arc<Rates>>,
    /// 換算した額を書く小数の桁 (0..=MAX_DECIMALS)。既定は 1。
    decimals: u32,
}

impl Default for Currency {
    fn default() -> Self {
        Self {
            mode: Mode::Fixed,
            manual: None,
            auto: None,
            decimals: 1,
        }
    }
}

impl Currency {
    /// 設定をそのまま使う (辞書だけを使う試験・プレビューでは既定の固定)。
    pub fn from_settings(settings: &Settings) -> Self {
        let mode = Mode::parse(settings.text("CurrencyRateMode")).unwrap_or_default();
        let manual = (mode == Mode::Manual)
            .then(|| manual_rates(settings.text("CurrencyManualRates")))
            .flatten();
        let auto = (mode == Mode::Auto).then(auto_rates).flatten();
        let decimals = settings
            .integer("CurrencyDecimals")
            .clamp(0, i64::from(MAX_DECIMALS)) as u32;
        Self {
            mode,
            manual,
            auto,
            decimals,
        }
    }
    /// 書く小数の桁 (0..=MAX_DECIMALS)。
    pub fn decimals(&self) -> u32 {
        self.decimals
    }
    pub fn mode(&self) -> Mode {
        self.mode
    }
    /// 使うレートと、値の書き方 (近似か・注記)。
    /// 手入力の表が読めないとき、自動の取得が無いときは、同梱の表 (固定) に戻る。
    fn source(&self) -> Option<(&Rates, bool, String)> {
        let date = |rates: &Rates| format!("{} のレート", rates.date.format("%Y/%m/%d"));
        let fixed = || bundled().map(|rates| (rates, true, date(rates)));
        match self.mode {
            Mode::Manual => self
                .manual
                .as_ref()
                .map(|rates| (rates, false, "手入力のレート".to_owned()))
                .or_else(fixed),
            Mode::Auto => self
                .auto
                .as_deref()
                .map(|rates| (rates, true, date(rates)))
                .or_else(fixed),
            Mode::Fixed => fixed(),
        }
    }
}

/// 「USD 150.2」「150.2」のような行を読んで、円を基準にした表にする。
/// 通貨コードを省いた行は米ドル (1 米ドルの円)。円は 1 とする。
pub fn manual_rates(text: &str) -> Option<Rates> {
    let mut rates = BTreeMap::from([("JPY".to_owned(), 1.0)]);
    // 設定画面の 1 行の入力欄でも複数の通貨を書けるよう、改行のほかカンマ・セミコロンでも区切る
    for line in text.split(['\n', ',', ';', '、']) {
        let line = line.split('#').next().unwrap_or("").trim();
        if line.is_empty() {
            continue;
        }
        let (code, value) = line
            .split_once(|c: char| c.is_whitespace() || c == '=' || c == ':')
            .map_or(("USD", line), |(code, value)| (code.trim(), value.trim()));
        let value = value.strip_prefix(['=', ':']).unwrap_or(value).trim();
        let code = code.to_ascii_uppercase();
        if code == "JPY" || code.len() != 3 || !code.bytes().all(|b| b.is_ascii_alphabetic()) {
            continue;
        }
        // 「1 ドル = 150.2 円」の 150.2。円を基準にした表には 1/150.2 を入れる
        let Ok(per_unit) = value.parse::<f64>() else {
            continue;
        };
        if per_unit.is_finite() && per_unit > 0.0 && (1.0 / per_unit).is_finite() {
            rates.insert(code, 1.0 / per_unit);
        }
    }
    (rates.len() > 1).then(|| Rates {
        date: chrono::Local::now().date_naive(),
        rates,
    })
}

/// ECB の eurofxref-daily.xml を読む (自動の取得結果)。
pub fn ecb_rates(xml: &str) -> Option<Rates> {
    let document = roxmltree::Document::parse(xml).ok()?;
    let day = document
        .descendants()
        .find(|node| node.has_tag_name("Cube") && node.attribute("time").is_some())?;
    let date = NaiveDate::parse_from_str(day.attribute("time")?, "%Y-%m-%d").ok()?;
    let mut rates = BTreeMap::from([("EUR".to_owned(), 1.0)]);
    for cube in day.children().filter(|node| node.has_tag_name("Cube")) {
        let (Some(code), Some(rate)) = (cube.attribute("currency"), cube.attribute("rate")) else {
            continue;
        };
        let Ok(rate) = rate.parse::<f64>() else {
            continue;
        };
        if code.len() == 3 && rate.is_finite() && rate > 0.0 {
            rates.insert(code.to_owned(), rate);
        }
    }
    (rates.len() > 1).then_some(Rates { date, rates })
}

/// 同梱の表と同じ形式の文字列 (自動の取得結果をキャッシュに書くとき)。
pub fn table(rates: &Rates) -> String {
    let mut text = format!(
        "# Meltype 通貨の換算に使うレート (1 ユーロあたりの各通貨の額)\n# 日付: {}\n",
        rates.date.format("%Y-%m-%d")
    );
    for (code, rate) in &rates.rates {
        text.push_str(&format!("{code}\t{rate}\n"));
    }
    text
}

/// 読みから取り出した金額・換算元・換算先 (決めていなければ None)。
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Parsed {
    pub amount: f64,
    pub from: (&'static str, &'static str),
    pub to: Option<(&'static str, &'static str)>,
}

fn currency(reading: &str) -> Option<(&'static str, &'static str)> {
    CURRENCIES
        .iter()
        .find(|(name, _, _)| *name == reading)
        .map(|(_, code, unit)| (*code, *unit))
}

fn digit(c: char) -> Option<char> {
    match c {
        '０'..='９' => char::from_u32(c as u32 - '０' as u32 + '0' as u32),
        '．' => Some('.'),
        '0'..='9' | '.' => Some(c),
        _ => None,
    }
}

/// 「100どる」「1まんえんをゆーろ」「１２．５ぽんど」のような読みを金額にする。
pub fn parse(reading: &str) -> Option<Parsed> {
    let reading = reading.trim_end_matches('\n');
    let mut number = String::new();
    let mut used = 0;
    for (offset, c) in reading.char_indices() {
        match digit(c) {
            Some(d) => {
                number.push(d);
                used = offset + c.len_utf8();
            }
            None => break,
        }
    }
    let rest = &reading[used..];
    let amount_text = number.as_str();
    let (whole, fraction) = amount_text
        .split_once('.')
        .map_or((amount_text, None), |(a, b)| (a, Some(b)));
    if whole.is_empty()
        || whole.len() > 12
        || !whole.bytes().all(|b| b.is_ascii_digit())
        || fraction
            .is_some_and(|f| f.is_empty() || f.len() > 4 || !f.bytes().all(|b| b.is_ascii_digit()))
    {
        return None;
    }
    let (multiplier, rest) = if let Some(rest) = rest.strip_prefix("まん") {
        (10_000.0, rest)
    } else if let Some(rest) = rest.strip_prefix("おく") {
        (100_000_000.0, rest)
    } else {
        (1.0, rest)
    };
    let (from_part, to_part) = rest
        .split_once('を')
        .map_or((rest, None), |(a, b)| (a, Some(b)));
    let from = currency(from_part)?;
    let to = match to_part {
        Some(target) => {
            let target = target.strip_suffix('に').unwrap_or(target);
            let target = currency(target)?;
            if target.0 == from.0 {
                return None;
            }
            Some(target)
        }
        None => None,
    };
    let amount = amount_text.parse::<f64>().ok()? * multiplier;
    Some(Parsed { amount, from, to })
}

fn group(value: u64) -> String {
    let text = value.to_string();
    let mut out = String::with_capacity(text.len() + text.len() / 3);
    for (index, c) in text.chars().enumerate() {
        if index > 0 && (text.len() - index).is_multiple_of(3) {
            out.push(',');
        }
        out.push(c);
    }
    out
}

/// 設定した桁 (既定は小数第 1 位) に丸め、1 万以上は 万・億・兆 で書く
/// (15,923 → 1.6万、62.8 → 62.8、桁を 0 にすれば 15,923 → 2万)。
/// 丸めると 0 になる小さな額だけは、有効数字 2 桁で書く (0.004 → 0.004)。
fn format_value(value: f64, decimals: u32) -> String {
    if value <= 0.0 {
        return "0".into();
    }
    let decimals = decimals.min(MAX_DECIMALS);
    let scale = pow10(decimals as i32);
    let round = |value: f64| round_away(value * scale) / scale;
    // 丸めた後の桁で 万・億・兆 を選ぶ (9,999.97 → 1万)
    let (size, suffix) = [(1e12, "兆"), (1e8, "億"), (1e4, "万")]
        .iter()
        .find(|(size, _)| round(value / size) >= 1.0)
        .copied()
        .unwrap_or((1.0, ""));
    let rounded = round(value / size);
    if rounded <= 0.0 {
        return if decimals == 0 {
            format!("0{suffix}")
        } else {
            format!("{}{}", significant_digits(value), suffix)
        };
    }
    format!("{}{}", group_decimals(rounded, decimals), suffix)
}

/// 小数の桁の上限。設定 `CurrencyDecimals` はこの範囲に収める。
pub const MAX_DECIMALS: u32 = 4;

/// 有効数字 2 桁で書く (0.004・0.05)。
fn significant_digits(value: f64) -> String {
    let drop = value.log10().floor() as i32 + 1 - 2;
    let scale = pow10(drop.abs());
    let rounded = if drop <= 0 {
        round_away(value * scale) / scale
    } else {
        round_away(value / scale) * scale
    };
    group_decimals(rounded, MAX_DECIMALS)
}

/// 0 から遠いほうへ丸める (C# の MidpointRounding.AwayFromZero と同じ)。
fn round_away(value: f64) -> f64 {
    if value >= 0.0 {
        (value + 0.5).floor()
    } else {
        (value - 0.5).ceil()
    }
}

fn pow10(exponent: i32) -> f64 {
    10f64.powi(exponent)
}

/// 3 桁ごとに , を入れ、`decimals` 桁まで書く (0 は省く。1.6・63・1,234.56)。
fn group_decimals(value: f64, decimals: u32) -> String {
    let text = format!("{value:.*}", decimals as usize);
    let (whole, fraction) = text.split_once('.').unwrap_or((text.as_str(), ""));
    let whole = group(whole.parse::<u64>().unwrap_or(0));
    let fraction = fraction.trim_end_matches('0');
    if fraction.is_empty() {
        whole
    } else {
        format!("{whole}.{fraction}")
    }
}

/// 換算した額の候補 (レートの出どころの注記つきと、額だけ)。
/// 換算先を決めていなければ、円以外は円に、円はドルとユーロにする。
/// `approximate` のときは ≒ を付け (固定・自動)、手入力はそのまま書く。
pub fn candidates_with(
    amount: f64,
    from: (&str, &str),
    to: Option<(&str, &str)>,
    rates: &Rates,
    approximate: bool,
    note: &str,
    decimals: u32,
) -> Vec<String> {
    let targets: Vec<(&str, &str)> = match to {
        Some(target) => vec![target],
        None if from.0 == "JPY" => vec![("USD", "ドル"), ("EUR", "ユーロ")],
        None => vec![("JPY", "円")],
    };
    let mark = if approximate { "≒" } else { "" };
    let mut values = Vec::new();
    for (code, unit) in targets {
        let Some(value) = rates.convert(amount, from.0, code) else {
            continue;
        };
        let text = format!("{mark}{}{unit}", format_value(value, decimals));
        let mut texts = vec![];
        if !note.is_empty() {
            texts.push(format!("{text}（{note}）"));
        }
        texts.push(text);
        for candidate in texts {
            if !values.contains(&candidate) {
                values.push(candidate);
            }
        }
    }
    values
}

impl Currency {
    /// 設定のレートで換算した候補。
    pub fn candidates(&self, reading: &str) -> Vec<String> {
        let Some(parsed) = parse(reading) else {
            return Vec::new();
        };
        let Some((rates, approximate, note)) = self.source() else {
            return Vec::new();
        };
        candidates_with(
            parsed.amount,
            parsed.from,
            parsed.to,
            rates,
            approximate,
            &note,
            self.decimals,
        )
    }
}

/// 同梱のレート (固定) で換算した候補。
pub fn candidates(reading: &str) -> Vec<String> {
    Currency::default().candidates(reading)
}

#[cfg(test)]
mod tests {
    use super::*;

    const TABLE: &str = "\
# Meltype 通貨の換算に使うレート (1 ユーロあたりの各通貨の額)
# 日付: 2026-11-11
EUR\t1
USD\t1.1127
JPY\t177.18
GBP\t0.83618
KRW\t1600.5
";

    fn rates() -> Rates {
        Rates::parse(TABLE).unwrap()
    }

    #[test]
    fn parses_the_bundled_table() {
        let rates = rates();
        assert_eq!(rates.date, NaiveDate::from_ymd_opt(2026, 11, 11).unwrap());
        assert_eq!(rates.rates["EUR"], 1.0);
        assert_eq!(rates.rates["JPY"], 177.18);
        assert!(Rates::parse("EUR\t1\nJPY\t177.18\n").is_none());
        assert!(bundled().is_some_and(|rates| {
            rates.rates.contains_key("JPY") && rates.rates.contains_key("USD")
        }));
    }

    #[test]
    fn converts_with_the_rate_date() {
        let rates = rates();
        let note = "2026/11/11 のレート";
        // 100 ドル → ≒1.6万円 (小数第 1 位に丸める。万で書く)
        assert_eq!(
            candidates_with(100.0, ("USD", "ドル"), None, &rates, true, note, 1),
            ["≒1.6万円（2026/11/11 のレート）", "≒1.6万円"]
        );
        assert_eq!(
            candidates_with(10_000.0, ("JPY", "円"), None, &rates, true, note, 1),
            [
                "≒62.8ドル（2026/11/11 のレート）",
                "≒62.8ドル",
                "≒56.4ユーロ（2026/11/11 のレート）",
                "≒56.4ユーロ"
            ]
        );
        assert_eq!(
            candidates_with(
                10_000.0,
                ("JPY", "円"),
                Some(("EUR", "ユーロ")),
                &rates,
                true,
                note,
                1
            ),
            ["≒56.4ユーロ（2026/11/11 のレート）", "≒56.4ユーロ"]
        );
        // 手入力は入れた数のまま (≒ を付けない)
        assert_eq!(
            candidates_with(
                100.0,
                ("USD", "ドル"),
                None,
                &rates,
                false,
                "手入力のレート",
                1
            ),
            ["1.6万円（手入力のレート）", "1.6万円"]
        );
        // 1 兆以上も兆で書く (丸めで 1 兆を超えても桁を落とさない)
        assert_eq!(format_value(1_000_000_000_000.0, 1), "1兆");
        assert_eq!(format_value(15_923.0, 1), "1.6万");
        assert_eq!(format_value(9_999.97, 1), "1万");
        assert_eq!(format_value(62.8, 1), "62.8");
        assert_eq!(format_value(1_234.56, 1), "1,234.6");
        assert_eq!(format_value(0.004, 1), "0.004");
        assert_eq!(format_value(0.0, 1), "0");
        assert!(candidates_with(100.0, ("THB", "バーツ"), None, &rates, true, note, 1).is_empty());
    }

    #[test]
    fn decimals_setting_changes_how_far_the_amount_is_written() {
        assert_eq!(format_value(62.83, 0), "63");
        assert_eq!(format_value(62.83, 1), "62.8");
        assert_eq!(format_value(62.834, 2), "62.83");
        assert_eq!(format_value(62.8344, 3), "62.834");
        assert_eq!(format_value(62.83456, 4), "62.8346");
        assert_eq!(format_value(62.83456, 9), "62.8346", "上限は 4 桁");
        assert_eq!(format_value(15_923.0, 0), "2万");
        assert_eq!(format_value(15_923.0, 2), "1.59万");
        assert_eq!(format_value(1_234.567, 2), "1,234.57");
        assert_eq!(format_value(0.4, 0), "0");
        assert_eq!(format_value(0.004, 0), "0");
        assert_eq!(format_value(0.004, 2), "0.004");
        // 設定を通すと、固定・自動・手入力のどれでも同じ桁で書く
        let mut settings = Settings::default();
        assert_eq!(Currency::from_settings(&settings).decimals(), 1);
        // 同梱の表に依らないよう、手入力のレート (1 ドル = 159.2338 円) で確かめる
        settings
            .set("CurrencyRateMode", serde_json::json!("Manual"))
            .unwrap();
        settings
            .set("CurrencyManualRates", serde_json::json!("USD 159.2338"))
            .unwrap();
        for (digits, expected) in [
            (0, "2万円"),
            (1, "1.6万円"),
            (2, "1.59万円"),
            (4, "1.5923万円"),
        ] {
            settings
                .set("CurrencyDecimals", serde_json::json!(digits))
                .unwrap();
            let currency = Currency::from_settings(&settings);
            assert_eq!(currency.decimals(), digits as u32);
            assert_eq!(currency.candidates("100どる")[1], expected, "{digits} 桁");
        }
        // 固定でも同じ桁の設定が効く (≒ と日付はそのまま)
        let mut fixed = Settings::default();
        fixed.set("CurrencyDecimals", serde_json::json!(0)).unwrap();
        assert_eq!(
            Currency::from_settings(&fixed).candidates("100どる")[1],
            "≒2万円"
        );
        settings
            .set("CurrencyDecimals", serde_json::json!(99))
            .unwrap();
        assert_eq!(
            Currency::from_settings(&settings).decimals(),
            MAX_DECIMALS,
            "多すぎる桁は上限に収める"
        );
        settings
            .set("CurrencyDecimals", serde_json::json!(-1))
            .unwrap();
        assert_eq!(Currency::from_settings(&settings).decimals(), 0);
    }

    #[test]
    fn manual_rates_are_yen_per_unit_and_only_need_the_number() {
        // 数値だけなら 1 米ドルの円
        fn near(value: Option<f64>, expected: f64) {
            assert!((value.unwrap() - expected).abs() < 1e-6, "{value:?}");
        }
        let yen = manual_rates("150").unwrap();
        near(yen.convert(100.0, "USD", "JPY"), 15_000.0);
        near(yen.convert(15_000.0, "JPY", "USD"), 100.0);
        // 通貨コードを書けばその通貨。区切りは空白・=・: のどれでもよい
        let yen = manual_rates(
            "USD 150
EUR=170
GBP:200
# これはコメント
",
        )
        .unwrap();
        near(yen.convert(100.0, "USD", "JPY"), 15_000.0);
        near(yen.convert(100.0, "EUR", "JPY"), 17_000.0);
        near(yen.convert(100.0, "GBP", "JPY"), 20_000.0);
        // 改行でも同じ
        let yen = manual_rates(
            "USD 150
EUR 170
",
        )
        .unwrap();
        near(yen.convert(100.0, "EUR", "JPY"), 17_000.0);
        // 読める行が無ければ手入力は使わない
        assert!(manual_rates("").is_none());
        assert!(manual_rates("どる 150").is_none());
        assert!(manual_rates("USD 0").is_none());
        assert!(manual_rates("USD -150").is_none());
    }

    #[test]
    fn manual_rates_preserve_yen_and_accept_spaced_separators() {
        let rates = manual_rates("USD = 150; EUR : 170; JPY 100").unwrap();
        assert_eq!(rates.rates["JPY"], 1.0);
        assert!((rates.convert(100.0, "USD", "JPY").unwrap() - 15000.0).abs() < 1e-6);
        assert!((rates.convert(100.0, "EUR", "JPY").unwrap() - 17000.0).abs() < 1e-6);
        assert!(manual_rates("USD 1e-320").is_none());
    }

    #[test]
    fn invalid_rates_and_overflow_do_not_produce_candidates() {
        assert!(Rates::parse("# 日付: 2026-11-11\nEUR\t1\nUSD\tinf\n").is_none());
        let mut rates = rates();
        rates.rates.insert("USD".into(), f64::MIN_POSITIVE);
        assert!(candidates_with(f64::MAX, ("USD", "ドル"), None, &rates, false, "", 1).is_empty());
        assert!(rates.convert(f64::NAN, "USD", "JPY").is_none());
        assert!(rates.convert(-1.0, "USD", "JPY").is_none());
    }

    #[test]
    fn ecb_daily_xml_becomes_the_same_table() {
        let xml = r#"<?xml version="1.0" encoding="UTF-8"?>
<gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
  <Cube><Cube time="2026-11-11"><Cube currency="USD" rate="1.1127"/><Cube currency="JPY" rate="177.18"/><Cube currency="GBP" rate="0.83618"/></Cube></Cube>
</gesmes:Envelope>"#;
        let rates = ecb_rates(xml).unwrap();
        assert_eq!(rates.date, NaiveDate::from_ymd_opt(2026, 11, 11).unwrap());
        assert_eq!(rates.rates["USD"], 1.1127);
        assert_eq!(rates.rates["EUR"], 1.0);
        assert_eq!(
            format_value(rates.convert(100.0, "USD", "JPY").unwrap(), 1),
            "1.6万"
        );
        // 書いた表を読み直すと元に戻る
        let again = Rates::parse(&table(&rates)).unwrap();
        assert_eq!(again.date, rates.date);
        assert_eq!(again.rates, rates.rates);
        assert!(ecb_rates("<Envelope/>").is_none());
    }

    #[test]
    fn mode_selects_fixed_auto_or_manual_rates() {
        assert_eq!(Mode::parse("Fixed"), Some(Mode::Fixed));
        assert_eq!(Mode::parse("auto"), Some(Mode::Auto));
        assert_eq!(Mode::parse("Manual"), Some(Mode::Manual));
        assert_eq!(Mode::parse("未知"), None);
        let mut settings = Settings::default();
        assert_eq!(
            Currency::from_settings(&settings).mode(),
            Mode::Fixed,
            "既定は同梱のレート"
        );
        settings
            .set("CurrencyRateMode", serde_json::json!("Manual"))
            .unwrap();
        settings
            .set("CurrencyManualRates", serde_json::json!("150"))
            .unwrap();
        let manual = Currency::from_settings(&settings);
        assert_eq!(
            manual.candidates("100どる"),
            ["1.5万円（手入力のレート）", "1.5万円"]
        );
        // 自動で取れていなければ固定の表に戻る
        settings
            .set("CurrencyRateMode", serde_json::json!("Auto"))
            .unwrap();
        assert!(!Currency::from_settings(&settings)
            .candidates("100どる")
            .is_empty());
    }

    #[test]
    fn parses_amount_and_currencies() {
        assert_eq!(
            parse("100どる"),
            Some(Parsed {
                amount: 100.0,
                from: ("USD", "ドル"),
                to: None
            })
        );
        assert_eq!(
            parse("1まんえんをゆーろ"),
            Some(Parsed {
                amount: 10_000.0,
                from: ("JPY", "円"),
                to: Some(("EUR", "ユーロ"))
            })
        );
        assert_eq!(
            parse("10000えんをどるに").unwrap().to,
            Some(("USD", "ドル"))
        );
        assert_eq!(parse("5かなだどる").unwrap().from, ("CAD", "カナダドル"));
        assert_eq!(
            parse("１２．５ぽんど"),
            Some(Parsed {
                amount: 12.5,
                from: ("GBP", "ポンド"),
                to: None
            })
        );
        assert!(parse("100にん").is_none());
        assert!(parse("どる").is_none());
        assert!(parse("100えんをえん").is_none());
    }
}
