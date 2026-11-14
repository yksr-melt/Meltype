// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! 組み込みshuntiの問い合わせと適用。古い要求・秘密欄・英字や登録語は上書きしない。
use super::{Clause, Session};
impl Session {
    pub fn ai_request(&self) -> Option<(String, String)> {
        if !self.is_converting()
            || self.private
            || !self.settings.boolean("Enabled")
            || !self.settings.boolean("UseShunti")
            || crate::ai_model::AiModel::selected(&self.settings)
                != crate::ai_model::AiModel::Shunti
        {
            return None;
        }
        let reading = self.conversion_reading();
        if reading.is_empty()
            || reading.encode_utf16().count() > 160
            || reading
                .chars()
                .any(|c| c.is_ascii_alphabetic() || c.is_control())
            || !reading.chars().any(crate::linguistics::kana)
            || self.registered_conversion_plan(&reading, &[]).is_some()
            || self
                .clauses
                .iter()
                .any(|c| c.text().chars().any(|c| c.is_ascii_digit()))
        {
            return None;
        }
        Some((reading, self.before.clone()))
    }
    pub fn apply_ai_candidates(&mut self, expected: &str, mut candidates: Vec<String>) -> bool {
        let Some((reading, _)) = self.ai_request() else {
            return false;
        };
        if reading != expected
            || candidates.is_empty()
            || candidates.len() > 8
            || candidates.iter().any(|s| {
                s.is_empty() || s.encode_utf16().count() > 1024 || s.chars().any(char::is_control)
            })
        {
            return false;
        }
        // 読みと合わない答え (繰り返し・直前の文節の混入) は使わない。Zenzai と同じ判定を、
        // shuntelligence などほかの AI の候補にも当てる (#1675・#1511)
        candidates.retain(|candidate| crate::plausible::plausible(&reading, candidate));
        if candidates.is_empty() {
            return false;
        }
        let previous = self.clauses.iter().map(Clause::text).collect::<String>();
        if !previous.is_empty() && !candidates.contains(&previous) {
            candidates.push(previous);
        }
        self.begin_clauses(vec![Clause::new(reading, candidates)])
    }
}
