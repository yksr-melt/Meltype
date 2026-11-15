// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
pub mod ai_catalog;
pub mod ai_model;
pub mod app_identity;
mod bracket_candidates;
pub mod calculator;
mod candidate_sources;
mod commit_correction;
mod composition_units;
pub mod context;
#[cfg(feature = "filesystem")]
pub mod control;
pub mod currency;
pub mod detection;
pub mod dictionary;
pub mod github_titles;
mod greetings;
pub mod input_session;
pub mod kana;
pub mod kana_layout;
mod kana_layout_data;
#[cfg(feature = "filesystem")]
pub mod keycount;
mod language_memory;
pub mod linguistics;
pub mod math_candidates;
pub mod mozc;
pub mod number_candidates;
mod phrase_spacing;
pub mod plausible;
pub mod prefix_learning;
pub mod protection;
pub mod romaji;
pub mod seasonal;
pub mod session;
pub mod settings;
pub mod settings_schema;
pub mod shortcuts;
mod sigil;
pub mod skk;
pub mod spelling;
#[cfg(feature = "filesystem")]
pub mod storage;
#[cfg(not(feature = "filesystem"))]
#[path = "storage_memory.rs"]
pub mod storage;
mod system_data;
pub mod time_candidates;
pub mod typo;
pub mod user_dictionary;
pub mod version;
pub use session::{Session, SessionResult};

#[cfg(windows)]
pub mod windows_http;
