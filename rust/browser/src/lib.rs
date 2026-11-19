// SPDX-License-Identifier: GPL-3.0-or-later
//! Browser host for the same Session/FST/detection engine as desktop builds.
use meltype_core::{
    portable_mozc::KanjiDictionary, session::Preferences, settings::Settings, storage::State,
    Session,
};
use serde::{Deserialize, Serialize};
use std::cell::RefCell;
use wasm_bindgen::prelude::*;

thread_local! {
    // 辞書はセッションや学習スナップショットへ複製しない。
    static KANJI: RefCell<Option<KanjiDictionary>> = const { RefCell::new(None) };
}

#[derive(Default, Serialize, Deserialize)]
#[serde(default)]
struct Snapshot {
    settings: Option<serde_json::Value>,
    state: State,
}

#[wasm_bindgen]
pub struct BrowserSession {
    session: Session,
    saved_settings: Settings,
    ai_pending: bool,
}

#[wasm_bindgen]
impl BrowserSession {
    /// 同梱した辞書だけを読み込む。利用者の入力を外部へ送らない。
    pub fn dictionary_integrity() -> String {
        include_str!("../../../native/mozc/portable/integrity.txt")
            .trim()
            .to_owned()
    }

    // Used only after the extension fetch verifies the pinned SHA-256.
    pub fn load_verified_dictionary(bytes: Vec<u8>) -> Result<(), String> {
        let dictionary = KanjiDictionary::from_verified_package(bytes)?;
        KANJI.with(|slot| *slot.borrow_mut() = Some(dictionary));
        Ok(())
    }

    pub fn load_dictionary(bytes: Vec<u8>) -> Result<(), String> {
        let dictionary = KanjiDictionary::parse(bytes)?;
        KANJI.with(|slot| *slot.borrow_mut() = Some(dictionary));
        Ok(())
    }
    #[wasm_bindgen(constructor)]
    pub fn new(snapshot: &str) -> Result<BrowserSession, String> {
        let snapshot: Snapshot = if snapshot.is_empty() {
            Snapshot::default()
        } else {
            serde_json::from_str(snapshot).map_err(|e| e.to_string())?
        };
        let mut settings = match snapshot.settings {
            Some(value) => Settings::parse(&value.to_string())?,
            None => Settings::default(),
        };
        settings.remove_neural_settings();
        Self::validate(&settings)?;
        let mut session = Session::default();
        session.settings = settings.clone();
        session.preferences = snapshot.state.preferences.clone();
        session.state = snapshot.state;
        // This host never requests destructive edits to an existing field.
        session.can_delete = false;
        Ok(Self {
            session,
            saved_settings: settings,
            ai_pending: false,
        })
    }

    pub fn key(
        &mut self,
        vk: i32,
        character: i32,
        modifiers: i32,
        now_ms: f64,
    ) -> Result<String, String> {
        if !now_ms.is_finite() || now_ms < 0.0 {
            return Err("invalid key timestamp".into());
        }
        self.ai_pending = false;
        let was_converting = self.session.is_converting();
        let mut result = self.session.key_at(vk, character, modifiers, now_ms as u64);
        if !was_converting && result.consumed && self.apply_dictionary() {
            result = self.session.result(true);
        }
        self.ai_pending = !was_converting && self.session.is_converting();
        serde_json::to_string(&result).map_err(|e| e.to_string())
    }

    pub fn apply_dictionary(&mut self) -> bool {
        if !self.session.is_converting() {
            return false;
        }
        KANJI.with(|slot| {
            let dictionary = slot.borrow();
            let Some(dictionary) = dictionary.as_ref() else {
                return false;
            };
            let reading = self.session.conversion_reading();
            self.session
                .begin_clauses(dictionary.clauses(&self.session, &reading))
        })
    }

    pub fn ai_request(&self) -> Result<String, String> {
        serde_json::to_string(&if self.ai_pending {
            self.session.ai_request()
        } else {
            None
        })
        .map_err(|e| e.to_string())
    }
    pub fn ai_apply(&mut self, reading: &str, candidates: &str) -> Result<bool, String> {
        let candidates: Vec<String> =
            serde_json::from_str(candidates).map_err(|e| e.to_string())?;
        if !self.ai_pending {
            return Ok(false);
        }
        self.ai_pending = false;
        Ok(self.session.apply_ai_candidates(reading, candidates))
    }
    pub fn select(&mut self, index: u32) -> Result<String, String> {
        serde_json::to_string(&self.session.select(index as usize)).map_err(|e| e.to_string())
    }

    /// Finish the active composition before switching to/from direct input.
    pub fn set_direct(&mut self, enabled: bool) -> Result<String, String> {
        let mut result = self.session.commit();
        self.session.set_direct(enabled);
        result.consumed = true;
        serde_json::to_string(&result).map_err(|e| e.to_string())
    }

    pub fn is_direct(&self) -> bool {
        self.session.direct
    }

    pub fn select_prediction(&mut self, index: u32) -> Result<String, String> {
        serde_json::to_string(&self.session.select_prediction(index as usize))
            .map_err(|e| e.to_string())
    }

    pub fn view(&self) -> Result<String, String> {
        serde_json::to_string(&self.session.result(false)).map_err(|e| e.to_string())
    }

    pub fn reset(&mut self) {
        self.ai_pending = false;
        self.session.reset();
        self.session.set_context(Some(""), Some(""));
    }

    pub fn focus(&mut self, learning: bool) {
        self.reset();
        self.session.no_learning = !learning;
    }

    pub fn context(&mut self, before: &str, after: &str) {
        self.session.set_context(Some(before), Some(after));
    }

    /// A website policy is transient and must not discard an active composition.
    pub fn set_code_mode(&mut self, enabled: bool) -> bool {
        if self.session.code_input == enabled {
            return true;
        }
        if !self.session.raw().is_empty() {
            return false;
        }
        self.session.caret_moved(0, false);
        self.session.code_input = enabled;
        true
    }

    pub fn configure(&mut self, settings: &str) -> Result<(), String> {
        let mut settings = Settings::parse(settings)?;
        settings.remove_neural_settings();
        Self::validate(&settings)?;
        self.reset();
        self.saved_settings = settings.clone();
        self.session.settings = settings;
        Ok(())
    }

    pub fn snapshot(&self) -> Result<String, String> {
        let mut state = self.session.state.clone();
        state.preferences = self.session.preferences.clone();
        serde_json::to_string(&Snapshot {
            settings: Some(serde_json::to_value(&self.saved_settings).map_err(|e| e.to_string())?),
            state,
        })
        .map_err(|e| e.to_string())
    }

    pub fn clear_learning(&mut self) {
        self.session.state.clear_learning();
        let dictionary = self.session.preferences.user_dictionary.clone();
        self.session.preferences = Preferences {
            user_dictionary: dictionary,
            ..Default::default()
        };
    }
}

impl BrowserSession {
    fn validate(settings: &Settings) -> Result<(), String> {
        if settings.boolean("CursorIntoBrackets") {
            return Err(
                "CursorIntoBrackets is not supported by the ChromeOS host; set it to false".into(),
            );
        }
        Ok(())
    }
}
