// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! 金額と通貨の読みに、別の通貨へ換算した額の候補を出す。
//!
//! レートは同梱した欧州中央銀行 (ECB) の参照レートを使い、使うときにネットへは
//! つながない。`tools/make-exchange-rates.mjs` で作った
//! `dictionaries/exchange-rates.txt` を読む。
use chrono::NaiveDate;
use std::collections::BTreeMap;
use std::sync::OnceLock;

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
            if rate > 0.0 {
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
        (from > 0.0).then(|| amount / from * to)
    }
}

/// 同梱したレート。読めなければ None。
pub fn bundled() -> Option<&'static Rates> {
    static RATES: OnceLock<Option<Rates>> = OnceLock::new();
    RATES
        .get_or_init(|| Rates::parse(crate::system_data::text("exchange-rates.txt")))
        .as_ref()
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

/// 有効数字 2 桁。0 以下は 0。
const SIGNIFICANT_DIGITS: i32 = 2;

/// 額を有効数字 2 桁に丸め、1 万以上は 万・億・兆 で書く (15,923 → 1.6万、62.8 → 63)。
/// 古いレートの概算なので、1 の位まで出して精度があるように見せない (#1871)。
fn format_value(value: f64) -> String {
    if value <= 0.0 {
        return "0".into();
    }
    let drop = value.log10().floor() as i32 + 1 - SIGNIFICANT_DIGITS;
    let rounded = if drop <= 0 {
        let scale = pow10(-drop);
        round_away(value * scale) / scale
    } else {
        let scale = pow10(drop);
        round_away(value / scale) * scale
    };
    let (size, suffix) = [(1e12, "兆"), (1e8, "億"), (1e4, "万")]
        .iter()
        .find(|(size, _)| rounded >= *size)
        .copied()
        .unwrap_or((1.0, ""));
    format!("{}{}", group_decimals(rounded / size), suffix)
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

/// 3 桁ごとに , を入れ、小数は 0 でないところまで書く (1.6・63・1,234.5)。
fn group_decimals(value: f64) -> String {
    let text = format!("{value:.10}");
    let (whole, fraction) = text.split_once('.').unwrap_or((text.as_str(), ""));
    let whole = group(whole.parse::<u64>().unwrap_or(0));
    let fraction = fraction.trim_end_matches('0');
    if fraction.is_empty() {
        whole
    } else {
        format!("{whole}.{fraction}")
    }
}

/// 換算した額の候補 (レートの日付つきと、額だけ)。
/// 換算先を決めていなければ、円以外は円に、円はドルとユーロにする。
pub fn candidates_with(
    amount: f64,
    from: (&str, &str),
    to: Option<(&str, &str)>,
    rates: &Rates,
) -> Vec<String> {
    let targets: Vec<(&str, &str)> = match to {
        Some(target) => vec![target],
        None if from.0 == "JPY" => vec![("USD", "ドル"), ("EUR", "ユーロ")],
        None => vec![("JPY", "円")],
    };
    let date = rates.date.format("%Y/%m/%d").to_string();
    let mut values = Vec::new();
    for (code, unit) in targets {
        let Some(value) = rates.convert(amount, from.0, code) else {
            continue;
        };
        let text = format!("約{}{}", format_value(value), unit);
        let dated = format!("{text}（{date} のレート）");
        for candidate in [dated, text] {
            if !values.contains(&candidate) {
                values.push(candidate);
            }
        }
    }
    values
}

/// 同梱のレートで換算した候補。
pub fn candidates(reading: &str) -> Vec<String> {
    let Some(parsed) = parse(reading) else {
        return Vec::new();
    };
    let Some(rates) = bundled() else {
        return Vec::new();
    };
    candidates_with(parsed.amount, parsed.from, parsed.to, rates)
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
        // 100 ドル → 約 1.6 万円 (有効数字 2 桁。1 の位は出さない)
        assert_eq!(
            candidates_with(100.0, ("USD", "ドル"), None, &rates),
            ["約1.6万円（2026/11/11 のレート）", "約1.6万円"]
        );
        assert_eq!(
            candidates_with(10_000.0, ("JPY", "円"), None, &rates),
            [
                "約63ドル（2026/11/11 のレート）",
                "約63ドル",
                "約56ユーロ（2026/11/11 のレート）",
                "約56ユーロ"
            ]
        );
        assert_eq!(
            candidates_with(10_000.0, ("JPY", "円"), Some(("EUR", "ユーロ")), &rates),
            ["約56ユーロ（2026/11/11 のレート）", "約56ユーロ"]
        );
        // 1 兆以上も兆で書く (丸めで 1 兆を超えても桁を落とさない)
        assert_eq!(format_value(1_000_000_000_000.0), "1兆");
        assert_eq!(format_value(15_923.0), "1.6万");
        assert_eq!(format_value(62.8), "63");
        assert_eq!(format_value(1_234.0), "1,200");
        assert_eq!(format_value(0.004), "0.004");
        assert_eq!(format_value(0.0), "0");
        assert!(candidates_with(100.0, ("THB", "バーツ"), None, &rates).is_empty());
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
