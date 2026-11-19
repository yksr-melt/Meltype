// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
//! Windows TSF companion. Menus use the same settings store as the input DLL.
use crate::{
    startup::Startup,
    windows_services::{self, Hotkey},
};
use eframe::egui;
use meltype_core::{ai_model::AiModel, settings::Settings, storage::Store};
use std::sync::mpsc::{self, Receiver};
use tray_icon::{
    menu::{CheckMenuItem, Menu, MenuEvent, MenuId, MenuItem, PredefinedMenuItem, Submenu},
    Icon, TrayIcon, TrayIconBuilder, TrayIconEvent,
};
#[derive(Clone, Debug)]
pub enum Action {
    Toggle,
    Mode(String),
    Help,
    Page(usize),
    KeyCount,
    AutoDetection,
    Profile(String),
    #[allow(dead_code)]
    Level(String),
    Folder,
    About,
    Report,
    Corrections,
    Backup(bool),
    Forget(&'static str),
    Uninstall,
    AutoUpdate,
    CheckUpdates,
    ReleaseNotes,
    Quit,
    MaintenanceQuit,
    Startup,
    Status(String),
    Error(String),
}
enum Event {
    Menu(MenuId),
    Toggle,
}
fn ai_status_text(settings: &Settings) -> String {
    let model = AiModel::selected(settings);
    if model == AiModel::None {
        return "AI変換: 使用しない".into();
    }
    let configured = if model == AiModel::Shunti {
        settings.boolean("UseShunti") && !settings.text("ShuntiHelperPath").is_empty()
    } else {
        settings.boolean("UseZenzai")
            && !settings.text("ZenzaiServerPath").is_empty()
            && !settings.text("ZenzaiModelPath").is_empty()
    };
    let status = if configured {
        "設定済み"
    } else {
        "設定を確認"
    };
    format!("AI変換: {}（{status}）", model.label())
}

pub struct Tray {
    stop_event: crate::tray_control::StopEvent,
    // Drop the subclass/hotkey before dropping its tray-owned native window.
    _hotkey: Option<Hotkey>,
    hotkey_receiver: Receiver<()>,
    icon: TrayIcon,
    enabled: CheckMenuItem,
    status: MenuItem,
    ime_mode: CheckMenuItem,
    keyboard_mode: CheckMenuItem,
    auto_detection: CheckMenuItem,
    ai_status: MenuItem,
    auto_update: CheckMenuItem,
    profiles: Submenu,
    profile_items: Vec<(String, CheckMenuItem)>,
    actions: Vec<(MenuId, Action)>,
    receiver: Receiver<Event>,
    last_enabled: bool,
    last_profile: String,
    startup: Option<Startup>,
    startup_item: CheckMenuItem,
    startup_user_request: bool,
    ctx: egui::Context,
    warnings: Vec<String>,
    guid: u128,
    directory: std::path::PathBuf,
    // Release only after the tray icon, callbacks and hotkeys have been dropped.
    _lock: std::fs::File,
}
impl Tray {
    pub fn new(ctx: &egui::Context, settings: &Settings, store: &Store) -> Result<Self, String> {
        std::fs::create_dir_all(&store.directory).map_err(|e| e.to_string())?;
        let lock = std::fs::OpenOptions::new()
            .create(true)
            .truncate(false)
            .read(true)
            .write(true)
            .open(store.directory.join(".tray.lock"))
            .map_err(|e| e.to_string())?;
        fs2::FileExt::try_lock_exclusive(&lock)
            .map_err(|_| "トレイは既に起動しています。設定画面を開きました。".to_owned())?;
        let menu = Menu::new();
        let status = MenuItem::new("Meltype-FLASH", false, None);
        let ai_status = MenuItem::new(ai_status_text(settings), false, None);
        let enabled = CheckMenuItem::new(
            "Meltype-FLASH を有効にする",
            true,
            settings.boolean("Enabled"),
            None,
        );
        menu.append(&status).map_err(|e| e.to_string())?;
        menu.append(&ai_status).map_err(|e| e.to_string())?;
        menu.append(&PredefinedMenuItem::separator())
            .map_err(|e| e.to_string())?;
        menu.append(&enabled).map_err(|e| e.to_string())?;
        let mut actions = vec![(enabled.id().clone(), Action::Toggle)];
        let ime_mode = CheckMenuItem::new(
            "Meltype-FLASH IME (入力欄に直接入力)",
            true,
            settings.text("Mode") == "Tsf",
            None,
        );
        let keyboard_mode = CheckMenuItem::new(
            "Meltype-FLASH キーボード (変換ボックスで入力)",
            true,
            settings.text("Mode") == "Keyboard",
            None,
        );
        menu.append(&ime_mode).map_err(|e| e.to_string())?;
        menu.append(&keyboard_mode).map_err(|e| e.to_string())?;
        actions.push((ime_mode.id().clone(), Action::Mode("Tsf".into())));
        actions.push((keyboard_mode.id().clone(), Action::Mode("Keyboard".into())));
        // 本家と同じ並び: 自動判定の ON/OFF は、動作モードのすぐ後ろに置く。
        let auto_detection = CheckMenuItem::new(
            "英語 / 日本語を自動で判定する",
            true,
            settings.text("DetectionLevel") != "Manual",
            None,
        );
        menu.append(&auto_detection).map_err(|e| e.to_string())?;
        actions.push((auto_detection.id().clone(), Action::AutoDetection));
        let profiles = Submenu::new("プロファイル", true);
        menu.append(&profiles).map_err(|e| e.to_string())?;
        let startup_item = CheckMenuItem::new("Windows の起動時に起動", false, false, None);
        menu.append(&startup_item).map_err(|e| e.to_string())?;
        actions.push((startup_item.id().clone(), Action::Startup));
        for (label, action) in [
            ("使い方...", Action::Help),
            ("設定...", Action::Page(0)),
            ("ユーザー辞書...", Action::Page(1)),
            ("定型文...", Action::Page(2)),
            ("ログ / 判定理由...", Action::Page(6)),
        ] {
            let item = MenuItem::new(label, true, None);
            menu.append(&item).map_err(|e| e.to_string())?;
            actions.push((item.id().clone(), action));
        }
        menu.append(&PredefinedMenuItem::separator())
            .map_err(|e| e.to_string())?;
        let folder = MenuItem::new("データフォルダを開く", true, None);
        menu.append(&folder).map_err(|e| e.to_string())?;
        actions.push((folder.id().clone(), Action::Folder));
        let backup = Submenu::new("バックアップ", true);
        for (label, restore) in [
            ("バックアップを書き出す...", false),
            ("バックアップから戻す...", true),
        ] {
            let item = MenuItem::new(label, true, None);
            backup.append(&item).map_err(|e| e.to_string())?;
            actions.push((item.id().clone(), Action::Backup(restore)));
        }
        menu.append(&backup).map_err(|e| e.to_string())?;
        for (label, action) in [
            ("不具合の報告・提案...", Action::Report),
            ("直した誤判定を報告...", Action::Corrections),
            ("Meltype-FLASH について...", Action::About),
            ("学習した語...", Action::Page(3)),
            ("打鍵数...", Action::KeyCount),
        ] {
            let item = MenuItem::new(label, true, None);
            menu.append(&item).map_err(|e| e.to_string())?;
            actions.push((item.id().clone(), action));
        }
        let forget = Submenu::new("学習データを消す", true);
        for (label, scope) in [
            ("この 1 時間に覚えたもの...", "hour"),
            ("今日覚えたもの...", "today"),
            ("すべて (リセット)...", "all"),
        ] {
            let item = MenuItem::new(label, true, None);
            forget.append(&item).map_err(|e| e.to_string())?;
            actions.push((item.id().clone(), Action::Forget(scope)));
        }
        menu.append(&forget).map_err(|e| e.to_string())?;
        let uninstall = MenuItem::new("アンインストール...", true, None);
        menu.append(&uninstall).map_err(|e| e.to_string())?;
        actions.push((uninstall.id().clone(), Action::Uninstall));
        let updates = Submenu::new("更新", true);
        let auto_update =
            CheckMenuItem::new("自動で更新する", true, settings.boolean("AutoUpdate"), None);
        updates.append(&auto_update).map_err(|e| e.to_string())?;
        actions.push((auto_update.id().clone(), Action::AutoUpdate));
        let check = MenuItem::new("今すぐ更新を確認", true, None);
        updates.append(&check).map_err(|e| e.to_string())?;
        actions.push((check.id().clone(), Action::CheckUpdates));
        let notes = MenuItem::new("リリースノート...", true, None);
        updates.append(&notes).map_err(|e| e.to_string())?;
        actions.push((notes.id().clone(), Action::ReleaseNotes));
        menu.append(&updates).map_err(|e| e.to_string())?;
        menu.append(&PredefinedMenuItem::separator())
            .map_err(|e| e.to_string())?;
        let quit = MenuItem::new("終了", true, None);
        menu.append(&quit).map_err(|e| e.to_string())?;
        actions.push((quit.id().clone(), Action::Quit));
        let guid = windows_services::tray_guid(&store.directory);
        let icon = TrayIconBuilder::new()
            .with_menu(Box::new(menu))
            .with_guid(guid)
            .with_tooltip("Meltype-FLASH")
            .with_icon(text_icon(settings.boolean("Enabled"))?)
            .build()
            .map_err(|e| e.to_string())?;
        let mut warnings = vec![];
        let (hotkey_sender, hotkey_receiver) = mpsc::channel();
        let hotkey = match Hotkey::attach_configured(
            icon.window_handle(),
            hotkey_sender,
            ctx,
            settings.text("PauseHotkey"),
        ) {
            Ok(hotkey) => {
                if let Some(name) = hotkey.name.as_deref() {
                    enabled.set_text(format!("Meltype-FLASH を有効にする ({name})"));
                } else {
                    warnings.push("一時停止/再開のホットキーは、候補がすべて使用中のため登録できませんでした。".into());
                }
                Some(hotkey)
            }
            Err(error) => {
                warnings.push(format!("ホットキー: {error}"));
                None
            }
        };
        let startup = match Startup::new(ctx) {
            Ok(startup) => Some(startup),
            Err(error) => {
                warnings.push(format!("起動設定: {error}"));
                None
            }
        };
        let (sender, receiver) = mpsc::channel();
        let menu_sender = sender.clone();
        let menu_ctx = ctx.clone();
        MenuEvent::set_event_handler(Some(move |event: MenuEvent| {
            let _ = menu_sender.send(Event::Menu(event.id));
            menu_ctx.request_repaint();
        }));
        let tray_ctx = ctx.clone();
        TrayIconEvent::set_event_handler(Some(move |event| {
            if matches!(event, TrayIconEvent::DoubleClick { .. }) {
                let _ = sender.send(Event::Toggle);
                tray_ctx.request_repaint();
            }
        }));
        let mut tray = Self {
            stop_event: crate::tray_control::StopEvent::new(&store.directory)?,
            _lock: lock,
            _hotkey: hotkey,
            hotkey_receiver,
            icon,
            enabled,
            status,
            ime_mode,
            keyboard_mode,
            auto_detection,
            ai_status,
            auto_update,
            profiles,
            profile_items: vec![],
            actions,
            receiver,
            last_enabled: settings.boolean("Enabled"),
            last_profile: settings.text("ActiveProfile").into(),
            startup,
            startup_item,
            startup_user_request: false,
            ctx: ctx.clone(),
            warnings,
            guid,
            directory: store.directory.clone(),
        };
        tray.sync(settings)?;
        Ok(tray)
    }
    pub fn input_status(&self, settings: &Settings, active: bool) {
        let count = meltype_core::keycount::load(&self.directory);
        let tooltip = if count.show {
            let today = count
                .days
                .get(&meltype_core::keycount::today())
                .copied()
                .unwrap_or(0);
            format!(
                "Meltype-FLASH 今日 {} 打",
                meltype_core::keycount::group(today)
            )
        } else {
            "Meltype-FLASH".to_owned()
        };
        let _ = self.icon.set_tooltip(Some(tooltip));
        if settings.boolean("Enabled") && settings.text("Mode") == "Keyboard" {
            let shortcut = settings.toggle_shortcuts_label();
            self.status.set_text(format!(
                "Meltype-FLASH キーボード: 入力{} ({shortcut} で切替)",
                if active { "ON" } else { "OFF" }
            ));
        }
    }
    pub fn sync(&mut self, settings: &Settings) -> Result<(), String> {
        let enabled = settings.boolean("Enabled");
        let message = if enabled != self.last_enabled {
            Some(if enabled {
                "Meltype-FLASH を有効にしました".to_owned()
            } else {
                "Meltype-FLASH を一時停止しました".to_owned()
            })
        } else if settings.text("ActiveProfile") != self.last_profile {
            Some(format!(
                "プロファイルを「{}」にしました",
                settings.text("ActiveProfile")
            ))
        } else {
            None
        };
        self.last_profile = settings.text("ActiveProfile").into();
        self.enabled.set_checked(enabled);
        self.ime_mode.set_checked(settings.text("Mode") == "Tsf");
        self.keyboard_mode
            .set_checked(settings.text("Mode") == "Keyboard");
        self.status.set_text(if !enabled {
            "一時停止中".to_owned()
        } else if settings.text("Mode") == "Keyboard" {
            let shortcut = settings.toggle_shortcuts_label();
            format!("Meltype-FLASH キーボード ({shortcut} で入力ON/OFF)")
        } else {
            "Meltype-FLASH IME (Win + Space で Meltype-FLASH を選んで入力)".to_owned()
        });
        if enabled != self.last_enabled {
            self.icon
                .set_icon(Some(text_icon(enabled)?))
                .map_err(|e| e.to_string())?;
            self.last_enabled = enabled;
        }
        self.auto_update.set_checked(settings.boolean("AutoUpdate"));
        self.auto_detection
            .set_checked(settings.text("DetectionLevel") != "Manual");
        self.ai_status.set_text(ai_status_text(settings));
        self.profiles
            .set_text(format!("プロファイル: {}", settings.text("ActiveProfile")));
        let names: Vec<String> = settings.0["Profiles"]
            .as_array()
            .into_iter()
            .flatten()
            .filter_map(|p| p["Name"].as_str().map(str::to_owned))
            .collect();
        if !names
            .iter()
            .eq(self.profile_items.iter().map(|(name, _)| name))
        {
            for (_, item) in self.profile_items.drain(..) {
                self.profiles.remove(&item).map_err(|e| e.to_string())?;
                self.actions.retain(|(id, _)| id != item.id());
            }
            for name in names {
                let item = CheckMenuItem::new(&name, true, false, None);
                self.profiles.append(&item).map_err(|e| e.to_string())?;
                self.actions
                    .push((item.id().clone(), Action::Profile(name.clone())));
                self.profile_items.push((name, item));
            }
        }
        for (name, item) in &self.profile_items {
            item.set_checked(name == settings.text("ActiveProfile"));
        }
        if let Some(startup) = &self.startup {
            self.startup_item
                .set_checked(startup.enabled.unwrap_or(false));
            self.startup_item
                .set_enabled(!startup.pending && startup.enabled.is_some());
        }
        if settings.boolean("ShowNotifications") {
            if let Some(message) = message {
                windows_services::notify(
                    self.icon.window_handle(),
                    self.guid,
                    "Meltype-FLASH",
                    &message,
                )?;
            }
        }
        Ok(())
    }
    pub fn poll(&mut self) -> Vec<Action> {
        if self.stop_event.requested() {
            return vec![Action::MaintenanceQuit];
        }
        let mut actions: Vec<Action> = self
            .receiver
            .try_iter()
            .filter_map(|event| match event {
                Event::Toggle => Some(Action::Toggle),
                Event::Menu(id) => self
                    .actions
                    .iter()
                    .find(|(key, _)| key == &id)
                    .map(|(_, action)| action.clone()),
            })
            .collect();
        actions.extend(self.hotkey_receiver.try_iter().map(|()| Action::Toggle));
        actions.extend(self.warnings.drain(..).map(Action::Status));
        if actions.iter().any(|a| matches!(a, Action::Startup)) {
            if let Some(startup) = &mut self.startup {
                if let Some(enabled) = startup.enabled {
                    match startup.request(Some(!enabled), &self.ctx) {
                        Ok(()) => self.startup_user_request = true,
                        Err(error) => actions.push(Action::Error(error)),
                    }
                }
            }
            actions.retain(|a| !matches!(a, Action::Startup));
        }
        if let Some(startup) = &mut self.startup {
            if let Some(result) = startup.poll() {
                match result {
                    Ok(enabled) if self.startup_user_request => actions.push(Action::Status(
                        if enabled {
                            "Windows の起動時に Meltype-FLASH を起動します。"
                        } else {
                            "Windows の起動時に Meltype-FLASH を起動しません。"
                        }
                        .into(),
                    )),
                    Err(error) => actions.push(Action::Error(error)),
                    _ => {}
                }
                self.startup_user_request = false;
            }
        }
        actions
    }
    #[cfg(feature = "visual-test")]
    pub fn hotkey_name(&self) -> Option<&str> {
        self._hotkey.as_ref().and_then(|h| h.name.as_deref())
    }
    #[cfg(feature = "visual-test")]
    pub fn startup_status(&self) -> (Option<bool>, bool) {
        self.startup
            .as_ref()
            .map_or((None, false), |s| (s.enabled, s.pending))
    }
    #[cfg(feature = "visual-test")]
    pub fn exercise_hotkey(&self) {
        windows_services::dispatch_test_hotkey(self.icon.window_handle());
    }
}
pub fn write_action(store: &Store, action: &Action) -> Result<(), String> {
    store
        .change_settings(|settings| match action {
            Action::AutoUpdate => settings.set(
                "AutoUpdate",
                serde_json::json!(!settings.boolean("AutoUpdate")),
            ),
            Action::Toggle => {
                settings.set("Enabled", serde_json::json!(!settings.boolean("Enabled")))
            }
            Action::Profile(name) => settings.switch_profile(name),
            Action::Mode(mode) => {
                if !["Tsf", "Keyboard"].contains(&mode.as_str()) {
                    return Err("unsupported input mode".into());
                }
                settings.set("Mode", serde_json::json!(mode))
            }
            Action::AutoDetection => settings.set(
                "DetectionLevel",
                serde_json::json!(if settings.text("DetectionLevel") == "Manual" {
                    "Balanced"
                } else {
                    "Manual"
                }),
            ),
            Action::Level(level) => settings.set("DetectionLevel", serde_json::json!(level)),
            _ => Ok(()),
        })
        .map_err(|e| e.to_string())
}
fn text_icon(enabled: bool) -> Result<Icon, String> {
    let image = eframe::icon_data::from_png_bytes(include_bytes!(
        "../../../assets/branding/meltype-flash-32.png"
    ))
    .map_err(|e| e.to_string())?;
    let mut rgba = image.rgba;
    // A white plate keeps the supplied black mark visible on dark taskbars.
    // The small status dot preserves the existing enabled/disabled indication.
    for pixel in rgba.as_chunks_mut::<4>().0 {
        let alpha = u16::from(pixel[3]);
        for channel in &mut pixel[..3] {
            *channel = ((u16::from(*channel) * alpha + 255 * (255 - alpha)) / 255) as u8;
            if !enabled {
                *channel = (*channel / 2) + 127;
            }
        }
        pixel[3] = 255;
    }
    let color = if enabled {
        [0, 120, 212]
    } else {
        [120, 120, 120]
    };
    for y in 23_i32..32 {
        for x in 23_i32..32 {
            let distance = (x - 27).pow(2) + (y - 27).pow(2);
            if distance <= 16 {
                let at = (y as usize * 32 + x as usize) * 4;
                rgba[at..at + 3].copy_from_slice(if distance <= 9 { &color } else { &[255; 3] });
            }
        }
    }
    Icon::from_rgba(rgba, image.width, image.height).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn status_identifies_the_selected_model_even_with_shared_backend_flags() {
        let mut settings = Settings::default();
        for model in AiModel::ALL {
            model.select(&mut settings, false);
            if model == AiModel::None {
                assert_eq!(ai_status_text(&settings), "AI変換: 使用しない");
                continue;
            }
            assert_eq!(
                ai_status_text(&settings),
                format!("AI変換: {}（設定を確認）", model.label())
            );
            model.select(&mut settings, true);
            for key in ["ShuntiHelperPath", "ZenzaiServerPath", "ZenzaiModelPath"] {
                settings.set(key, serde_json::json!("path")).unwrap();
            }
            assert_eq!(
                ai_status_text(&settings),
                format!("AI変換: {}（設定済み）", model.label())
            );
        }
    }
    #[test]
    fn tray_actions_write_shared_store_and_merge_with_an_open_settings_window() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path());
        let opened = store.settings().unwrap();
        let mut edited = opened.clone();
        edited
            .set("LiveConversion", serde_json::json!(false))
            .unwrap();
        write_action(&store, &Action::Toggle).unwrap();
        write_action(&store, &Action::Level("Conservative".into())).unwrap();
        let current = store.settings().unwrap();
        let merged = Settings::merge_edits(&opened, &edited, &current);
        assert!(!merged.boolean("Enabled"));
        assert!(!merged.boolean("LiveConversion"));
        assert_eq!(merged.text("DetectionLevel"), "Conservative");
    }
    #[test]
    fn both_tray_states_render_the_supplied_mark_on_windows() {
        assert!(text_icon(true).is_ok());
        assert!(text_icon(false).is_ok());
    }
    #[test]
    fn tray_modes_persist_without_losing_an_open_settings_draft() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path());
        let opened = store.settings().unwrap();
        let mut edited = opened.clone();
        edited
            .set("LiveConversion", serde_json::json!(false))
            .unwrap();
        write_action(&store, &Action::Mode("Keyboard".into())).unwrap();
        let current = store.settings().unwrap();
        assert_eq!(current.text("Mode"), "Keyboard");
        let merged = Settings::merge_edits(&opened, &edited, &current);
        assert_eq!(merged.text("Mode"), "Keyboard");
        assert!(!merged.boolean("LiveConversion"));
        write_action(&store, &Action::Mode("Tsf".into())).unwrap();
        assert_eq!(store.settings().unwrap().text("Mode"), "Tsf");
        assert!(write_action(&store, &Action::Mode("AutoSwitch".into())).is_err());
    }
}
