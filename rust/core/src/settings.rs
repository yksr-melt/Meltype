// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
//! Lossless settings document: upstream names and unknown future fields survive
//! edits. Defaults originate from Settings.cs at the migration baseline.
use serde::{Deserialize, Serialize};
use serde_json::{json, Map, Value};
use std::collections::HashSet;

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(transparent)]
pub struct Settings(pub Map<String, Value>);

const SHARED: &[&str] = &[
    "$schema",
    "Profiles",
    "ActiveProfile",
    "SettingsVersion",
    "WelcomeShown",
    "Enabled",
    "JapaneseKeyboardOnly",
    "AllowInjectedInput",
    "FileLog",
    "LogTypedText",
    "AutoUpdate",
    "Mode",
    "TsfIntroduced",
    "TsfOffered",
    "SettingsShowHelp",
    "SettingsWheelChangesValue",
    "UiLanguage",
];
fn same_type(default: &Value, value: &Value) -> bool {
    match default {
        Value::Bool(_) => value.is_boolean(),
        Value::Number(_) => value.is_i64(),
        Value::String(_) => value.is_string(),
        Value::Array(_) => value.is_array(),
        Value::Object(_) => value.is_object(),
        Value::Null => value.is_null(),
    }
}
fn migrate_input_style(values: &mut Map<String, Value>) {
    if values
        .get("InputStyle")
        .and_then(Value::as_str)
        .is_some_and(|s| s.eq_ignore_ascii_case("Both"))
    {
        values.insert("InputStyle".into(), json!("Romaji"));
    }
}
fn normalize_toggle_shortcuts(values: &mut Map<String, Value>, warnings: &mut Vec<String>) {
    use crate::shortcuts::ToggleShortcut;
    let mut shortcuts = match values.get("ToggleShortcuts").and_then(Value::as_array) {
        Some(entries) => entries.clone(),
        None if values.get("HankakuTogglesKeyboard") == Some(&json!(false)) => vec![],
        None => serde_json::to_value(ToggleShortcut::defaults())
            .unwrap()
            .as_array()
            .unwrap()
            .clone(),
    };
    if let Some(text) = values
        .get("ToggleKeyboardShortcuts")
        .and_then(Value::as_str)
    {
        // Flashの旧ポータブル入力では空文字がCtrl+Alt+Jを意味した。
        let text = if text.trim().is_empty() {
            "Ctrl+Alt+J"
        } else {
            text
        };
        for part in text
            .split([';', ',', '\n'])
            .map(str::trim)
            .filter(|part| !part.is_empty())
        {
            if let Some(shortcut) = ToggleShortcut::parse(part) {
                if !shortcuts
                    .iter()
                    .filter_map(|entry| {
                        serde_json::from_value::<ToggleShortcut>(entry.clone()).ok()
                    })
                    .any(|other| other.matches(shortcut.key, shortcut.modifiers))
                {
                    shortcuts.push(serde_json::to_value(shortcut).unwrap());
                }
            } else {
                warnings.push(format!(
                    "切替キー「{part}」を除外しました: キーの書式を解釈できません"
                ));
            }
        }
    }
    values.remove("HankakuTogglesKeyboard");
    values.remove("ToggleKeyboardShortcuts");
    let settings = Settings(values.clone());
    let mut accepted = Vec::new();
    for entry in shortcuts {
        match serde_json::from_value::<ToggleShortcut>(entry) {
            Ok(mut shortcut) => {
                shortcut.key = ToggleShortcut::normalize_key(shortcut.key);
                match shortcut.validate(&settings, &accepted) {
                    Ok(()) => accepted.push(shortcut),
                    Err(reason) => warnings.push(format!(
                        "切替キー「{}」を除外しました: {reason}",
                        shortcut.label()
                    )),
                }
            }
            Err(_) => warnings.push("不正な入力モード切替キーを除外しました".into()),
        }
    }
    values.insert(
        "ToggleShortcuts".into(),
        serde_json::to_value(accepted).unwrap(),
    );
}
impl Default for Settings {
    fn default() -> Self {
        let settings = Self(
            serde_json::from_str(include_str!("../data/settings-defaults.json"))
                .expect("bundled settings"),
        );
        #[cfg(any(target_os = "android", target_os = "ios"))]
        let settings = {
            let mut settings = settings;
            settings.remove_neural_settings();
            settings
        };
        settings
    }
}
impl Settings {
    pub fn parse(text: &str) -> Result<Self, String> {
        Self::parse_migrating(text).map(|(settings, _)| settings)
    }
    pub(crate) fn parse_migrating(text: &str) -> Result<(Self, bool), String> {
        let mut loaded: Map<String, Value> = json5::from_str(text).map_err(|e| e.to_string())?;
        let neural_migrate = if cfg!(any(target_os = "android", target_os = "ios")) {
            let mut document = Self(loaded);
            let removed = document.remove_neural_settings();
            loaded = document.0;
            removed
        } else {
            false
        };
        let mut defaults = Self::default();
        let version = loaded
            .get("SettingsVersion")
            .and_then(Value::as_i64)
            .unwrap_or(1);
        let removed = |value: Option<&Value>| {
            value.and_then(Value::as_str).is_some_and(|s| {
                s.eq_ignore_ascii_case("AutoSwitch") || s.eq_ignore_ascii_case("Both")
            })
        };
        let migrate = neural_migrate
            || version < 7
            || loaded.contains_key("HankakuTogglesKeyboard")
            || loaded.contains_key("ToggleKeyboardShortcuts")
            || removed(loaded.get("Mode"))
            || removed(loaded.get("InputStyle"))
            || loaded
                .get("Profiles")
                .and_then(Value::as_array)
                .is_some_and(|profiles| {
                    profiles.iter().any(|p| {
                        removed(p["Values"].get("Mode"))
                            || removed(p["Values"].get("InputStyle"))
                            || p["Values"].get("HankakuTogglesKeyboard").is_some()
                            || p["Values"].get("ToggleKeyboardShortcuts").is_some()
                    })
                });
        if version < 2 {
            if loaded.get("IdleFlushMs") == Some(&json!(400)) {
                loaded.insert("IdleFlushMs".into(), json!(700));
            }
            if loaded.get("MaxHoldMs") == Some(&json!(1200)) {
                loaded.insert("MaxHoldMs".into(), json!(2500));
            }
        }
        if version < 4 {
            let rules = loaded
                .entry("AppRules")
                .or_insert_with(|| defaults.0["AppRules"].clone());
            if let Some(rules) = rules.as_array_mut() {
                for rule in defaults.0["AppRules"]
                    .as_array()
                    .unwrap()
                    .iter()
                    .filter(|r| r["Profile"] == "Code")
                {
                    if let Some(old) = rules.iter_mut().find(|r| {
                        r["Process"]
                            .as_str()
                            .unwrap_or("")
                            .eq_ignore_ascii_case(rule["Process"].as_str().unwrap())
                    }) {
                        old["Profile"] = json!("Code");
                    } else {
                        rules.push(rule.clone());
                    }
                }
            }
        } else if version < 5 {
            if let Some(rules) = loaded.get_mut("AppRules").and_then(Value::as_array_mut) {
                if !rules.iter().any(|r| {
                    r["Process"]
                        .as_str()
                        .unwrap_or("")
                        .eq_ignore_ascii_case("maya.exe")
                }) {
                    rules.push(json!({"Process":"maya.exe","Enabled":true,"Profile":"Code"}));
                }
            }
        }
        // version < 8: ReadingLattice と SpaceAroundEnglish は試験版・上流と同じく
        // 既定では有効にしない。既存設定の値はそのまま保つ。
        normalize_toggle_shortcuts(&mut loaded, &mut Vec::new());
        loaded.insert("SettingsVersion".into(), json!(8));
        defaults.0.extend(loaded);
        defaults.normalize();
        Ok((defaults, migrate))
    }
    pub fn boolean(&self, name: &str) -> bool {
        self.0.get(name).and_then(Value::as_bool).unwrap_or(false)
    }
    pub fn integer(&self, name: &str) -> i64 {
        self.0.get(name).and_then(Value::as_i64).unwrap_or(0)
    }
    pub fn text(&self, name: &str) -> &str {
        self.0.get(name).and_then(Value::as_str).unwrap_or("")
    }
    pub fn toggle_shortcuts(&self) -> Vec<crate::shortcuts::ToggleShortcut> {
        self.0
            .get("ToggleShortcuts")
            .cloned()
            .and_then(|value| serde_json::from_value(value).ok())
            .unwrap_or_else(crate::shortcuts::ToggleShortcut::defaults)
    }
    pub fn toggle_shortcuts_label(&self) -> String {
        let labels = self
            .toggle_shortcuts()
            .iter()
            .map(|shortcut| shortcut.label())
            .collect::<Vec<_>>();
        if labels.is_empty() {
            "切替キー未登録".into()
        } else {
            labels.join(" / ")
        }
    }
    /// 設定画面の起動前に移行で除外されるキーと理由を取得する。
    pub fn shortcut_migration_warnings(text: &str) -> Vec<String> {
        let Ok(mut values) = json5::from_str::<Map<String, Value>>(text) else {
            return vec![];
        };
        let mut warnings = Vec::new();
        normalize_toggle_shortcuts(&mut values, &mut warnings);
        if let Some(profiles) = values.get_mut("Profiles").and_then(Value::as_array_mut) {
            for profile in profiles {
                if let Some(values) = profile.get_mut("Values").and_then(Value::as_object_mut) {
                    normalize_toggle_shortcuts(values, &mut warnings);
                }
            }
        }
        warnings
    }
    /// Hosts without Zenz discard its server configuration, including
    /// inactive profiles imported from desktop settings. Other fields survive.
    /// Return whether anything was removed, so persisted documents can migrate.
    pub fn remove_neural_settings(&mut self) -> bool {
        fn remove(values: &mut Map<String, Value>) -> bool {
            let mut removed = false;
            for key in [
                "UseZenzai",
                "ZenzaiServerPath",
                "ZenzaiModelPath",
                "ShuntiHelperPath",
            ] {
                removed |= values.remove(key).is_some();
            }
            if values
                .get("AiModel")
                .and_then(Value::as_str)
                .and_then(crate::ai_model::AiModel::parse)
                .is_some_and(|m| m.is_gguf())
            {
                values.insert("AiModel".into(), json!("None"));
                removed = true;
            }
            removed
        }
        let mut removed = remove(&mut self.0);
        if let Some(profiles) = self.0.get_mut("Profiles").and_then(Value::as_array_mut) {
            for profile in profiles {
                if let Some(values) = profile.get_mut("Values").and_then(Value::as_object_mut) {
                    removed |= remove(values);
                }
            }
        }
        removed
    }
    pub fn set(&mut self, name: &str, value: Value) -> Result<(), String> {
        if [
            "ZenzModelVariant",
            "JinenModelVariant",
            "KanakanjiModelVariant",
            "ShuntiModelVariant",
        ]
        .contains(&name)
            && !value
                .as_str()
                .and_then(crate::ai_catalog::find)
                .is_some_and(|m| {
                    crate::ai_model::AiModel::parse(&m.family)
                        .is_some_and(|f| crate::ai_catalog::key(f) == name)
                })
        {
            return Err("モデルのバージョンが一覧にありません".into());
        }
        if name == "AiModel"
            && value
                .as_str()
                .and_then(crate::ai_model::AiModel::parse)
                .is_none()
        {
            return Err("AIモデルは使用しない・Zenz・Jinen・kanakanji-edge・shuntelligenceから選択してください".into());
        }
        if name == "ToggleKeyboardShortcuts" && !value.is_string()
            || name == "HankakuTogglesKeyboard" && !value.is_boolean()
        {
            return Err(format!("invalid type for {name}"));
        }
        if name == "ToggleShortcuts" {
            let shortcuts: Vec<crate::shortcuts::ToggleShortcut> =
                serde_json::from_value(value.clone()).map_err(|_| {
                    "切替キーはKeyとModifiersを持つ一覧で指定してください".to_owned()
                })?;
            for (index, shortcut) in shortcuts.iter().enumerate() {
                shortcut
                    .validate(self, &shortcuts[..index])
                    .map_err(|reason| format!("{}: {reason}", shortcut.label()))?;
            }
        }
        if crate::settings_schema::OPTIONAL_BOOLEANS.contains(&name) && !value.is_boolean() {
            return Err(format!("invalid type for {name}"));
        }
        if let Some(default) = Self::default().0.get(name) {
            if !(default.is_boolean() && value.is_boolean()
                || default.is_number() && value.is_i64()
                || default.is_string() && value.is_string()
                || default.is_array() && value.is_array())
            {
                return Err(format!("invalid type for {name}"));
            }
        }
        self.0.insert(name.into(), value);
        self.normalize();
        Ok(())
    }
    pub fn normalize(&mut self) {
        if let Some(value) = self.0.get("AiModel") {
            let model = value
                .as_str()
                .and_then(crate::ai_model::AiModel::parse)
                .unwrap_or(crate::ai_model::AiModel::None);
            self.0.insert("AiModel".into(), json!(model.value()));
            if !model.is_gguf() {
                self.0.insert("UseZenzai".into(), json!(false));
            }
            if model != crate::ai_model::AiModel::Shunti {
                self.0.insert("UseShunti".into(), json!(false));
            }
        }
        if let Some(value) = self.0.get("CurrencyRateMode") {
            let mode = value
                .as_str()
                .and_then(crate::currency::Mode::parse)
                .unwrap_or_default();
            self.0
                .insert("CurrencyRateMode".into(), json!(mode.value()));
        }
        if self.0.contains_key("CurrencyDecimals") {
            let digits = self
                .integer("CurrencyDecimals")
                .clamp(0, i64::from(crate::currency::MAX_DECIMALS));
            self.0.insert("CurrencyDecimals".into(), json!(digits));
        }
        let defaults = Self::default();
        for key in crate::settings_schema::OPTIONAL_BOOLEANS {
            if let Some(value) = self.0.get_mut(*key) {
                if !value.is_boolean() {
                    *value = json!(false);
                }
            }
        }
        // Removed choices are read case-insensitively by the 1.3.0 converters.
        // Only a top-level AutoSwitch restarts the first-run TSF introduction.
        if self.text("Mode").eq_ignore_ascii_case("AutoSwitch") {
            self.0.insert("Mode".into(), json!("Tsf"));
            self.0.insert("TsfIntroduced".into(), json!(false));
        }
        migrate_input_style(&mut self.0);
        normalize_toggle_shortcuts(&mut self.0, &mut Vec::new());
        for (name, default) in &defaults.0 {
            let value = self
                .0
                .entry(name.clone())
                .or_insert_with(|| default.clone());
            if !same_type(default, value) {
                *value = default.clone();
            }
        }
        for (name, min, max) in [
            ("JapaneseThreshold", 2, 20),
            ("MaxPendingKeys", 2, 12),
            ("IdleFlushMs", 100, 3000),
            ("MaxHoldMs", 200, 5000),
            ("SessionIdleMs", 300, 60000),
            ("FeedbackWindowMs", 500, 30000),
            ("ImeTimeoutMs", 50, 2000),
        ] {
            let value = self
                .0
                .get(name)
                .and_then(Value::as_i64)
                .unwrap_or(defaults.integer(name))
                .clamp(min, max);
            self.0.insert(name.into(), json!(value));
        }
        for key in ["AppRules", "AppKinds", "Profiles"] {
            if !self.0[key].is_array() {
                self.0.insert(key.into(), json!([]));
            }
        }
        if let Some(rules) = self.0.get_mut("AppRules").and_then(Value::as_array_mut) {
            let mut seen = HashSet::new();
            rules.reverse();
            rules.retain(|r| {
                r["Process"]
                    .as_str()
                    .is_some_and(|s| !s.trim().is_empty() && seen.insert(s.trim().to_lowercase()))
            });
            rules.reverse();
            for r in rules {
                r["Process"] = json!(r["Process"].as_str().unwrap().trim());
                for (key, value) in [
                    ("Enabled", json!(true)),
                    ("Profile", json!("General")),
                    ("Kind", Value::Null),
                ] {
                    r.as_object_mut().unwrap().entry(key).or_insert(value);
                }
            }
        }
        if let Some(kinds) = self.0.get_mut("AppKinds").and_then(Value::as_array_mut) {
            for kind in kinds.iter_mut().filter_map(Value::as_object_mut) {
                for (key, value) in [
                    ("Name", json!("")),
                    ("Base", json!("General")),
                    ("DetectionLevel", Value::Null),
                    ("LiveConversion", Value::Null),
                    ("StartInEnglish", json!(false)),
                ] {
                    kind.entry(key).or_insert(value);
                }
            }
        }
        let active = self.text("ActiveProfile").trim().to_owned();
        let profiles = self.0["Profiles"].as_array().unwrap();
        let mut seen = HashSet::new();
        let mut profiles: Vec<_> = profiles
            .iter()
            .filter_map(|p| {
                let name = p["Name"].as_str()?.trim();
                if name.is_empty() || !seen.insert(name.to_owned()) {
                    return None;
                }
                let mut values = p["Values"].clone();
                if let Some(values) = values.as_object_mut() {
                    migrate_input_style(values);
                    normalize_toggle_shortcuts(values, &mut Vec::new());
                    // Mode is shared; old profile copies must not reintroduce it.
                    if values
                        .get("Mode")
                        .and_then(Value::as_str)
                        .is_some_and(|s| s.eq_ignore_ascii_case("AutoSwitch"))
                    {
                        values.insert("Mode".into(), json!("Tsf"));
                    }
                    for (key, value) in values.iter_mut() {
                        if let Some(default) = defaults.0.get(key) {
                            if !same_type(default, value) {
                                *value = default.clone();
                            }
                        }
                    }
                }
                Some(json!({"Name":name,"Values":values}))
            })
            .collect();
        let name = if active.is_empty() {
            profiles
                .first()
                .and_then(|p| p["Name"].as_str())
                .unwrap_or("標準")
                .to_owned()
        } else {
            active
        };
        self.0.insert("ActiveProfile".into(), json!(name));
        if !profiles.iter().any(|p| p["Name"] == name) {
            profiles.insert(0, json!({"Name":name,"Values":null}));
        }
        self.0.insert("Profiles".into(), json!(profiles));
        #[cfg(any(target_os = "android", target_os = "ios"))]
        self.remove_neural_settings();
    }
    fn profile_values(&self) -> Value {
        Value::Object(
            self.0
                .iter()
                .filter(|(k, _)| !SHARED.contains(&k.as_str()))
                .map(|(k, v)| (k.clone(), v.clone()))
                .collect(),
        )
    }
    fn values_of(&self, name: &str) -> Option<Map<String, Value>> {
        if name == self.text("ActiveProfile") {
            return self.profile_values().as_object().cloned();
        }
        self.0["Profiles"]
            .as_array()?
            .iter()
            .find(|p| p["Name"] == name)?["Values"]
            .as_object()
            .cloned()
    }
    /// Apply only fields edited in a settings window, preserving newer tray or
    /// other-process changes. Profile-local edits stay in their named profile.
    pub fn merge_edits(opened: &Self, edited: &Self, current: &Self) -> Self {
        if opened.0 == edited.0 {
            return current.clone();
        }
        let mut merged = current.clone();
        for key in SHARED
            .iter()
            .filter(|k| !["Profiles", "ActiveProfile"].contains(k))
        {
            if opened.0.get(*key) != edited.0.get(*key) {
                if let Some(value) = edited.0.get(*key) {
                    merged.0.insert((*key).into(), value.clone());
                }
            }
        }
        let names: Vec<_> = edited.0["Profiles"]
            .as_array()
            .into_iter()
            .flatten()
            .filter_map(|p| p["Name"].as_str())
            .collect();
        let active = if opened.text("ActiveProfile") != edited.text("ActiveProfile")
            || !names.contains(&current.text("ActiveProfile"))
        {
            edited.text("ActiveProfile")
        } else {
            current.text("ActiveProfile")
        };
        let mut profiles = vec![];
        for name in names {
            let Some(edits) = edited.values_of(name) else {
                continue;
            };
            let values = if let (Some(original), Some(mut values)) =
                (opened.values_of(name), current.values_of(name))
            {
                for (key, value) in edits {
                    if original.get(&key) != Some(&value) {
                        values.insert(key, value);
                    }
                }
                values
            } else {
                edits
            };
            if name == active {
                for (key, value) in &values {
                    if !SHARED.contains(&key.as_str()) {
                        merged.0.insert(key.clone(), value.clone());
                    }
                }
            }
            profiles.push(json!({"Name":name,"Values":values}));
        }
        merged.0.insert("Profiles".into(), json!(profiles));
        merged.0.insert("ActiveProfile".into(), json!(active));
        merged.normalize();
        merged
    }
    pub fn add_profile(&mut self, name: &str) -> Result<(), String> {
        let name = name.trim();
        if name.is_empty() {
            return Err("empty profile name".into());
        }
        if self.0["Profiles"]
            .as_array()
            .unwrap()
            .iter()
            .any(|p| p["Name"] == name)
        {
            return Err("profile already exists".into());
        }
        let values = self.profile_values();
        let active = self.text("ActiveProfile").to_owned();
        if let Some(current) = self.0["Profiles"]
            .as_array_mut()
            .unwrap()
            .iter_mut()
            .find(|p| p["Name"] == active)
        {
            current["Values"] = values.clone();
        }
        self.0["Profiles"]
            .as_array_mut()
            .unwrap()
            .push(json!({"Name":name,"Values":values}));
        self.0.insert("ActiveProfile".into(), json!(name));
        Ok(())
    }
    pub fn switch_profile(&mut self, name: &str) -> Result<(), String> {
        if name == self.text("ActiveProfile") {
            return Ok(());
        }
        let target = self.0["Profiles"]
            .as_array()
            .unwrap()
            .iter()
            .find(|p| p["Name"] == name)
            .cloned()
            .ok_or("unknown profile")?;
        let active = self.text("ActiveProfile").to_owned();
        let values = self.profile_values();
        if let Some(current) = self.0["Profiles"]
            .as_array_mut()
            .unwrap()
            .iter_mut()
            .find(|p| p["Name"] == active)
        {
            current["Values"] = values;
        }
        let defaults = Self::default();
        // Older profiles omit Flash-only options; absence means false, not
        // the option inherited from the profile we just left.
        for key in crate::settings_schema::OPTIONAL_BOOLEANS {
            self.0.remove(*key);
        }
        for (key, value) in defaults
            .0
            .iter()
            .filter(|(k, _)| !SHARED.contains(&k.as_str()))
        {
            self.0.insert(key.clone(), value.clone());
        }
        if let Some(values) = target["Values"].as_object() {
            for (key, value) in values.iter().filter(|(k, _)| !SHARED.contains(&k.as_str())) {
                self.0.insert(key.clone(), value.clone());
            }
        }
        self.0.insert("ActiveProfile".into(), json!(name));
        self.normalize();
        Ok(())
    }
    pub fn delete_profile(&mut self, name: &str) -> Result<(), String> {
        if name == self.text("ActiveProfile") {
            return Err("switch profiles before deleting the active profile".into());
        }
        self.0["Profiles"]
            .as_array_mut()
            .unwrap()
            .retain(|p| p["Name"] != name);
        Ok(())
    }
    pub fn rename_profile(&mut self, old: &str, new: &str) -> Result<(), String> {
        let new = new.trim();
        if new.is_empty()
            || self.0["Profiles"]
                .as_array()
                .unwrap()
                .iter()
                .any(|p| p["Name"] == new)
        {
            return Err("empty or duplicate profile name".into());
        }
        let profile = self.0["Profiles"]
            .as_array_mut()
            .unwrap()
            .iter_mut()
            .find(|p| p["Name"] == old)
            .ok_or("unknown profile")?;
        profile["Name"] = json!(new);
        if self.text("ActiveProfile") == old {
            self.0.insert("ActiveProfile".into(), json!(new));
        }
        Ok(())
    }
    pub fn remove_profile(&mut self, name: &str) -> Result<(), String> {
        let names: Vec<String> = self.0["Profiles"]
            .as_array()
            .unwrap()
            .iter()
            .filter_map(|p| p["Name"].as_str().map(str::to_owned))
            .collect();
        if names.len() <= 1 || !names.iter().any(|n| n == name) {
            return Err("cannot remove the last or unknown profile".into());
        }
        if self.text("ActiveProfile") == name {
            self.switch_profile(names.iter().find(|n| n.as_str() != name).unwrap())?;
        }
        self.delete_profile(name)
    }
    pub fn export_profile(&self) -> Value {
        json!({"format":"meltype-profile","version":7,"name":self.text("ActiveProfile"),"values":self.profile_values()})
    }
    pub fn import_profile(&mut self, text: &str) -> Result<String, String> {
        let file: Value = json5::from_str(text).map_err(|e| e.to_string())?;
        if file["format"] != "meltype-profile" {
            return Err("not a Meltype profile".into());
        }
        let mut raw = file["values"]
            .as_object()
            .ok_or("profile values must be an object")?
            .clone();
        normalize_toggle_shortcuts(&mut raw, &mut Vec::new());
        let mut imported = Self::default();
        for (key, value) in &raw {
            if (imported.0.contains_key(key)
                || crate::settings_schema::OPTIONAL_BOOLEANS.contains(&key.as_str()))
                && !SHARED.contains(&key.as_str())
            {
                imported.set(key, value.clone())?;
            }
        }
        let name = file["name"].as_str().unwrap_or("").trim();
        let name: String = if name.is_empty() {
            "読み込んだプロファイル"
        } else {
            name
        }
        .chars()
        .take(50)
        .collect();
        let mut unique = name.clone();
        let mut i = 2;
        while self.0["Profiles"]
            .as_array()
            .unwrap()
            .iter()
            .any(|p| p["Name"] == unique)
        {
            unique = format!("{name} ({i})");
            i += 1;
        }
        let previous = self.text("ActiveProfile").to_owned();
        self.add_profile(&unique)?;
        let profile = self.0["Profiles"]
            .as_array_mut()
            .unwrap()
            .last_mut()
            .unwrap();
        profile["Values"] = imported.profile_values();
        self.0.insert("ActiveProfile".into(), json!(previous));
        self.switch_profile(&unique)?;
        Ok(unique)
    }
    pub fn for_app(&self, process: &str) -> Self {
        self.for_app_on(process, crate::app_identity::Platform::current())
    }
    fn application_rule(
        &self,
        process: &str,
        platform: crate::app_identity::Platform,
    ) -> Option<&Value> {
        let find = |identity: &str| {
            self.0["AppRules"].as_array()?.iter().rev().find(|rule| {
                rule["Process"]
                    .as_str()
                    .is_some_and(|name| name.to_lowercase() == identity.to_lowercase())
            })
        };
        find(process).or_else(|| crate::app_identity::legacy_rule(process, platform).and_then(find))
    }
    pub fn for_app_on(&self, process: &str, platform: crate::app_identity::Platform) -> Self {
        let mut settings = self.clone();
        settings.0.insert("AppProfile".into(), json!("General"));
        settings.0.insert("StartInEnglish".into(), json!(false));
        let rule = self.application_rule(process, platform);
        if let Some(rule) = rule {
            if rule["Enabled"] == false {
                settings.0.insert("Enabled".into(), json!(false));
            }
            settings.0.insert(
                "AppProfile".into(),
                rule.get("Profile").cloned().unwrap_or(json!("General")),
            );
            if let Some(kind) = self.0["AppKinds"]
                .as_array()
                .unwrap()
                .iter()
                .find(|k| k["Name"] == rule["Kind"] && k["Name"].is_string())
            {
                settings.0.insert(
                    "AppProfile".into(),
                    kind.get("Base").cloned().unwrap_or(json!("General")),
                );
                for key in ["DetectionLevel", "LiveConversion", "StartInEnglish", ""] {
                    if let Some(v) = kind.get(key).filter(|v| !v.is_null()) {
                        settings.0.insert(key.into(), v.clone());
                    }
                }
            }
        }
        if settings.text("AppProfile") == "Game" {
            settings.0.insert("Enabled".into(), json!(false));
        }
        settings
    }
    /// Shared effective app policy; executable matching follows upstream and
    /// keeps extensions significant, with explicit aliases for native applications.
    pub fn app_policy(&self, process: &str, looks_like_game: bool) -> Value {
        let app = self.for_app(process);
        let explicit = self
            .application_rule(process, crate::app_identity::Platform::current())
            .is_some();
        json!({"enabled":app.boolean("Enabled") && !(self.boolean("StopInGames")&&looks_like_game&&!explicit),"profile":app.text("AppProfile"),"level":app.text("DetectionLevel"),"live":app.boolean("LiveConversion"),"startInEnglish":app.boolean("StartInEnglish"),})
    }
}
