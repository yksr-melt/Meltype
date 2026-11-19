// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
use eframe::egui;
use meltype_core::{control, settings::Settings, storage::Store, Session};
use serde_json::{json, Value};
mod ai_setup;
#[cfg(any(windows, target_os = "linux"))]
mod analytics;
mod branding;
mod consent;
mod dialogs;
#[cfg(any(windows, target_os = "linux", target_os = "macos"))]
mod discord;
mod i18n;
mod lifecycle;
mod markdown;
mod metadata;
#[cfg(windows)]
mod now_playing;
#[cfg(windows)]
mod portable_keyboard;
mod release_notes;
mod rules;
#[cfg(windows)]
mod startup;
#[cfg(windows)]
mod tray;
#[cfg(windows)]
mod tray_control;
mod widgets;
#[cfg(windows)]
mod windows_services;

fn main() -> eframe::Result {
    #[cfg(windows)]
    if let Some(code) = startup::command_line(&std::env::args_os().skip(1).collect::<Vec<_>>()) {
        std::process::exit(code);
    }
    // 同意が済んでいないときは、設定画面を出して同意を待つ。
    // 起動の確認・導入などの引数は上の時点で終了している。
    let needs_consent = !consent::accepted(&Store::default_directory());
    // 打鍵数を Discord のステータスに出す (設定画面で入れた人だけ)。Discord が
    // 起動していなければ何もしない。
    #[cfg(any(windows, target_os = "linux", target_os = "macos"))]
    let _presence = discord::Presence::start(
        Store::default_directory(),
        Store::new(Store::default_directory()),
    );
    // Windows の再生中メディアを「なう」候補用に書き出す。
    #[cfg(windows)]
    let _now_playing = now_playing::NowPlaying::start(Store::new(Store::default_directory()));
    let resident = cfg!(windows)
        && std::env::args().any(|a| matches!(a.as_str(), "--tray" | "--portable-keyboard"));
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_icon(branding::icon())
            .with_app_id("meltype-flash")
            .with_inner_size([720., 700.])
            .with_min_inner_size([480., 400.]),
        ..Default::default()
    };
    let mut options = options;
    options.viewport = options.viewport.with_visible(!resident || needs_consent);
    let ui_language = Store::new(Store::default_directory())
        .settings()
        .map(|settings| settings.text("UiLanguage").to_owned())
        .unwrap_or_else(|_| "Auto".into());
    i18n::set(&ui_language);
    eframe::run_native(
        i18n::window_title(),
        options,
        Box::new(|cc| {
            widgets::configure_style(&cc.egui_ctx);
            let mut app = App::new();
            app.settings_visible = !resident || needs_consent;
            app.update_ui_fonts(&cc.egui_ctx);
            #[cfg(windows)]
            if app.resident {
                match windows_services::HiddenRepaint::start(cc) {
                    Ok(worker) => app.hidden_repaint = Some(worker),
                    Err(error) => app.status = error,
                }
            }
            Ok(Box::new(app))
        }),
    )
}
fn install_ui_fonts(ctx: &egui::Context) {
    let mut fonts = egui::FontDefinitions::default();
    let paths = std::env::var_os("MELTYPE_FONT")
        .map(std::path::PathBuf::from)
        .into_iter()
        .chain(
            [
                "C:/Windows/Fonts/YuGothR.ttc",
                "C:/Windows/Fonts/YuGothM.ttc",
                "C:/Windows/Fonts/meiryo.ttc",
                "/System/Library/Fonts/ヒラギノ角ゴシック W3.ttc",
                "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            ]
            .into_iter()
            .map(std::path::PathBuf::from),
        );
    for path in paths {
        if let Ok(bytes) = std::fs::read(&path) {
            let mut font = egui::FontData::from_owned(bytes);
            if path
                .file_name()
                .is_some_and(|name| name == "YuGothM.ttc" || name == "YuGothR.ttc")
            {
                font.index = 1;
            }
            fonts.font_data.insert("japanese".into(), font.into());
            fonts
                .families
                .entry(egui::FontFamily::Monospace)
                .or_default()
                .push("japanese".into());
            fonts
                .families
                .entry(egui::FontFamily::Proportional)
                .or_default()
                .insert(0, "japanese".into());
            ctx.set_fonts(fonts);
            break;
        }
    }
}

struct App {
    settings_visible: bool,
    ui_fonts_active: bool,
    store: Store,
    lifecycle: lifecycle::Lifecycle,
    ai_setup: ai_setup::AiSetup,
    dialogs: dialogs::Dialogs,
    settings: Settings,
    opened: Settings,
    discard: bool,
    closing: bool,
    consent_pending: bool,
    release_notes: bool,
    release_notes_from_update: bool,
    release_notes_english: bool,
    help: String,
    #[cfg(windows)]
    resident: bool,
    #[cfg(windows)]
    hidden_repaint: Option<windows_services::HiddenRepaint>,
    #[cfg(windows)]
    keyboard: Option<portable_keyboard::Keyboard>,
    #[cfg(windows)]
    tray: Option<tray::Tray>,
    page: usize,
    settings_category: Option<String>,
    title: String,
    status: String,
    filter: String,
    reading: String,
    word: String,
    snippet_name: String,
    snippet_text: String,
    restore_path: Option<std::path::PathBuf>,
    restore_prompt: bool,
    profile: String,
    profile_prompt: Option<bool>,
    confirm_remove: bool,
    forget_prompt: Option<&'static str>,
    uninstall_prompt: bool,
    test_raw: String,
    test_session: Session,
    result: Value,
    complex: BTreeEditor,
    show_all: bool,
    #[cfg(target_os = "linux")]
    runtime_services: bool,
    #[cfg(feature = "visual-test")]
    capture_frames: usize,
}
#[derive(Default)]
struct BTreeEditor(std::collections::BTreeMap<String, String>);
impl App {
    fn new() -> Self {
        let store = Store::new(Store::default_directory());
        #[cfg(not(windows))]
        {
            let app = Self::with_store(store);
            #[cfg(target_os = "linux")]
            let app = {
                let mut app = app;
                app.runtime_services = std::env::var_os("MELTYPE_FLASH_DATA_DIR").is_none()
                    && std::env::var_os("MELTYPE_RUST_DATA_DIR").is_none();
                app
            };
            app
        }
        #[cfg(windows)]
        {
            let mut app = Self::with_store(store.clone());
            app.resident = true;
            if std::env::args().any(|a| a == "--portable-keyboard") {
                if let Err(error) = store.change_settings(|s| s.set("Mode", json!("Keyboard"))) {
                    app.status = error.to_string();
                    return app;
                }
                app.settings = store.settings().unwrap_or_else(|_| app.settings.clone());
                app.opened = app.settings.clone();
                match portable_keyboard::Keyboard::start(store) {
                    Ok(keyboard) => {
                        keyboard.activate();
                        app.keyboard = Some(keyboard);
                        app.status = "ポータブル入力を開始しました。切替キーは設定画面で変更できます。登録済みIMEは英数にしてください。".into();
                    }
                    Err(error) => app.status = error,
                }
            }
            app
        }
    }
    #[cfg(windows)]
    fn show_settings(&mut self, ctx: &egui::Context) {
        self.settings_visible = true;
        ctx.send_viewport_cmd(egui::ViewportCommand::Visible(true));
    }

    fn update_ui_fonts(&mut self, ctx: &egui::Context) {
        // The welcome dialog also makes the root viewport visible.
        if self.lifecycle.welcome {
            self.settings_visible = true;
        }
        let needed = self.settings_visible
            || self.consent_pending
            || self.release_notes
            || self.lifecycle.about
            || self.lifecycle.updates
            || self.dialogs.log
            || self.dialogs.report
            || self.dialogs.corrections
            || (cfg!(feature = "visual-test") && std::env::var_os("MELTYPE_UI_CAPTURE").is_some());
        if needed != self.ui_fonts_active {
            if needed {
                install_ui_fonts(ctx);
            } else {
                ctx.set_fonts(egui::FontDefinitions::default());
            }
            self.ui_fonts_active = needed;
            ctx.request_repaint();
        }
    }

    fn with_store(store: Store) -> Self {
        let shortcut_warnings = std::fs::read_to_string(store.directory.join("config.json"))
            .map(|text| Settings::shortcut_migration_warnings(&text).join("\n"))
            .unwrap_or_default();
        let loaded = store.settings();
        let status = loaded
            .as_ref()
            .err()
            .map(ToString::to_string)
            .unwrap_or(shortcut_warnings);
        let settings = loaded.unwrap_or_default();
        crate::i18n::set(settings.text("UiLanguage"));
        // The input test must not write learning or settings to disk.
        let mut test_session = Session::default();
        test_session.settings = settings.clone();
        // 画面の採取試験は隔離したデータで行うため、同意画面とリリースノートでは止めない。
        let visual_capture =
            cfg!(feature = "visual-test") && std::env::var_os("MELTYPE_UI_CAPTURE").is_some();
        let consent_pending = !consent::accepted(&store.directory) && !visual_capture;
        #[cfg(any(windows, target_os = "linux"))]
        if !consent_pending {
            analytics::track("app_open");
        }
        // 新規導入では出さず、前の版から更新したときだけリリースノートを一度出す。
        let first_run = !store.directory.join("config.json").is_file();
        let release_notes_from_update = !consent_pending
            && !visual_capture
            && release_notes::take_update_notice(&store.directory, first_run);
        let release_notes = release_notes_from_update;
        let release_notes_english = crate::i18n::english();
        Self {
            settings_visible: true,
            ui_fonts_active: false,
            lifecycle: lifecycle::Lifecycle::new(&settings),
            ai_setup: ai_setup::AiSetup::default(),
            dialogs: dialogs::Dialogs::default(),
            store,
            opened: settings.clone(),
            settings,
            discard: false,
            closing: false,
            consent_pending,
            release_notes,
            release_notes_from_update,
            release_notes_english,
            help: "Meltype-FLASH を有効にする".into(),
            #[cfg(windows)]
            resident: std::env::args()
                .any(|a| matches!(a.as_str(), "--tray" | "--portable-keyboard")),
            #[cfg(windows)]
            keyboard: None,
            #[cfg(windows)]
            hidden_repaint: None,
            #[cfg(windows)]
            tray: None,
            page: 0,
            settings_category: None,
            title: crate::i18n::window_title().to_owned(),
            status,
            filter: String::new(),
            reading: String::new(),
            word: String::new(),
            snippet_name: String::new(),
            snippet_text: String::new(),
            restore_path: None,
            restore_prompt: false,
            profile: String::new(),
            profile_prompt: None,
            confirm_remove: false,
            forget_prompt: None,
            uninstall_prompt: false,
            test_raw: String::new(),
            test_session,
            result: Value::Null,
            complex: BTreeEditor::default(),
            show_all: false,
            #[cfg(target_os = "linux")]
            runtime_services: false,
            #[cfg(feature = "visual-test")]
            capture_frames: 0,
        }
    }
    fn action(&mut self, request: Value) {
        let operation = request["op"].as_str().unwrap_or("");
        let name = request["name"].as_str().unwrap_or("");
        let previous_shortcuts = self.settings.toggle_shortcuts();
        let draft = match operation {
            "settings_set" => Some(self.settings.set(name, request["value"].clone())),
            "profile_add" => Some(
                self.settings
                    .add_profile(name)
                    .and_then(|()| self.settings.switch_profile(name)),
            ),
            "profile_switch" => Some(self.settings.switch_profile(name)),
            "profile_delete" => Some(self.settings.delete_profile(name)),
            "profile_rename" => Some(self.settings.rename_profile(
                self.settings.text("ActiveProfile").to_owned().as_str(),
                name,
            )),
            "profile_remove" => Some(self.settings.remove_profile(name)),
            _ => None,
        };
        if let Some(result) = draft {
            match result {
                Ok(()) => {
                    self.status = "変更は「OK」または閉じるボタンで保存します。".into();
                    let shortcuts = self.settings.toggle_shortcuts();
                    if name != "ToggleShortcuts" {
                        for shortcut in previous_shortcuts
                            .iter()
                            .filter(|shortcut| !shortcuts.contains(shortcut))
                        {
                            if let Err(reason) = shortcut.validate(&self.settings, &[]) {
                                self.status.push_str(&format!(
                                    "\n切替キー「{}」を除外しました: {reason}",
                                    shortcut.label()
                                ));
                            }
                        }
                    }
                    self.test_session.settings = self.settings.clone();
                }
                Err(error) => self.status = error,
            }
            return;
        }
        match control::execute(&self.store, &request) {
            Ok(result) => {
                self.result = result;
                self.status = if let Some(count) = self.result["imported"].as_u64() {
                    format!("{count} 件取り込みました。")
                } else if operation.ends_with("_list") {
                    "読み込みました。".into()
                } else if operation == "backup_inspect" {
                    "バックアップの内容を読み込みました。".into()
                } else {
                    "保存しました。".into()
                };
            }
            Err(error) => {
                self.result = Value::Null;
                self.status = error.to_string();
            }
        }
    }
    fn save(&mut self) -> bool {
        let shortcuts = self.settings.toggle_shortcuts();
        for (index, shortcut) in shortcuts.iter().enumerate() {
            if let Err(reason) = shortcut.validate(&self.settings, &shortcuts[..index]) {
                self.status = format!("切替キー「{}」: {reason}", shortcut.label());
                return false;
            }
        }
        if self.settings.0 == self.opened.0 {
            return self.sync_platform_updates();
        }
        match control::execute(
            &self.store,
            &json!({"op":"settings_merge", "opened":self.opened, "edited":self.settings}),
        ) {
            Ok(_) => {
                self.opened = self.settings.clone();
                self.sync_platform_updates()
            }
            Err(error) => {
                self.status = format!("保存できません: {error}");
                false
            }
        }
    }
    fn sync_platform_updates(&mut self) -> bool {
        #[cfg(target_os = "linux")]
        if self.runtime_services {
            let result = (|| -> Result<(), String> {
                self.store
                    .change_settings(|s| {
                        s.set("AutoUpdate", json!(self.settings.boolean("AutoUpdate")))
                    })
                    .map_err(|e| e.to_string())?;
                let root = &self.store.directory;
                let service = root.join("background-updates.py");
                meltype_core::storage::atomic_write(
                    &service,
                    include_bytes!("../../linux-background-updates.py"),
                )
                .map_err(|e| e.to_string())?;
                meltype_core::storage::atomic_write(
                    &root.join("update-linux.py"),
                    include_bytes!("../../update-linux.py"),
                )
                .map_err(|e| e.to_string())?;
                let exe = std::env::current_exe().map_err(|e| e.to_string())?;
                let output = std::process::Command::new("python3")
                    .arg(service)
                    .arg(if self.settings.boolean("AutoUpdate") {
                        "enable"
                    } else {
                        "disable"
                    })
                    .arg(root)
                    .arg(exe.parent().ok_or("アプリの場所が不明です")?)
                    .arg(env!("CARGO_PKG_VERSION"))
                    .output()
                    .map_err(|e| e.to_string())?;
                if !output.status.success() {
                    return Err(String::from_utf8_lossy(&output.stderr).trim().to_owned());
                }
                Ok(())
            })();
            if let Err(error) = result {
                self.status =
                    format!("設定は保存しましたが、自動更新の登録に失敗しました: {error}");
                return false;
            }
        }
        true
    }
    fn finish(&mut self, ctx: &egui::Context, discard: bool) {
        if !discard && !self.save() {
            ctx.send_viewport_cmd(egui::ViewportCommand::CancelClose);
            return;
        }
        if discard {
            match self.store.settings() {
                Ok(settings) => {
                    self.opened = settings.clone();
                    self.settings = settings.clone();
                    self.test_session.settings = settings;
                    self.complex.0.clear();
                }
                Err(error) => self.status = error.to_string(),
            }
        }
        #[cfg(windows)]
        if self.resident {
            ctx.send_viewport_cmd(egui::ViewportCommand::CancelClose);
            self.settings_visible = false;
            ctx.send_viewport_cmd(egui::ViewportCommand::Visible(false));
            self.discard = false;
            return;
        }
        self.discard = discard;
        self.closing = true;
        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
    }
    #[cfg(windows)]
    fn maintenance_quit(&mut self, ctx: &egui::Context) {
        if self.settings.0 != self.opened.0 {
            self.status = "更新・削除のための終了要求があります。未保存の設定を保存または取り消してください。".into();
            self.show_settings(ctx);
            return;
        }
        self.closing = true;
        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
    }
    fn open_release_notes(&mut self) {
        self.release_notes = true;
        self.release_notes_from_update = false;
        release_notes::mark_shown(&self.store.directory);
    }
    fn release_notes_window(&mut self, ctx: &egui::Context) -> bool {
        let version = release_notes::VERSION;
        let title = if self.release_notes_english {
            "Meltype-FLASH release notes"
        } else {
            "Meltype-FLASH リリースノート"
        };
        crate::dialogs::window(ctx, "flash-release-notes", title, [720., 620.], |ui| {
            let mut closed = false;
            ui.horizontal(|ui| {
                ui.heading(if self.release_notes_english {
                    "Release notes"
                } else {
                    "リリースノート"
                });
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if ui
                        .selectable_label(self.release_notes_english, "English")
                        .clicked()
                    {
                        self.release_notes_english = true;
                    }
                    if ui
                        .selectable_label(!self.release_notes_english, "日本語")
                        .clicked()
                    {
                        self.release_notes_english = false;
                    }
                });
            });
            if self.release_notes_from_update {
                ui.label(if self.release_notes_english {
                    format!("Updated to {version}.")
                } else {
                    format!("{version} に更新しました。")
                });
            }
            ui.separator();
            egui::ScrollArea::vertical()
                .auto_shrink([false, false])
                .show(ui, |ui| {
                    match release_notes::notes(version, self.release_notes_english) {
                        Some(notes) => markdown::show(ui, notes),
                        None => {
                            ui.label(if self.release_notes_english {
                                "The release notes for this version are on GitHub."
                            } else {
                                "この版のリリースノートは GitHub のリリースページで見られます。"
                            });
                        }
                    }
                });
            ui.separator();
            ui.horizontal(|ui| {
                if ui
                    .button(if self.release_notes_english {
                        "Close"
                    } else {
                        "閉じる"
                    })
                    .clicked()
                {
                    closed = true;
                }
                ui.hyperlink_to(
                    if self.release_notes_english {
                        "View on GitHub"
                    } else {
                        "GitHub で見る"
                    },
                    release_notes::release_url(version),
                );
                ui.hyperlink_to(
                    if self.release_notes_english {
                        "Past release notes"
                    } else {
                        "過去のリリースノート"
                    },
                    crate::lifecycle::RELEASES,
                );
            });
            closed
        })
    }
    fn consent(&mut self, ctx: &egui::Context) {
        egui::CentralPanel::default().show(ctx, |ui| {
            egui::ScrollArea::vertical().show(ui, |ui| {
                ui.add_space(20.);
                ui.heading("はじめる前に");
                ui.add_space(10.);
                ui.label(consent::text());
                ui.add_space(18.);
                ui.horizontal(|ui| {
                    if ui
                        .add_sized([200., 36.], egui::Button::new("同意してはじめる"))
                        .clicked()
                    {
                        match consent::accept(&self.store.directory) {
                            Ok(()) => {
                                self.consent_pending = false;
                                self.status = "同意を保存しました。".into();
                                #[cfg(any(windows, target_os = "linux"))]
                                analytics::track("app_open");
                                // 新規導入の初回同意ではリリースノートを出さない。
                                release_notes::take_update_notice(&self.store.directory, true);
                            }
                            Err(error) => self.status = format!("同意を保存できません: {error}"),
                        }
                    }
                    if ui
                        .add_sized([150., 36.], egui::Button::new("同意しない (終了)"))
                        .clicked()
                    {
                        self.closing = true;
                        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                    }
                });
                if !self.status.is_empty() {
                    ui.add_space(10.);
                    ui.colored_label(egui::Color32::from_rgb(180, 40, 40), &self.status);
                }
            });
        });
    }
    #[cfg(windows)]
    fn update_tray(&mut self, ctx: &egui::Context) {
        if !self.resident {
            return;
        }
        ctx.request_repaint_after(std::time::Duration::from_millis(500));
        let settings = match self.store.settings() {
            Ok(s) => s,
            Err(e) => {
                self.status = e.to_string();
                return;
            }
        };
        if settings.text("Mode") == "Keyboard" && self.keyboard.is_none() {
            match portable_keyboard::Keyboard::start(self.store.clone()) {
                Ok(keyboard) => self.keyboard = Some(keyboard),
                Err(error) => self.status = error,
            }
        }
        if let Some(keyboard) = &mut self.keyboard {
            keyboard.configure(&settings);
        }
        if self.tray.is_none() {
            match tray::Tray::new(ctx, &settings, &self.store) {
                Ok(tray) => self.tray = Some(tray),
                Err(error) => {
                    self.status = error;
                    self.resident = false;
                    self.show_settings(ctx);
                    return;
                }
            }
        }
        let actions = self.tray.as_mut().unwrap().poll();
        for action in actions {
            match action {
                tray::Action::Page(page) => {
                    if page == 6 {
                        self.dialogs.log = true;
                        continue;
                    }
                    // Keep an open draft; otherwise start a fresh settings window.
                    if ctx.input(|i| i.viewport().minimized.unwrap_or(false))
                        || self.settings.0 == self.opened.0
                    {
                        self.settings = settings.clone();
                        self.opened = settings.clone();
                        self.test_session.settings = settings.clone();
                    }
                    self.page = page;
                    self.discard = false;
                    self.show_settings(ctx);
                    ctx.send_viewport_cmd(egui::ViewportCommand::Minimized(false));
                    ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
                }
                tray::Action::KeyCount => {
                    // 本家と同じく、トレイから打鍵数の画面を開く。
                    if ctx.input(|i| i.viewport().minimized.unwrap_or(false))
                        || self.settings.0 == self.opened.0
                    {
                        self.settings = settings.clone();
                        self.opened = settings.clone();
                        self.test_session.settings = settings.clone();
                    }
                    self.page = 0;
                    self.settings_category = Some("13. 打鍵数".into());
                    self.filter.clear();
                    self.discard = false;
                    self.show_settings(ctx);
                    ctx.send_viewport_cmd(egui::ViewportCommand::Minimized(false));
                    ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
                }
                tray::Action::Report => self.dialogs.open_report(&self.store),
                tray::Action::Corrections => self.dialogs.corrections = true,
                tray::Action::Backup(restore) => {
                    if restore {
                        self.open_backup_dialog();
                        if self.restore_prompt {
                            self.show_settings(ctx);
                            ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
                        }
                    } else {
                        self.create_backup_dialog();
                    }
                }
                tray::Action::Forget(scope) => {
                    self.forget_prompt = Some(scope);
                    self.show_settings(ctx);
                    ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
                }
                tray::Action::Uninstall => {
                    self.uninstall_prompt = true;
                    self.show_settings(ctx);
                    ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
                }
                tray::Action::CheckUpdates => {
                    self.lifecycle.updates = true;
                    self.lifecycle.check_updates();
                }
                tray::Action::ReleaseNotes => {
                    self.open_release_notes();
                    self.show_settings(ctx);
                    ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
                }
                tray::Action::About => self.lifecycle.about = true,
                tray::Action::Help => {
                    self.lifecycle.welcome = true;
                    self.show_settings(ctx);
                    ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
                }
                tray::Action::Folder => {
                    if let Err(e) = std::fs::create_dir_all(&self.store.directory).and_then(|()| {
                        std::process::Command::new("explorer.exe")
                            .arg(&self.store.directory)
                            .spawn()
                            .map(|_| ())
                    }) {
                        self.status = e.to_string();
                    }
                }
                tray::Action::MaintenanceQuit => {
                    self.maintenance_quit(ctx);
                }
                tray::Action::Quit => {
                    if self.save() {
                        if let Err(error) = self
                            .store
                            .change_settings(|s| s.set("Enabled", json!(false)))
                        {
                            self.status = error.to_string();
                            continue;
                        }
                        self.closing = true;
                        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                    }
                }
                tray::Action::Status(message) => self.status = message,
                tray::Action::Error(message) => {
                    self.status = message;
                    self.show_settings(ctx);
                    ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
                }
                action => {
                    if let Err(error) = tray::write_action(&self.store, &action) {
                        self.status = error;
                    }
                }
            }
        }
        if let Ok(settings) = self.store.settings() {
            if let Some(tray) = &mut self.tray {
                if let Err(error) = tray.sync(&settings) {
                    self.status = error;
                }
                tray.input_status(
                    &settings,
                    self.keyboard.as_ref().is_some_and(|k| k.enabled()),
                );
            }
        }
    }
    fn navigation(&mut self, ui: &mut egui::Ui) {
        ui.horizontal_wrapped(|ui| {
            ui.spacing_mut().item_spacing.x = 14.;
            for (page, title) in [
                "設定",
                "ユーザー辞書",
                "定型文",
                "学習した語",
                "バックアップ",
                "入力テスト",
                "打鍵数",
            ]
            .iter()
            .enumerate()
            {
                if ui
                    .selectable_value(&mut self.page, page, i18n::t(title))
                    .clicked()
                {
                    ui.close_menu();
                }
            }
        });
    }
    fn settings(&mut self, ui: &mut egui::Ui) {
        ui.add_space(4.);
        ui.horizontal_wrapped(|ui| {
            ui.spacing_mut().item_spacing.x = 8.;
            ui.spacing_mut().interact_size.y = 25.;
            ui.label(i18n::t("プロファイル:"));
            let active = self.settings.text("ActiveProfile").to_owned();
            let mut selected = active.clone();
            egui::ComboBox::from_id_salt("active_profile")
                .width(200.)
                .selected_text(&active)
                .show_ui(ui, |ui| {
                    if let Some(profiles) = self.settings.0["Profiles"].as_array() {
                        for profile in profiles {
                            if let Some(name) = profile["Name"].as_str() {
                                ui.selectable_value(&mut selected, name.to_owned(), name);
                            }
                        }
                    }
                });
            if selected != active {
                self.action(json!({"op":"profile_switch","name":selected}));
            }
            if ui
                .add_sized([72., 25.], egui::Button::new(i18n::t("新規...")))
                .clicked()
            {
                self.profile.clear();
                self.profile_prompt = Some(false);
            }
            if ui
                .add_sized([88., 25.], egui::Button::new(i18n::t("名前を変更...")))
                .clicked()
            {
                self.profile = active;
                self.profile_prompt = Some(true);
            }
            if ui
                .add_enabled(
                    self.settings.0["Profiles"]
                        .as_array()
                        .is_some_and(|p| p.len() > 1),
                    egui::Button::new(i18n::t("削除")).min_size(egui::vec2(72., 25.)),
                )
                .clicked()
            {
                self.confirm_remove = true;
            }
            if ui
                .add_sized([72., 25.], egui::Button::new(i18n::t("書き出す...")))
                .clicked()
            {
                if let Some(path) = rfd::FileDialog::new()
                    .set_title("プロファイルを書き出す")
                    .add_filter("Meltype プロファイル", &["json"])
                    .set_file_name("Meltype-profile.json")
                    .save_file()
                {
                    match serde_json::to_vec_pretty(&self.settings.export_profile())
                        .map_err(std::io::Error::other)
                        .and_then(|bytes| meltype_core::storage::atomic_write(&path, &bytes))
                    {
                        Ok(()) => self.status =
                            "プロファイルを書き出しました。アプリ別設定のプロセス名も含まれます。"
                                .into(),
                        Err(error) => self.status = error.to_string(),
                    }
                }
            }
            if ui
                .add_sized([72., 25.], egui::Button::new(i18n::t("読み込む...")))
                .clicked()
            {
                if let Some(path) = rfd::FileDialog::new()
                    .set_title("プロファイルを読み込む")
                    .add_filter("Meltype プロファイル", &["json"])
                    .pick_file()
                {
                    match std::fs::read_to_string(path)
                        .map_err(|e| e.to_string())
                        .and_then(|text| self.settings.import_profile(&text))
                    {
                        Ok(name) => {
                            self.status =
                                format!("「{name}」を読み込みました。「OK」で保存します。");
                            self.test_session.settings = self.settings.clone();
                        }
                        Err(error) => self.status = error,
                    }
                }
            }
        });
        ui.add_space(8.);
        ui.horizontal_wrapped(|ui| {
            let key = "SettingsWheelChangesValue";
            let label = "マウスホイールで値を変更";
            let mut enabled = self.settings.boolean(key);
            ui.label(i18n::t(label));
            if widgets::toggle(ui, &mut enabled, i18n::t(label)).changed() {
                self.action(json!({"op":"settings_set","name":key,"value":enabled}));
            }
        });
        if self.show_all {
            ui.horizontal(|ui| {
                ui.label(i18n::t("検索"));
                ui.text_edit_singleline(&mut self.filter);
            });
        }
        ui.add_space(8.);
        let mut fields: Vec<_> = metadata::fields()
            .iter()
            .filter_map(|f| {
                let key = f["key"].as_str()?;
                if cfg!(any(windows, target_os = "linux", target_os = "macos"))
                    && [
                        "AiModel",
                        "UseZenzai",
                        "ZenzaiServerPath",
                        "ZenzaiModelPath",
                        "UseShunti",
                        "ShuntiHelperPath",
                    ]
                    .contains(&key)
                {
                    return None;
                }
                let value = self.settings.0.get(key)?;
                let filter = self.filter.to_lowercase();
                ((self.show_all || visible_by_default(key))
                    && (key.to_lowercase().contains(&filter)
                        || metadata::label(key).contains(&self.filter)
                        || metadata::description(key).contains(&self.filter)))
                .then(|| {
                    (
                        key.to_owned(),
                        value.clone(),
                        ui_category(key, f["category"].as_str().unwrap_or("")).to_owned(),
                    )
                })
            })
            .collect();
        #[cfg(any(windows, target_os = "linux", target_os = "macos"))]
        if self.filter.is_empty()
            || [
                "ai",
                "zenz",
                "zenzai",
                "shunti",
                "パス",
                "モデル",
                "インストール",
            ]
            .iter()
            .any(|word| self.filter.to_lowercase().contains(word))
        {
            fields.push(("ai-packages".into(), Value::Null, "4. 変換".into()));
            fields.push(("keycount-discord".into(), Value::Null, "8. 表示".into()));
            fields.push(("keycount".into(), Value::Null, "13. 打鍵数".into()));
        }
        // 本家と同じ数字の順に並べる。文字列の順だと 10. アプリ が 2. 入力モード より前になる。
        fields.sort_by(|a, b| category_order(&a.2).cmp(&category_order(&b.2)));
        let mut categories: Vec<String> = Vec::new();
        for (_, _, group) in &fields {
            if categories.last() != Some(group) {
                categories.push(group.clone());
            }
        }
        let available_width = ui.available_width();
        let content_width = (available_width - 166.).max(320.);
        ui.horizontal_top(|ui| {
            egui::Frame::NONE
                .fill(egui::Color32::from_gray(246))
                .inner_margin(egui::Margin::symmetric(6, 8))
                .show(ui, |ui| {
                    ui.set_width(146.);
                    egui::ScrollArea::vertical()
                        .id_salt("settings_categories")
                        .auto_shrink([false, false])
                        .show(ui, |ui| {
                            ui.vertical(|ui| {
                                if ui
                                    .selectable_label(
                                        self.settings_category.is_none(),
                                        i18n::t("すべて"),
                                    )
                                    .clicked()
                                {
                                    self.settings_category = None;
                                }
                                for category in &categories {
                                    let label = category
                                        .split_once(". ")
                                        .map_or(category.as_str(), |(_, label)| label);
                                    let selected = self.settings_category.as_deref()
                                        == Some(category.as_str());
                                    if ui
                                        .selectable_label(selected, i18n::category(label))
                                        .clicked()
                                    {
                                        self.settings_category = Some(category.clone());
                                    }
                                }
                            });
                        });
                });
            ui.separator();
            ui.allocate_ui_with_layout(
                egui::vec2(content_width, ui.available_height()),
                egui::Layout::top_down(egui::Align::Min),
                |ui| {
                    egui::ScrollArea::vertical()
                    .id_salt("settings_body")
                    .auto_shrink([false, false])
                    .show(ui, |ui| {
                        ui.set_width((content_width - 18.).max(220.));
                for group_fields in fields.chunk_by(|a, b| a.2 == b.2) {
                    if self
                        .settings_category
                        .as_deref()
                        .is_some_and(|selected| group_fields[0].2 != selected)
                    {
                        continue;
                    }
                    let group = &group_fields[0].2;
                    let title = group
                        .split_once(". ")
                        .map_or(group.as_str(), |(_, label)| label);
                    let title = i18n::category(title);
                    let frame = egui::Frame::NONE.inner_margin(egui::Margin {
                        left: 12,
                        right: 12,
                        top: 29,
                        bottom: 10,
                    });
                    let group_response = frame.show(ui, |ui| {
                        ui.set_min_width(ui.available_width());
                        for (key, value, _) in group_fields {
                            if key == "keycount-discord" {
                                let mut count = meltype_core::keycount::load(&self.store.directory);
                                let mut changed = false;
                                ui.horizontal(|ui| {
                                    ui.label("Discord のステータスに表示する");
                                    changed |= widgets::toggle(
                                        ui,
                                        &mut count.discord,
                                        "Discord のステータスに表示する",
                                    )
                                    .on_hover_text("今日・合計・1 分あたりの回数だけを送ります。打った文字・候補・入力先のアプリ名は送りません。")
                                    .changed();
                                });
                                if changed {
                                    if let Err(error) = meltype_core::keycount::save_settings(
                                        &self.store.directory,
                                        &count,
                                    ) {
                                        self.status = error.to_string();
                                    }
                                }
                                continue;
                            }
                            if key == "keycount" {
                                self.keycount(ui);
                                continue;
                            }
                            #[cfg(any(windows, target_os = "linux", target_os = "macos"))]
                            if key == "ai-packages" {
                                self.ai_settings(ui);
                                continue;
                            }
                            let mut value = value.clone();
                            ui.push_id(key, |ui| {
                                if key == "AppRules" || key == "AppKinds" {
                                    let label = ui
                                        .add_sized(
                                            [ui.available_width() * 0.55, 28.],
                                            egui::Label::new(metadata::label(key)).wrap(),
                                        )
                                        .on_hover_text(metadata::description(key));
                                    if label.hovered() {
                                        self.help = metadata::description(key).into();
                                    }
                                    let changed = egui::ScrollArea::horizontal()
                                        .id_salt(("rules-scroll", key))
                                        .show(ui, |ui| {
                                            rules::editor(ui, key, &mut value, &self.settings)
                                        })
                                        .inner;
                                    if changed {
                                        self.action(
                                            json!({"op":"settings_set","name":key,"value":value}),
                                        );
                                    }
                                    return;
                                }
                                if key == "ToggleShortcuts" {
                                    ui.label(metadata::label(key)).on_hover_text(metadata::description(key));
                                    if cfg!(target_os = "linux") { ui.weak("IBusが選択されている入力欄で有効です。システム全体の切替はデスクトップ設定を使います。"); } else if !cfg!(windows) { ui.weak("この切替キーはこのOSでは編集できません。"); }
                                    let draft = self.complex.0.entry("toggle-shortcut-new".into()).or_default();
                                    let edit = ui.add_enabled_ui(cfg!(any(windows, target_os = "linux")), |ui| {
                                        toggle_shortcuts_editor(ui, &mut value, &self.settings, draft)
                                    }).inner;
                                    match edit {
                                        Ok(true) => self.action(json!({"op":"settings_set","name":key,"value":value})),
                                        Err(error) => self.status = error,
                                        _ => {}
                                    }
                                    return;
                                }

                                if key == "Mode" && cfg!(target_os = "linux") {
                                    ui.label("入力方式: Linux IBus");
                                    ui.weak("入力ソースはデスクトップの日本語入力設定で切り替えます。");
                                    return;
                                }
                                if key == "AutoUpdate" {
                                    let mut automatic = value.as_bool().unwrap_or(false);
                                    let prior = automatic;
                                    ui.horizontal_wrapped(|ui| {
                                        ui.label(i18n::t("アップデート方法"));
                                        egui::ComboBox::from_id_salt("update-method")
                                            .selected_text(if automatic { i18n::t("自動") } else { i18n::t("手動") })
                                            .show_ui(ui, |ui| {
                                                ui.selectable_value(&mut automatic, false, i18n::t("手動"));
                                                ui.selectable_value(&mut automatic, true, i18n::t("自動"));
                                            });
                                        if ui.button(i18n::t("今アップデートを確認")).clicked() {
                                            self.lifecycle.updates = true;
                                            self.lifecycle.check_updates();
                                        }
                                        if ui.button(i18n::t("リリースノートを見る")).clicked() {
                                            self.open_release_notes();
                                        }
                                    });
                                    ui.weak(metadata::description(key));
                                    if automatic != prior {
                                        self.action(json!({"op":"settings_set", "name":key, "value":automatic}));
                                    }
                                    return;
                                }
                                let limitation =
                                    if !cfg!(any(windows, target_os = "linux")) && ["UseZenzai", "ZenzaiServerPath", "ZenzaiModelPath"].contains(&key.as_str()) {
                                        Some("ZenzaiはWindowsのIME方式で使用できます。")
                                    } else if cfg!(windows) && self.settings.text("Mode") == "Keyboard" {
                                        keyboard_limitation(key)
                                    } else {
                                        None
                                    };
                                if let Some(reason) = limitation {
                                    ui.weak(reason);
                                    ui.disable();
                                }
                                let row_width = ui.available_width();
                                let label_width = (row_width * 0.5 + 4.).min(320.);
                                let control_width = (row_width - label_width - 8.).max(120.);
                                let row = ui.horizontal(|ui| {
                                    let label = ui
                                        .allocate_ui_with_layout(
                                            egui::vec2(label_width, 29.),
                                            egui::Layout::left_to_right(egui::Align::Center),
                                            |ui| {
                                                ui.set_min_width(label_width);
                                                ui.label(metadata::label(key))
                                                    .on_hover_text(metadata::description(key))
                                            },
                                        )
                                        .inner;
                                    if label.hovered() {
                                        self.help = metadata::description(key).into();
                                    }
                                    match &mut value {
                                        Value::Bool(v) => {
                                            if widgets::toggle(ui, v, metadata::label(key))
                                                .on_hover_text(metadata::description(key))
                                                .changed()
                                            {
                                                self.action(
                                            json!({"op":"settings_set","name":key,"value":value}),
                                        );
                                            }
                                        }
                                        Value::Number(n) => {
                                            let mut number = n.as_i64().unwrap_or(0);
                                            let response =
                                                ui.add(egui::DragValue::new(&mut number));
                                            let step = widgets::wheel_step(
                                                ui,
                                                &response,
                                                self.settings.boolean("SettingsWheelChangesValue"),
                                            );
                                            number = number.saturating_add(step);
                                            if response.changed() || step != 0 {
                                                self.action(
                                            json!({"op":"settings_set","name":key,"value":number}),
                                        );
                                            }
                                        }
                                        Value::String(text) => {
                                            if let Some(choices) = choices(key) {
                                                let old = text.clone();
                                                let combo = egui::ComboBox::from_id_salt(key)
                                                    .width(control_width)
                                                    .selected_text(metadata::choice(key, text))
                                                    .show_ui(ui, |ui| {
                                                        for choice in choices {
                                                            ui.selectable_value(
                                                                text,
                                                                (*choice).to_owned(),
                                                                metadata::choice(key, choice),
                                                            );
                                                        }
                                                    });
                                                let step = widgets::wheel_step(
                                                    ui,
                                                    &combo.response,
                                                    self.settings
                                                        .boolean("SettingsWheelChangesValue"),
                                                );
                                                if step != 0 {
                                                    if let Some(index) = choices
                                                        .iter()
                                                        .position(|choice| *choice == text.as_str())
                                                    {
                                                        *text = choices[(index as i64 - step)
                                                            .clamp(0, choices.len() as i64 - 1)
                                                            as usize]
                                                            .to_string();
                                                    }
                                                }
                                                if *text != old {
                                                    self.action(
                                            json!({"op":"settings_set","name":key,"value":text}),
                                        );
                                                }
                                            } else {
                                                let response = ui.add(egui::TextEdit::singleline(text).desired_width(control_width));
                                                if response.changed() {
                                                    self.settings
                                                        .0
                                                        .insert(key.clone(), json!(text));
                                                }
                                            }
                                        }
                                        _ => {
                                            let text =
                                                self.complex.0.entry(key.clone()).or_insert_with(
                                                    || {
                                                        serde_json::to_string(&value)
                                                            .unwrap_or_default()
                                                    },
                                                );
                                            ui.text_edit_singleline(text);
                                            if ui.button(i18n::t("保存 JSON")).clicked() {
                                                match serde_json::from_str::<Value>(text) {
                                        Ok(value) => self.action(
                                            json!({"op":"settings_set","name":key,"value":value}),
                                        ),
                                        Err(e) => self.status = e.to_string(),
                                    }
                                            }
                                        }
                                    }
                                });
                                if row.response.hovered() {
                                    self.help = if supported(key) {
                                        metadata::description(key).to_owned()
                                    } else {
                                        format!(
                                            "{}\nRust版では設定の保存のみ対応しています。",
                                            metadata::description(key)
                                        )
                                    };
                                }
                                row.response.on_hover_text(&self.help);
                            });
                        }
                    });
                    widgets::group_border(ui, group_response.response.rect, title);
                    ui.add_space(14.);
                }
                // Retain the original form's trailing space below the last row.
                ui.add_space(16.);
                    });
                },
            );
        });
    }
    #[cfg(any(windows, target_os = "linux", target_os = "macos"))]
    fn ai_settings(&mut self, ui: &mut egui::Ui) {
        use meltype_core::{ai_catalog, ai_model::AiModel};
        let mut selected = AiModel::selected(&self.settings);
        let previous = selected;
        ui.add_enabled_ui(!self.ai_setup.locked(), |ui| {
            ui.horizontal(|ui| {
                ui.label(i18n::t("AIモデル"));
                egui::ComboBox::from_id_salt("ai-model")
                    .selected_text(selected.label())
                    .show_ui(ui, |ui| {
                        for model in AiModel::ALL {
                            ui.selectable_value(&mut selected, model, model.label());
                        }
                    });
            });
        });
        if selected != previous {
            self.ai_setup.reset_status();
            selected.select(&mut self.settings, false);
        }
        let Some(spec) = ai_catalog::selected(&self.settings, selected) else {
            ui.weak("AI変換を使用しません。導入済みのモデルは保持します。");
            return;
        };
        let mut variant = spec.id.clone();
        ui.add_enabled_ui(!self.ai_setup.locked(), |ui| {
            ui.horizontal(|ui| {
                ui.label("バージョン・モデルサイズ・量子化");
                egui::ComboBox::from_id_salt("ai-variant")
                    .selected_text(&spec.label)
                    .show_ui(ui, |ui| {
                        for m in ai_catalog::models()
                            .iter()
                            .filter(|m| m.family == selected.value())
                        {
                            ui.selectable_value(&mut variant, m.id.clone(), &m.label);
                        }
                    });
            });
        });
        if variant != spec.id {
            self.settings
                .set(ai_catalog::key(selected), json!(variant))
                .unwrap();
            selected.select(&mut self.settings, false);
        }
        let spec = ai_catalog::selected(&self.settings, selected).unwrap();
        let ready = self.ai_setup.status(&self.store.directory, spec);
        ui.label(format!(
            "モデルデータ {:.1} MB",
            spec.files.iter().map(|f| f.size).sum::<u64>() as f64 / 1_000_000.
        ));
        ui.weak(&spec.license);
        ui.hyperlink_to("モデルの説明・配布元", &spec.source);
        if !spec.available {
            ui.label("この配布版はライセンスを確認中のため、インストールできません。");
        }
        let checking = self.ai_setup.checking();
        if ui
            .add_enabled(
                !self.ai_setup.locked() && !ready && !checking && spec.available,
                egui::Button::new(if ready {
                    "インストール済み"
                } else if checking {
                    "インストール状態を確認中…"
                } else {
                    "このモデルをインストール"
                }),
            )
            .clicked()
        {
            self.ai_setup.request_model(spec);
        }
        if self.ai_setup.busy() {
            ui.spinner();
            ui.label("選択したAI環境を準備しています…");
        } else if ready {
            ui.weak("インストール済みです。「OK」で選択を保存します。");
        } else {
            ui.weak("専用の実行環境と選択したモデルを別ダウンロードします。パス入力は不要です。");
        }
        ui.collapsing("手動でパスを指定（任意）", |ui| {
            let paths: &[(&str, &str)] = if selected == AiModel::Shunti {
                &[("ShuntiHelperPath", "shuntiの実行ファイル")]
            } else {
                &[
                    ("ZenzaiServerPath", "llama-serverの実行ファイル"),
                    ("ZenzaiModelPath", "選択した版のGGUFモデル"),
                ]
            };
            for (key, label) in paths {
                ui.label(*label);
                let mut text = self.settings.text(key).to_owned();
                if ui.text_edit_singleline(&mut text).changed() {
                    self.action(json!({"op":"settings_set","name":key,"value":text}));
                }
            }
            if !self.ai_setup.locked() && ui.button("指定したパスを使う").clicked() {
                if paths
                    .iter()
                    .all(|(key, _)| std::path::Path::new(self.settings.text(key)).is_file())
                {
                    selected.select(&mut self.settings, true);
                } else {
                    self.status =
                        "指定したファイルが見つかりません。インストールボタンで自動設定できます。"
                            .into();
                }
            }
        });
        self.test_session.settings = self.settings.clone();
    }
    fn dictionary(&mut self, ui: &mut egui::Ui) {
        ui.heading("Meltype-FLASH ユーザー辞書");
        let reading_finished = ui
            .horizontal_wrapped(|ui| {
                ui.label("読み (ローマ字・ひらがな)");
                ui.add(egui::TextEdit::singleline(&mut self.reading).desired_width(200.))
                    .lost_focus()
            })
            .inner;
        let reading = meltype_core::user_dictionary::hiragana(&meltype_core::romaji::hiragana(
            self.reading.trim(),
            true,
        ));
        let mut candidates = meltype_core::dictionary::Dictionary::bundled()
            .candidates(&reading, self.settings.text("TimeZone"));
        let mut remove_reading = reading.clone();
        if let Ok(state) = self.store.load() {
            if state
                .preferences
                .user_dictionary
                .contains_key(self.reading.trim())
            {
                remove_reading = self.reading.trim().to_owned();
            }
            if let Some(words) = state.preferences.user_dictionary.get(&reading) {
                for word in words.iter().rev() {
                    candidates.retain(|candidate| candidate != word);
                    candidates.insert(0, word.clone());
                }
            }
        }
        if reading_finished && !reading.is_empty() && self.word.is_empty() {
            self.word = candidates.first().cloned().unwrap_or_default();
        }
        if self.reading.chars().any(|c| c.is_ascii_alphabetic()) {
            ui.label(format!("→ {reading}"));
        }
        ui.horizontal_wrapped(|ui| {
            ui.label("単語 (候補から選ぶか直接入力)");
            ui.add(egui::TextEdit::singleline(&mut self.word).desired_width(200.));
            egui::ComboBox::from_id_salt("dictionary_suggestions")
                .selected_text("候補")
                .show_ui(ui, |ui| {
                    for word in candidates {
                        ui.selectable_value(&mut self.word, word.clone(), word);
                    }
                });
        });
        ui.horizontal(|ui| {
            if ui.button("登録").clicked() {
                self.action(json!({"op":"dictionary_set","reading":reading,"word":self.word}));
                if self.result["saved"] == true {
                    self.reading.clear();
                    self.word.clear();
                }
            }
            if ui.button("選んだ語を削除").clicked() {
                self.action(
                    json!({"op":"dictionary_remove","reading":remove_reading,"word":self.word}),
                );
            }
        });
        ui.horizontal(|ui| {
            if ui.button("取り込む...").clicked() {
                if let Some(path) = rfd::FileDialog::new()
                    .set_title("ユーザー辞書を取り込む")
                    .add_filter("辞書のテキストファイル", &["txt"])
                    .add_filter("macOS のユーザ辞書", &["plist"])
                    .add_filter("すべてのファイル", &["*"])
                    .pick_file()
                {
                    self.action(json!({"op":"dictionary_import","path":path}));
                }
            }
            if ui.button("書き出す...").clicked() {
                if let Some(path) = rfd::FileDialog::new()
                    .set_title("ユーザー辞書を書き出す")
                    .add_filter("辞書のテキストファイル", &["txt"])
                    .set_file_name("Meltype-ユーザー辞書.txt")
                    .save_file()
                {
                    self.action(json!({"op":"dictionary_export","path":path}));
                }
            }
        });
        match self.store.load() {
            Ok(state) => {
                egui::ScrollArea::vertical().show(ui, |ui| {
                    egui::Grid::new("dictionary_words")
                        .min_col_width(150.)
                        .striped(true)
                        .show(ui, |ui| {
                            ui.strong("読み");
                            ui.strong("単語");
                            ui.end_row();
                            for (reading, words) in state.preferences.user_dictionary {
                                for word in words {
                                    let selected = self.reading == reading && self.word == word;
                                    let a = ui.selectable_label(selected, &reading);
                                    let b = ui.selectable_label(selected, &word);
                                    if a.clicked() || b.clicked() {
                                        self.reading = reading.clone();
                                        self.word = word;
                                    }
                                    ui.end_row();
                                }
                            }
                        });
                });
            }
            Err(e) => {
                ui.label(e.to_string());
            }
        }
    }
    fn snippets(&mut self, ui: &mut egui::Ui) {
        ui.heading("Meltype-FLASH 定型文");
        ui.label(format!("行の先頭か空白の直後に {}名前 と打って Space / Tab を押すと、登録した文に置き換えます。", self.settings.text("SnippetMark")));
        ui.horizontal(|ui| {
            ui.label("名前");
            ui.text_edit_singleline(&mut self.snippet_name);
        });
        ui.add(
            egui::TextEdit::multiline(&mut self.snippet_text)
                .desired_rows(6)
                .desired_width(f32::INFINITY),
        );
        ui.horizontal(|ui| {
            if ui.button("登録").clicked() {
                self.action(
                    json!({"op":"snippet_set","name":self.snippet_name,"text":self.snippet_text}),
                );
            }
            if ui.button("選んだ定型文を削除").clicked() {
                self.action(json!({"op":"snippet_remove","name":self.snippet_name}));
            }
        });
        ui.horizontal(|ui| {
            if ui.button("取り込む...").clicked() {
                if let Some(path) = rfd::FileDialog::new()
                    .set_title("定型文を取り込む")
                    .add_filter("定型文", &["json"])
                    .pick_file()
                {
                    self.action(json!({"op":"snippets_import","path":path}));
                }
            }
            if ui.button("書き出す...").clicked() {
                if let Some(path) = rfd::FileDialog::new()
                    .set_title("定型文を書き出す")
                    .add_filter("定型文", &["json"])
                    .set_file_name("Meltype-定型文.json")
                    .save_file()
                {
                    self.action(json!({"op":"snippets_export","path":path}));
                }
            }
        });
        if let Ok(state) = self.store.load() {
            egui::ScrollArea::vertical().show(ui, |ui| {
                egui::Grid::new("snippet_entries")
                    .min_col_width(180.)
                    .striped(true)
                    .show(ui, |ui| {
                        ui.strong("名前");
                        ui.strong("文");
                        ui.end_row();
                        for (name, text) in state.snippets {
                            let selected = self.snippet_name == name;
                            let a = ui.selectable_label(
                                selected,
                                format!("{}{name}", self.settings.text("SnippetMark")),
                            );
                            let b = ui.add(
                                egui::Label::new(text.replace('\n', " ⏎ "))
                                    .truncate()
                                    .sense(egui::Sense::click()),
                            );
                            if a.clicked() || b.clicked() {
                                self.snippet_name = name;
                                self.snippet_text = text;
                            }
                            ui.end_row();
                        }
                    });
            });
        }
    }
    fn learning(&mut self, ui: &mut egui::Ui) {
        ui.heading("Meltype-FLASH 学習した語");
        ui.horizontal(|ui| {
            if ui.button("更新").clicked() {
                self.action(json!({"op":"learning_list"}));
            }
            if ui.button("直近1時間の学習を消去").clicked() {
                self.action(
                    json!({"op":"learning_forget_since","since":meltype_core::storage::now()-3600}),
                );
            }
        });
        ui.collapsing("学習データをすべて消す", |ui| {
            ui.label("辞書と定型文は残ります。");
            if ui.button("全学習を消去").clicked() {
                self.action(json!({"op":"learning_clear"}));
            }
        });
        let data = match control::execute(&self.store, &json!({"op":"learning_list"})) {
            Ok(data) => data,
            Err(error) => {
                ui.label(error.to_string());
                return;
            }
        };
        let mut remove = None;
        egui::ScrollArea::vertical().show(ui, |ui| {
            for (group, title) in [
                ("languages", "日英の選び直し"),
                ("conversions", "変換した語"),
                ("translations", "英訳"),
                ("phrases", "入力した文"),
            ] {
                ui.heading(title);
                let mut rows = Vec::new();
                if let Some(entries) = data[group].as_object() {
                    for (key, value) in entries {
                        if group == "languages" {
                            rows.push((
                                key.clone(),
                                format!(
                                    "{} ({} 回{})",
                                    if value["english"] == true {
                                        "英語"
                                    } else {
                                        "日本語"
                                    },
                                    value["count"],
                                    if value["active"] == false {
                                        "・確認待ち"
                                    } else {
                                        ""
                                    }
                                ),
                            ));
                        } else if let Some(text) = value.as_str() {
                            rows.push((key.clone(), text.to_owned()));
                        } else if let Some(choices) = value.as_object() {
                            for (text, count) in choices {
                                rows.push((key.clone(), format!("{text} ({count} 回)")));
                            }
                        }
                    }
                } else if let Some(entries) = data[group].as_array() {
                    for value in entries {
                        rows.push((
                            value["reading"].as_str().unwrap_or_default().to_owned(),
                            value["text"].as_str().unwrap_or_default().to_owned(),
                        ));
                    }
                }
                if rows.is_empty() {
                    ui.label("まだありません。");
                }
                egui::Grid::new(group)
                    .min_col_width(150.)
                    .striped(true)
                    .show(ui, |ui| {
                        for (key, text) in rows {
                            if group == "languages" {
                                if let Some(word) = key.strip_suffix("@e") {
                                    ui.label(format!("{word} (英語の後)"));
                                } else if let Some(word) = key.strip_suffix("@j") {
                                    ui.label(format!("{word} (日本語の後)"));
                                } else {
                                    ui.label(&key);
                                }
                            } else {
                                ui.label(&key);
                            }
                            ui.add(egui::Label::new(text).wrap());
                            if ui
                                .button("忘れる")
                                .on_hover_text("この語・読みの学習を忘れます。")
                                .clicked()
                            {
                                let prefix = match group {
                                    "languages" => "language",
                                    "phrases" => "phrase",
                                    _ => "conversion",
                                };
                                remove = Some(format!("{prefix}:{key}"));
                            }
                            ui.end_row();
                        }
                    });
                ui.separator();
            }
        });
        if let Some(key) = remove {
            self.action(json!({"op":"learning_remove","key":key}));
        }
    }
    fn create_backup_dialog(&mut self) {
        if let Some(path) = rfd::FileDialog::new()
            .set_title("バックアップを書き出す")
            .add_filter("Meltype-FLASH バックアップ", &["meltype-backup", "json"])
            .set_file_name("Meltype-flash.meltype-backup")
            .save_file()
        {
            self.action(json!({"op":"backup_create","path":path}));
            let saved = self.result["saved"] == true;
            rfd::MessageDialog::new()
                .set_title("Meltype-FLASH バックアップ")
                .set_level(if saved {
                    rfd::MessageLevel::Info
                } else {
                    rfd::MessageLevel::Error
                })
                .set_description(if saved {
                    format!("バックアップを書き出しました。\n{}", path.display())
                } else {
                    self.status.clone()
                })
                .show();
        }
    }
    fn open_backup_dialog(&mut self) {
        if let Some(path) = rfd::FileDialog::new()
            .set_title("バックアップから戻す")
            .add_filter("Meltype-FLASH バックアップ", &["meltype-backup", "json"])
            .pick_file()
        {
            self.action(json!({"op":"backup_inspect","path":path}));
            self.restore_path = self.result["files"].is_object().then_some(path);
            self.restore_prompt = self.restore_path.is_some();
            if !self.restore_prompt {
                rfd::MessageDialog::new()
                    .set_title("Meltype-FLASH バックアップ")
                    .set_level(rfd::MessageLevel::Error)
                    .set_description(&self.status)
                    .show();
            }
        }
    }
    fn backup(&mut self, ui: &mut egui::Ui) {
        ui.heading("Meltype-FLASH バックアップ");
        ui.horizontal(|ui| {
            if ui.button("バックアップを書き出す...").clicked() {
                self.create_backup_dialog();
            }
            if ui.button("バックアップから戻す...").clicked() {
                self.open_backup_dialog();
            }
        });
        if let Some(path) = self.restore_path.clone() {
            ui.label(path.display().to_string());
            if let Some(files) = self.result["files"].as_object() {
                ui.label(format!(
                    "作成日時: {}",
                    self.result["created"].as_str().unwrap_or("不明")
                ));
                for name in files.keys() {
                    ui.label(name);
                }
            }
            ui.collapsing("このバックアップから復元", |ui| {
                ui.label("同名の現在のデータと編集中の設定を置き換えます。元データは .before-restore に残します。");
                if ui.button("復元する...").clicked() { self.restore_prompt = true; }
            });
        }
    }
    fn restore_backup(&mut self, path: &std::path::Path) {
        self.action(json!({"op":"backup_restore","path":path}));
        if self.result["restored"].is_number() {
            match self.store.settings() {
                Ok(settings) => {
                    self.opened = settings.clone();
                    self.settings = settings.clone();
                    self.test_session.reset();
                    self.test_session.settings = settings;
                    self.complex.0.clear();
                    self.restore_path = None;
                    self.restore_prompt = false;
                    self.status = "バックアップを復元しました。".into();
                }
                Err(error) => self.status = format!("復元した設定を読めません: {error}"),
            }
        }
    }
    fn test(&mut self, ui: &mut egui::Ui) {
        ui.label("ローマ字入力を試す / Type romaji");
        if ui.text_edit_singleline(&mut self.test_raw).changed() {
            self.test_session.reset();
            for c in self.test_raw.chars() {
                self.test_session.key(
                    c.to_ascii_uppercase() as i32,
                    c as i32,
                    i32::from(c.is_ascii_uppercase()),
                );
            }
        }
        if !self.test_raw.is_empty() && self.test_raw.len() <= 256 {
            let keys: Vec<_> = self
                .test_raw
                .chars()
                .map(|c| c.to_ascii_uppercase() as i32)
                .collect();
            let result = meltype_core::detection::evaluate(
                &self.test_raw.to_ascii_lowercase(),
                &keys,
                true,
                &self.settings,
            );
            let language = match result.verdict {
                meltype_core::detection::Verdict::Japanese => "日本語",
                meltype_core::detection::Verdict::English => "英語",
                _ => "未確定",
            };
            ui.label(format!(
                "英数状態での判定: {language}（日本語 {} / 英語 {}）",
                result.japanese, result.english
            ));
            ui.collapsing("判定理由", |ui| {
                for contribution in result.contributions {
                    ui.label(contribution.reason);
                }
            });
        }
        ui.horizontal(|ui| {
            for (label, vk) in [
                ("変換", 32),
                ("ひらがな", 0x75),
                ("カタカナ", 0x76),
                ("半角カナ", 0x77),
                ("英字", 0x79),
            ] {
                if ui.button(label).clicked() {
                    let before = self
                        .test_session
                        .result(false)
                        .view
                        .map(|view| view.text)
                        .unwrap_or_default();
                    self.test_session.key(vk, 0, 0);
                    let after = self
                        .test_session
                        .result(false)
                        .view
                        .map(|view| view.text)
                        .unwrap_or_default();
                    self.dialogs.record(
                        &self.test_raw,
                        &before,
                        &after,
                        if vk == 32 { 0 } else { 2 },
                    );
                }
            }
        });
        let view = self.test_session.result(false).view;
        if let Some(view) = view {
            ui.heading(&view.text);
            if let Some(meaning) = view.meaning {
                ui.label(meaning);
            }
            if let Some(suggestion) = view.suggestion {
                ui.label(format!("もしかして: {suggestion}"));
            }
            egui::ScrollArea::vertical().show(ui, |ui| {
                ui.horizontal_wrapped(|ui| {
                    ui.spacing_mut().item_spacing.x = 6.0;
                    ui.spacing_mut().item_spacing.y = 4.0;
                    for (i, candidate) in view.candidates.iter().enumerate() {
                        if ui.button(candidate).clicked() {
                            let result = self.test_session.select(i);
                            let expected = result
                                .commits
                                .iter()
                                .map(|entry| entry.text.as_str())
                                .collect::<String>();
                            self.dialogs
                                .record(&self.test_raw, &view.text, &expected, 0);
                            self.test_raw.clear();
                            self.status = result
                                .commits
                                .iter()
                                .map(|e| e.text.as_str())
                                .collect::<String>();
                        }
                    }
                });
            });
        }
    }
}
fn toggle_shortcuts_editor(
    ui: &mut egui::Ui,
    value: &mut Value,
    settings: &Settings,
    draft: &mut String,
) -> Result<bool, String> {
    use meltype_core::shortcuts::ToggleShortcut;
    let mut shortcuts: Vec<ToggleShortcut> =
        serde_json::from_value(value.clone()).map_err(|error| error.to_string())?;
    let mut remove = None;
    for (index, shortcut) in shortcuts.iter().enumerate() {
        ui.horizontal(|ui| {
            ui.label(shortcut.label());
            if ui.small_button("削除").clicked() {
                remove = Some(index);
            }
        });
    }
    let mut changed = false;
    if let Some(index) = remove {
        shortcuts.remove(index);
        changed = true;
    }
    let mut add = false;
    ui.horizontal(|ui| {
        ui.text_edit_singleline(draft)
            .on_hover_text("例: Ctrl+Alt+J、Ctrl+CapsLock、Alt+英数");
        add = ui.button("追加").clicked();
        if ui.button("初期値に戻す").clicked() {
            shortcuts = ToggleShortcut::defaults();
            changed = true;
        }
    });
    ui.weak(if cfg!(target_os = "linux") {
        "複数のキーを登録できます。Superはデスクトップ側で使われる場合があります。"
    } else {
        "複数のキーを登録できます。CapsLock・英数にはCtrl・Alt・Winを組み合わせます。"
    });
    if add {
        let shortcut = ToggleShortcut::parse(draft)
            .ok_or("切替キーを確認してください。例: Ctrl+Alt+J、Ctrl+CapsLock、Alt+英数")?;
        shortcut
            .validate(settings, &shortcuts)
            .map_err(|reason| format!("{}: {reason}", shortcut.label()))?;
        shortcuts.push(shortcut);
        draft.clear();
        changed = true;
    }
    if changed {
        *value = serde_json::to_value(shortcuts).unwrap();
    }
    Ok(changed)
}
/// 本家1.5.0系の設定画面に近い、左側の分類へ振り分ける表示用の分類。
/// 保存する設定の分類 (settings-ui.json) は変えない。
fn ui_category(key: &str, original: &str) -> &'static str {
    match key {
        "ToggleShortcuts" | "ConvertKeysSwitchKeyboard" | "AltKeysSwitchKeyboard" | "SkkMode" => {
            "2. 入力モード"
        }
        "DetectionLevel"
        | "ScoredSegmentation"
        | "JapaneseThreshold"
        | "TypoEnabled"
        | "DirectModeAutoDetect"
        | "SigilWordsDirect"
        | "EnglishFirstInput"
        | "AutoCorrectAfterCommit"
        | "LaughterAfterKana"
        | "ReadingLattice" => "3. 判定",
        "NumberKeysSelectCandidates"
        | "AsyncConversion"
        | "EnterCommitsAsShown"
        | "ShowCandidatesWhileTyping"
        | "LiveConversion"
        | "ConversionEngine"
        | "UseZenzai"
        | "ZenzaiServerPath"
        | "ZenzaiModelPath"
        | "TabConversion"
        | "ContinueAfterConversion"
        | "CorrectTypos"
        | "StartConversionAtLastClause" => "4. 変換",
        "CurrencyRateMode" | "CurrencyDecimals" | "CurrencyManualRates" => "14. 通貨",
        "SnippetMark"
        | "CalculatorCandidates"
        | "GitHubIssueCandidates"
        | "GitHubRepository"
        | "ClipboardCandidates"
        | "TranslationCandidates"
        | "PredictiveCandidates"
        | "EnglishNamePredictions"
        | "ConsonantAbbreviations"
        | "ShowCandidateMeanings" => "5. 候補",
        "SpaceAroundEnglish"
        | "SpaceAroundNumbers"
        | "FullWidthSpaceByDefault"
        | "ShiftSpaceHalfWidth"
        | "FullWidthParentheses"
        | "SquareBrackets"
        | "CursorIntoBrackets"
        | "Punctuation"
        | "JapanesePunctuationAfterEnglish"
        | "FullWidthSlash"
        | "SlashAsMiddleDot"
        | "ZSymbols" => "6. 記号・スペース",
        "AlphanumericKeyStartsAsTyped"
        | "EscStepsBack"
        | "EscRevertsSelectedClause"
        | "FunctionKeysAffectSelectedClause"
        | "CompatibleShortcuts"
        | "PartialCommit"
        | "ControlKeyBindings"
        | "MacControlKeys"
        | "SeparateKeyRoles" => "7. キー操作",
        "ShowTypedKeys"
        | "CompositionPlacement"
        | "CompositionSize"
        | "CompositionFont"
        | "CompositionTheme"
        | "CompositionOpacity"
        | "ShowModeIndicator"
        | "ShowModeIndicatorOnFocus"
        | "ModeIndicatorJapaneseColor"
        | "ModeIndicatorDirectColor"
        | "ModeIndicatorTextColor" => "8. 表示",
        "LearningEnabled" | "ContextualLearning" => "9. 学習",
        "FileLog" | "LogTypedText" | "LogKeyIntervals" => "11. ログ",
        "AutoUpdate" | "PrereleaseUpdate" => "12. 更新",
        "keycount-discord" => "8. 表示",
        "keycount" => "13. 打鍵数",
        _ if original == "7. アプリ" => "10. アプリ",
        _ if original == "8. ログ" => "11. ログ",
        _ if original == "9. 更新" => "12. 更新",
        _ => "1. 全般",
    }
}
/// 設定の分類を本家と同じ順に並べるための並び順。
fn category_order(category: &str) -> (u32, &str) {
    let number = match category {
        "1. 全般" => 1,
        "2. 入力モード" => 2,
        "3. 判定" => 3,
        "5. 候補" => 4,
        "6. 記号・スペース" => 5,
        "4. 変換" => 6,
        "7. キー操作" => 7,
        "9. 学習" => 8,
        "8. 表示" => 9,
        "12. 更新" => 10,
        "10. アプリ" => 11,
        "11. ログ" => 12,
        "13. 打鍵数" => 13,
        "14. 通貨" => 14,
        _ => {
            let digits = category
                .find(|c: char| !c.is_ascii_digit())
                .unwrap_or(category.len());
            category[..digits].parse().unwrap_or(u32::MAX - 1)
        }
    };
    (number, category)
}
fn visible_by_default(key: &str) -> bool {
    supported(key)
        || key == "ToggleShortcuts"
        || metadata::field(key).is_some_and(|f| f["category"] == "1. 全般")
}
fn supported(key: &str) -> bool {
    [
        "AutoUpdate",
        "GitHubIssueCandidates",
        "GitHubRepository",
        "ClipboardCandidates",
        "LogKeyIntervals",
        "PauseHotkey",
        "ContextualLearning",
        "ShiftSpaceHalfWidth",
        "EnterCommitsAsShown",
        "AlphanumericKeyStartsAsTyped",
        "FullWidthParentheses",
        "FullWidthSlash",
        "SquareBrackets",
        "EscStepsBack",
        "EscRevertsSelectedClause",
        "FunctionKeysAffectSelectedClause",
        "CompatibleShortcuts",
        "PartialCommit",
        "MacControlKeys",
        "ControlKeyBindings",
        "CalculatorCandidates",
        "LaughterAfterKana",
        "FullWidthSpaceByDefault",
        "EnglishFirstInput",
        "ConsonantAbbreviations",
        "EnglishNamePredictions",
        "ShowCandidatesWhileTyping",
        "StartConversionAtLastClause",
        "Enabled",
        "Mode",
        "LiveConversion",
        "DirectModeAutoDetect",
        "SigilWordsDirect",
        "DetectionLevel",
        "JapaneseThreshold",
        "TypoEnabled",
        "MaxPendingKeys",
        "AppRules",
        "AppKinds",
        "LearningEnabled",
        "InputStyle",
        "Punctuation",
        "CorrectTypos",
        "AutoCorrectAfterCommit",
        "TranslationCandidates",
        "PredictiveCandidates",
        "TabConversion",
        "ContinueAfterConversion",
        "SeparateKeyRoles",
        "ShowCandidateMeanings",
        "ShowTypedKeys",
        "SnippetMark",
        "SpaceAroundEnglish",
        "SpaceAroundNumbers",
        "NumberKeysSelectCandidates",
        "ZSymbols",
        "SlashAsMiddleDot",
        "CurrencyRateMode",
        "CurrencyDecimals",
        "CurrencyManualRates",
    ]
    .contains(&key)
        || cfg!(target_os = "linux")
            && [
                "ConversionEngine",
                "UseZenzai",
                "ZenzaiServerPath",
                "ZenzaiModelPath",
            ]
            .contains(&key)
        || cfg!(windows)
            && [
                "TextInputApps",
                "ShowNotifications",
                "CursorIntoBrackets",
                "ToggleShortcuts",
                "AllowInjectedInput",
                "ConversionEngine",
                "UseZenzai",
                "ZenzaiServerPath",
                "ZenzaiModelPath",
            ]
            .contains(&key)
}
fn keyboard_limitation(key: &str) -> Option<&'static str> {
    match key {
        "LiveConversion" => {
            Some("キーボード方式ではライブ変換を使用できません。IME方式で使用できます。")
        }
        "LearningEnabled" | "ContextualLearning" => {
            Some("キーボード方式では学習を保存しません。IME方式で使用できます。")
        }
        "AutoCorrectAfterCommit" | "DirectModeAutoDetect" => {
            Some("キーボード方式では確定済みの文章を書き換えません。IME方式で使用できます。")
        }
        "ConversionEngine"
        | "UseZenzai"
        | "ZenzaiServerPath"
        | "ZenzaiModelPath"
        | "ClipboardCandidates"
        | "GitHubIssueCandidates"
        | "GitHubRepository" => {
            Some("キーボード方式ではこの候補源を使用できません。IME方式で使用できます。")
        }
        "CompositionTheme"
        | "CompositionSize"
        | "CompositionPlacement"
        | "CompositionOpacity"
        | "ShowCandidateMeanings" => {
            Some("キーボード方式の簡易候補表示では、この表示設定を使用できません。")
        }
        _ => None,
    }
}
fn time_zones() -> &'static [&'static str] {
    static ZONES: std::sync::OnceLock<Vec<&'static str>> = std::sync::OnceLock::new();
    ZONES
        .get_or_init(|| {
            chrono_tz::TZ_VARIANTS
                .iter()
                .map(|zone| zone.name())
                .collect()
        })
        .as_slice()
}
fn choices(key: &str) -> Option<&'static [&'static str]> {
    Some(match key {
        "DetectionLevel" => &["Aggressive", "Balanced", "Conservative", "Manual"],
        "InputStyle" => &["Romaji", "Kana"],
        "Punctuation" => &[
            "Japanese",
            "FullWidthCommaPeriod",
            "FullWidthCommaKuten",
            "ToutenFullWidthPeriod",
        ],
        "Mode" => &["Keyboard", "Tsf"],
        "ConversionEngine" if cfg!(target_os = "linux") => &["Mozc"],
        "ConversionEngine" => &["Hybrid", "Mozc", "System"],
        "CompositionPlacement" => &["Overlay", "BelowCaret", "AboveCaret"],
        "CompositionSize" => &["Auto", "Small", "Medium", "Large", "ExtraLarge", "Huge"],
        "CompositionTheme" => &["Dark", "Light", "System"],
        "CompositionOpacity" => &["Opaque", "Percent90", "Percent80", "Percent70"],
        "UiLanguage" => &["Auto", "Japanese", "English"],
        "TimeZone" => time_zones(),
        "CurrencyRateMode" => &["Fixed", "Auto", "Manual"],
        _ => return None,
    })
}
impl App {
    /// 打鍵数。数えるか・Discord に出すか・表示する文言を変える。
    fn keycount(&mut self, ui: &mut egui::Ui) {
        use meltype_core::keycount;
        let mut count = keycount::load(&self.store.directory);
        let mut changed = false;
        ui.weak("打った文字は残さず、合計と日ごとの回数だけを数えます。Discord には今日と合計の回数・1 分あたりの回数だけを送ります。");
        ui.add_space(8.);
        let today = count.days.get(&keycount::today()).copied().unwrap_or(0);
        ui.label(format!(
            "合計 {} 打　　今日 {} 打",
            keycount::group(count.total),
            keycount::group(today)
        ));
        ui.add_space(12.);
        changed |= ui
            .horizontal(|ui| {
                ui.label("打鍵を数える");
                widgets::toggle(ui, &mut count.enabled, "打鍵を数える")
                    .on_hover_text(
                        "OFF の間は何も数えません。パスワード欄などプライベートな入力は数えません。",
                    )
                    .changed()
            })
            .inner;
        changed |= ui
            .horizontal(|ui| {
                ui.label("打鍵数をトレイに表示する");
                widgets::toggle(ui, &mut count.show, "打鍵数をトレイに表示する")
                    .on_hover_text("トレイのツールチップに今日の打鍵数を表示します。")
                    .changed()
            })
            .inner;
        ui.weak("Discord に表示するかどうかは「表示」の設定で切り替えます。");
        ui.add_space(12.);
        ui.label("表示する文言 (Discord のステータス・変更できません):");
        ui.indent("fixed-keycount-wording", |ui| {
            ui.label(format!("詳細の行: {}", keycount::DEFAULT_DETAILS));
            ui.label(format!("状態の行: {}", keycount::DEFAULT_STATE));
            ui.label(format!("ボタンの文字: {}", keycount::DEFAULT_BUTTON));
            ui.label(format!("ボタンのリンク先: {}", keycount::DEFAULT_URL));
        });
        ui.add_space(12.);
        if ui.button("数えた分をリセット").clicked() {
            match keycount::reset(&self.store.directory) {
                Ok(()) => {
                    self.status = "打鍵数をリセットしました。".into();
                    count = keycount::load(&self.store.directory);
                }
                Err(error) => self.status = error.to_string(),
            }
        }
        if changed {
            match keycount::save_settings(&self.store.directory, &count) {
                Ok(()) => self.status = "打鍵数の設定を保存しました。".into(),
                Err(error) => self.status = error.to_string(),
            }
        }
    }
}
impl eframe::App for App {
    fn update(&mut self, ctx: &egui::Context, _: &mut eframe::Frame) {
        crate::i18n::set(self.settings.text("UiLanguage"));
        let title = crate::i18n::window_title();
        if title != self.title {
            self.title = title.to_owned();
            ctx.send_viewport_cmd(egui::ViewportCommand::Title(title.to_owned()));
        }
        // 同意待ちでもトレイは作る。設定画面だけを同意画面で止める。
        #[cfg(windows)]
        self.update_tray(ctx);
        self.update_ui_fonts(ctx);
        if self.consent_pending {
            self.consent(ctx);
            return;
        }
        if ctx.input(|i| i.viewport().close_requested()) && !self.closing {
            self.finish(ctx, self.discard);
        }
        #[cfg(feature = "visual-test")]
        if let Ok(path) = std::env::var("MELTYPE_UI_CAPTURE") {
            self.capture_frames += 1;
            if self.capture_frames == 1 {
                if let Ok(filter) = std::env::var("MELTYPE_UI_FILTER") {
                    self.show_all = true;
                    self.filter = filter;
                }
                if let Ok(screen) = std::env::var("MELTYPE_UI_LIFECYCLE") {
                    self.lifecycle.welcome = screen == "welcome";
                    self.lifecycle.about = screen == "about";
                    self.lifecycle.updates = screen == "updates";
                    if screen == "ai-setup" {
                        self.ai_setup.request_model(
                            meltype_core::ai_catalog::find("zenz-v3.1-small-q5_k_m").unwrap(),
                        );
                    }
                    self.dialogs.report = screen == "report";
                    self.dialogs.log = screen == "log";
                    self.dialogs.corrections = screen == "corrections";
                    if self.dialogs.corrections {
                        self.dialogs.record("hashi", "橋", "箸", 0);
                    }
                }
                if let Ok(page) = std::env::var("MELTYPE_UI_PAGE") {
                    self.page = page.parse::<usize>().unwrap_or(0).min(6);
                }
                if let Ok(key) = std::env::var("MELTYPE_UI_HELP_KEY") {
                    self.help = metadata::description(&key).to_owned();
                }
            }
            ctx.request_repaint();
            #[cfg(windows)]
            if self.capture_frames == 5 && std::env::var_os("MELTYPE_TRAY_SMOKE").is_some() {
                if let Some(tray) = &self.tray {
                    tray.exercise_hotkey();
                }
            }
            #[cfg(windows)]
            let ready = self.tray.as_ref().is_none_or(|t| !t.startup_status().1);
            #[cfg(not(windows))]
            let ready = true;
            let ready = ready && !self.ai_setup.checking();
            if self.capture_frames >= 20 && ready {
                ctx.send_viewport_cmd(egui::ViewportCommand::Screenshot(egui::UserData::default()));
            }
            for event in ctx.input(|i| i.events.clone()) {
                if let egui::Event::Screenshot { image, .. } = event {
                    let rgba: Vec<u8> = image.pixels.iter().flat_map(|p| p.to_array()).collect();
                    std::fs::write(format!("{path}.rgba"), rgba).expect("write capture");
                    std::fs::write(format!("{path}.json"), json!(image.size).to_string())
                        .expect("write capture size");
                    #[cfg(windows)]
                    std::fs::write(
                        format!("{path}.state.json"),
                        json!({"resident":self.resident,"trayCreated":self.tray.is_some(),"hotkey":self.tray.as_ref().and_then(|t|t.hotkey_name()),"startup":self.tray.as_ref().map(|t|t.startup_status()),"status":self.status})
                            .to_string(),
                    )
                    .expect("write tray capture state");
                    self.closing = true;
                    ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                }
            }
        }
        if let Some(result) = self.ai_setup.show(ctx, &self.store.directory) {
            match result {
                Ok(paths) => {
                    use meltype_core::{ai_catalog, ai_model::AiModel};
                    let family = paths["family"].as_str().and_then(AiModel::parse).unwrap_or(
                        if paths["engine"] == "shunti" {
                            AiModel::Shunti
                        } else {
                            AiModel::Zenz
                        },
                    );
                    // A background verification must not undo a newer selection.
                    let current = AiModel::selected(&self.settings);
                    if current == family
                        && paths["variant"].as_str().is_none_or(|id| {
                            ai_catalog::selected(&self.settings, current)
                                .is_some_and(|m| m.id == id)
                        })
                    {
                        if family == AiModel::Shunti {
                            self.settings
                                .set("ShuntiHelperPath", paths["helper"].clone())
                                .unwrap();
                        } else {
                            self.settings
                                .set("ZenzaiServerPath", paths["server"].clone())
                                .unwrap();
                            self.settings
                                .set("ZenzaiModelPath", paths["model"].clone())
                                .unwrap();
                        }
                        family.select(&mut self.settings, true);
                        self.status =
                            "選択したモデルを使用できます。「OK」で保存すると入力へ反映します。"
                                .into();
                    }
                }
                Err(error) => self.status = format!("AI環境を作成できませんでした: {error}"),
            }
        }
        if self.lifecycle.show(ctx, &self.store) {
            match self.store.change_settings(|s| {
                s.set("WelcomeShown", json!(true))?;
                if s.text("Mode") == "Tsf" {
                    s.set("TsfIntroduced", json!(true))?;
                }
                Ok(s.boolean("TsfIntroduced"))
            }) {
                Ok(introduced) => {
                    self.lifecycle.welcome = false;
                    self.settings.0.insert("WelcomeShown".into(), json!(true));
                    self.opened.0.insert("WelcomeShown".into(), json!(true));
                    self.settings
                        .0
                        .insert("TsfIntroduced".into(), json!(introduced));
                    self.opened
                        .0
                        .insert("TsfIntroduced".into(), json!(introduced));
                }
                Err(error) => self.status = error.to_string(),
            }
        }
        if self.lifecycle.release_notes {
            self.lifecycle.release_notes = false;
            self.open_release_notes();
        }
        if self.release_notes {
            let open = self.release_notes_window(ctx);
            if !open {
                self.release_notes_from_update = false;
            }
            self.release_notes = open;
        }
        if self.page != 0 {
            egui::TopBottomPanel::top("navigation").show(ctx, |ui| self.navigation(ui));
        }
        egui::TopBottomPanel::bottom("status")
            .frame(
                egui::Frame::NONE
                    .fill(egui::Color32::from_gray(240))
                    .inner_margin(egui::Margin::symmetric(8, 10)),
            )
            .show(ctx, |ui| {
                #[cfg(target_os = "linux")]
                ui.horizontal_wrapped(|ui| {
                    ui.label(format!(
                        "Meltype-FLASH {}",
                        meltype_core::version::display(env!("CARGO_PKG_VERSION"))
                    ));
                    if ui.button(i18n::t("アップデート")).clicked() {
                        self.lifecycle.updates = true;
                    }
                    if ui.button(i18n::t("バージョン情報")).clicked() {
                        self.lifecycle.about = true;
                    }
                    if ui
                        .add_enabled(
                            self.settings.0 != self.opened.0,
                            egui::Button::new(i18n::t("適用")),
                        )
                        .clicked()
                    {
                        self.save();
                    }
                });
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if ui
                        .add_sized([92., 32.], egui::Button::new(i18n::t("キャンセル")))
                        .clicked()
                    {
                        self.finish(ctx, true);
                    }
                    if ui
                        .add_sized(
                            [72., 32.],
                            egui::Button::new("OK").stroke(egui::Stroke::new(
                                1_f32,
                                egui::Color32::from_rgb(0, 120, 212),
                            )),
                        )
                        .clicked()
                    {
                        self.finish(ctx, false);
                    }
                    if self.page == 0
                        && ui
                            .add_sized([114., 32.], egui::Button::new(i18n::t("既定値に戻す")))
                            .clicked()
                    {
                        let defaults = Settings::default();
                        for (key, value) in defaults.0 {
                            if !["Profiles", "ActiveProfile", "SettingsVersion"]
                                .contains(&key.as_str())
                            {
                                self.settings.0.insert(key, value);
                            }
                        }
                        self.test_session.settings = self.settings.clone();
                        self.status = "既定値に戻しました。「OK」で保存します。".into();
                    }
                });
                #[cfg(windows)]
                if let Some(keyboard) = &self.keyboard {
                    let shortcut = self.opened.toggle_shortcuts_label();
                    ui.label(format!(
                        "ポータブル入力: {}（{shortcut}で切替）",
                        if keyboard.enabled() { "ON" } else { "OFF" }
                    ));
                    ui.small(
                    "編集可能な通常入力欄のみ対応。OS辞書・確定後訂正・学習保存は使用しません。",
                );
                }
                if !self.status.is_empty() {
                    ui.label(&self.status);
                }
            });
        if self.page == 0 {
            egui::TopBottomPanel::bottom("detection_test")
                .exact_height(110.)
                .resizable(false)
                .show(ctx, |ui| {
                    ui.label(i18n::t("判定テスト (ローマ字か英語かの判定。英字で入力):"));
                    ui.add(
                        egui::TextEdit::singleline(&mut self.test_raw).desired_width(f32::INFINITY),
                    );
                    egui::Frame::NONE
                        .fill(egui::Color32::from_gray(240))
                        .stroke(egui::Stroke::new(1_f32, egui::Color32::from_gray(160)))
                        .inner_margin(3.)
                        .show(ui, |ui| {
                            ui.set_min_size(egui::vec2(ui.available_width(), 52.));
                            if !self.test_raw.is_empty() && self.test_raw.len() <= 256 {
                                let keys: Vec<_> = self
                                    .test_raw
                                    .chars()
                                    .map(|c| c.to_ascii_uppercase() as i32)
                                    .collect();
                                let result = meltype_core::detection::evaluate(
                                    &self.test_raw.to_ascii_lowercase(),
                                    &keys,
                                    true,
                                    &self.settings,
                                );
                                let verdict = match result.verdict {
                                    meltype_core::detection::Verdict::Japanese => i18n::t("日本語"),
                                    meltype_core::detection::Verdict::English => i18n::t("英語"),
                                    _ => i18n::t("未確定"),
                                };
                                egui::ScrollArea::vertical()
                                    .id_salt("detection_reasons")
                                    .show(ui, |ui| {
                                        ui.label(format!(
                                            "{verdict}  日本語 {} / 英語 {}",
                                            result.japanese, result.english
                                        ));
                                        for contribution in result.contributions {
                                            ui.label(contribution.reason);
                                        }
                                    });
                            }
                        });
                });
        }
        egui::CentralPanel::default().show(ctx, |ui| {
            // Register the background before its children. context_menu upgrades
            // its response to Sense::click; registering it last covers every
            // switch, button and editor with an invisible click target.
            let background = ui.interact(
                ui.max_rect(),
                ui.id().with("page-menu"),
                egui::Sense::click(),
            );
            background.context_menu(|ui| {
                self.navigation(ui);
                ui.separator();
                ui.checkbox(&mut self.show_all, "すべての設定・検索を表示");
                if !self.show_all {
                    self.filter.clear();
                }
                if ui.button("使い方...").clicked() {
                    self.lifecycle.welcome = true;
                    ui.close_menu();
                }
                if ui.button("不具合の報告・提案...").clicked() {
                    self.dialogs.open_report(&self.store);
                    ui.close_menu();
                }
                if ui.button("直した誤判定を報告...").clicked() {
                    self.dialogs.corrections = true;
                    ui.close_menu();
                }
                if ui.button("更新...").clicked() {
                    self.lifecycle.updates = true;
                    ui.close_menu();
                }
                if ui.button("リリースノート...").clicked() {
                    self.open_release_notes();
                    ui.close_menu();
                }
                if ui.button("Meltype-FLASH について...").clicked() {
                    self.lifecycle.about = true;
                    ui.close_menu();
                }
            });
            match self.page {
                0 => self.settings(ui),
                1 => self.dictionary(ui),
                2 => self.snippets(ui),
                3 => self.learning(ui),
                4 => self.backup(ui),
                5 => self.test(ui),
                6 => self.keycount(ui),
                _ => {
                    self.dialogs.log = true;
                    self.page = 0;
                    self.settings(ui);
                }
            }
        });
        if self.dialogs.log || self.dialogs.report || self.dialogs.corrections {
            let active = self
                .store
                .settings()
                .unwrap_or_else(|_| self.opened.clone());
            self.dialogs.show(ctx, &self.store, &active);
        }
        if self.restore_prompt {
            if let Some(path) = self.restore_path.clone() {
                egui::Modal::new(egui::Id::new("confirm-backup-restore")).show(ctx, |ui| {
                    ui.set_max_width(520.);
                    ui.heading("バックアップから戻す");
                    ui.label(path.display().to_string());
                    ui.label(format!("作成日時: {}", self.result["created"].as_str().unwrap_or("不明")));
                    if let Some(files) = self.result["files"].as_object() {
                        ui.label("含まれるファイル:");
                        egui::ScrollArea::vertical().max_height(130.).show(ui, |ui| {
                            for name in files.keys() { ui.label(name); }
                        });
                    }
                    ui.label("現在の設定・ユーザー辞書・学習データと、編集中の設定を置き換えます。元データは .before-restore に残します。");
                    ui.horizontal(|ui| {
                        if ui.button("戻す").clicked() { self.restore_backup(&path); self.restore_prompt = false; }
                        if ui.button("キャンセル").clicked() { self.restore_prompt = false; }
                    });
                });
            } else {
                self.restore_prompt = false;
            }
        }
        if let Some(scope) = self.forget_prompt {
            egui::Window::new("学習データを消す")
                .collapsible(false)
                .resizable(false)
                .show(ctx, |ui| {
                    let label = match scope {
                        "hour" => "この 1 時間",
                        "today" => "今日",
                        _ => "すべて",
                    };
                    ui.label(format!("{label}の学習データを消しますか？"));
                    ui.label("ユーザー辞書と定型文は残ります。");
                    ui.horizontal(|ui| {
                        if ui.button("消す").clicked() {
                            if scope == "all" {
                                self.action(json!({"op":"learning_clear"}));
                            } else {
                                let since = if scope == "hour" {
                                    meltype_core::storage::now() - 3600
                                } else {
                                    chrono::Local::now()
                                        .date_naive()
                                        .and_hms_opt(0, 0, 0)
                                        .and_then(|date| {
                                            date.and_local_timezone(chrono::Local).earliest()
                                        })
                                        .map(|date| date.timestamp())
                                        .unwrap_or_else(meltype_core::storage::now)
                                };
                                self.action(json!({"op":"learning_forget_since","since":since}));
                            }
                            self.forget_prompt = None;
                        }
                        if ui.button("キャンセル").clicked() {
                            self.forget_prompt = None;
                        }
                    });
                });
        }
        if self.uninstall_prompt {
            egui::Window::new("アンインストール").open(&mut self.uninstall_prompt).collapsible(false).show(ctx, |ui| {
                ui.label("Rust版の配布フォルダーで次のコマンドを実行してください。");
                ui.monospace(".\\Manage-Windows.ps1 -Action Uninstall");
                ui.label("Windows の管理者確認後に IME の登録を解除します。利用者データは保持します。");
            });
        }
        if let Some(rename) = self.profile_prompt {
            egui::Window::new(if rename {
                "プロファイルの名前を変更"
            } else {
                "新しいプロファイル"
            })
            .collapsible(false)
            .resizable(false)
            .show(ctx, |ui| {
                ui.label("名前 (例: 仕事用、趣味用、SNS 用)");
                ui.text_edit_singleline(&mut self.profile);
                ui.horizontal(|ui| {
                    if ui.button("OK").clicked() {
                        let result = if rename {
                            let active = self.settings.text("ActiveProfile").to_owned();
                            self.settings.rename_profile(&active, &self.profile)
                        } else {
                            self.settings
                                .add_profile(&self.profile)
                                .and_then(|()| self.settings.switch_profile(&self.profile))
                        };
                        match result {
                            Ok(()) => {
                                self.profile_prompt = None;
                                self.test_session.settings = self.settings.clone();
                                self.status = "「OK」で保存します。".into();
                            }
                            Err(error) => self.status = error,
                        }
                    }
                    if ui.button("キャンセル").clicked() {
                        self.profile_prompt = None;
                    }
                });
            });
        }
        if self.confirm_remove {
            egui::Window::new("プロファイルを削除")
                .collapsible(false)
                .resizable(false)
                .show(ctx, |ui| {
                    let active = self.settings.text("ActiveProfile").to_owned();
                    ui.label(format!("プロファイル「{active}」を削除しますか？"));
                    ui.horizontal(|ui| {
                        if ui.button("削除").clicked() {
                            self.action(json!({"op":"profile_remove","name":active}));
                            self.confirm_remove = false;
                        }
                        if ui.button("キャンセル").clicked() {
                            self.confirm_remove = false;
                        }
                    });
                });
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn ai_choice_keeps_both_installed_paths_and_only_uses_one_engine() {
        let mut settings = Settings::default();
        for (key, path) in [
            ("ZenzaiServerPath", "server"),
            ("ZenzaiModelPath", "model"),
            ("ShuntiHelperPath", "shunti"),
        ] {
            settings.set(key, json!(path)).unwrap();
        }
        meltype_core::ai_model::AiModel::Shunti.select(&mut settings, true);
        assert!(settings.boolean("UseShunti"));
        assert!(!settings.boolean("UseZenzai"));
        meltype_core::ai_model::AiModel::Zenz.select(&mut settings, true);
        assert!(settings.boolean("UseZenzai"));
        assert!(!settings.boolean("UseShunti"));
        assert_eq!(settings.text("ShuntiHelperPath"), "shunti");
        assert_eq!(settings.text("ZenzaiServerPath"), "server");
        assert_eq!(settings.text("ZenzaiModelPath"), "model");
        meltype_core::ai_model::AiModel::None.select(&mut settings, false);
        assert!(!settings.boolean("UseZenzai") && !settings.boolean("UseShunti"));
    }

    #[cfg(windows)]
    #[test]
    fn keyboard_shortcut_validation_and_capabilities_preserve_saved_values() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path());
        let mut app = App::with_store(store.clone());
        assert!(app
            .settings
            .set("ToggleShortcuts", json!([{"Key":0x55,"Modifiers":2}]))
            .is_err());
        // A malformed draft cannot be saved even when it bypasses the editor.
        app.settings.0.insert(
            "ToggleShortcuts".into(),
            json!([{"Key":0x55,"Modifiers":2}]),
        );
        assert!(!app.save());
        assert_eq!(
            store.settings().unwrap().toggle_shortcuts(),
            meltype_core::shortcuts::ToggleShortcut::defaults()
        );
        app.settings
            .set(
                "ToggleShortcuts",
                json!([{"Key":0x4b,"Modifiers":3},{"Key":0x78,"Modifiers":4}]),
            )
            .unwrap();
        app.settings.set("Mode", json!("Keyboard")).unwrap();
        assert!(app.save());
        assert!(keyboard_limitation("LiveConversion").is_some());
        assert!(keyboard_limitation("LearningEnabled").is_some());
        assert!(keyboard_limitation("ConversionEngine").is_some());
        for key in ["UseZenzai", "ZenzaiServerPath", "ZenzaiModelPath"] {
            assert!(supported(key), "Windows IME setting {key}");
            assert!(
                keyboard_limitation(key).is_some(),
                "portable limitation {key}"
            );
        }
        for key in [
            "SpaceAroundNumbers",
            "NumberKeysSelectCandidates",
            "ZSymbols",
        ] {
            assert!(supported(key), "shared input setting {key}");
            assert!(keyboard_limitation(key).is_none());
        }
        assert!(keyboard_limitation("ControlKeyBindings").is_none());
        // Capability presentation does not overwrite the user's TSF preferences.
        assert_eq!(
            store.settings().unwrap().boolean("LiveConversion"),
            Settings::default().boolean("LiveConversion")
        );
    }
    #[test]
    fn mouse_click_can_change_settings_in_the_complete_window() {
        use eframe::App as _;
        let directory = tempfile::tempdir().unwrap();
        consent::accept(directory.path()).unwrap();
        let mut app = App::with_store(Store::new(directory.path()));
        app.settings
            .0
            .insert("UiLanguage".into(), json!("Japanese"));
        app.opened.0.insert("UiLanguage".into(), json!("Japanese"));
        let mut lifecycle_settings = app.settings.clone();
        lifecycle_settings.set("AutoUpdate", json!(false)).unwrap();
        app.lifecycle = lifecycle::Lifecycle::new(&lifecycle_settings);
        app.lifecycle.welcome = false;
        let ctx = egui::Context::default();
        widgets::configure_style(&ctx);
        let mut frame = eframe::Frame::_new_kittest();
        let mut draw = |events| {
            let _ = ctx.run(
                egui::RawInput {
                    screen_rect: Some(egui::Rect::from_min_size(
                        egui::Pos2::ZERO,
                        egui::vec2(720., 700.),
                    )),
                    events,
                    ..Default::default()
                },
                |ctx| app.update(ctx, &mut frame),
            );
        };
        draw(vec![]);
        draw(vec![]);
        let point = ctx
            .data(|data| {
                data.get_temp::<egui::Rect>(egui::Id::new((
                    "toggle_rect",
                    "マウスホイールで値を変更",
                )))
            })
            .unwrap()
            .center();
        draw(vec![
            egui::Event::PointerMoved(point),
            egui::Event::PointerButton {
                pos: point,
                button: egui::PointerButton::Primary,
                pressed: true,
                modifiers: egui::Modifiers::NONE,
            },
        ]);
        draw(vec![egui::Event::PointerButton {
            pos: point,
            button: egui::PointerButton::Primary,
            pressed: false,
            modifiers: egui::Modifiers::NONE,
        }]);
        assert!(
            !app.settings.boolean("SettingsWheelChangesValue"),
            "mouse click did not change the switch"
        );
    }
    #[cfg(windows)]
    #[test]
    fn resident_fonts_follow_settings_and_independent_dialogs() {
        let directory = tempfile::tempdir().unwrap();
        let mut app = App::with_store(Store::new(directory.path()));
        app.lifecycle.welcome = false;
        app.consent_pending = false;
        app.release_notes = false;
        app.settings_visible = false;
        let ctx = egui::Context::default();

        for (settings, log, needed) in [
            (false, false, false),
            (true, false, true),
            (false, true, true),
            (false, false, false),
            (false, true, true),
        ] {
            app.settings_visible = settings;
            app.dialogs.log = log;
            app.update_ui_fonts(&ctx);
            assert_eq!(app.ui_fonts_active, needed);
            let _ = ctx.run(egui::RawInput::default(), |ctx| {
                if std::path::Path::new("C:/Windows/Fonts/YuGothR.ttc").is_file() {
                    ctx.fonts(|f| {
                        assert_eq!(f.has_glyph(&egui::FontId::default(), '語'), needed);
                    });
                }
            });
        }
    }

    #[cfg(windows)]
    #[test]
    fn maintenance_exit_preserves_unsaved_drafts_and_enabled_preference() {
        let directory = tempfile::tempdir().unwrap();
        let store = Store::new(directory.path());
        let mut app = App::with_store(store.clone());
        app.resident = true;
        let ctx = egui::Context::default();
        app.settings.set("LiveConversion", json!(false)).unwrap();
        let _ = ctx.run(egui::RawInput::default(), |ctx| app.maintenance_quit(ctx));
        assert!(!app.closing);
        assert!(!app.settings.boolean("LiveConversion"), "draft lost");
        assert!(!directory.path().join("config.json").exists());
        app.settings = app.opened.clone();
        let _ = ctx.run(egui::RawInput::default(), |ctx| app.maintenance_quit(ctx));
        assert!(app.closing);
        assert!(store.settings().unwrap().boolean("Enabled"));
        assert!(
            !directory.path().join("config.json").exists(),
            "maintenance exit must not change settings"
        );
    }
    #[test]
    fn all_added_fields_have_target_labels_help_and_typed_controls() {
        let inventory: Value =
            serde_json::from_str(include_str!("../../../tools/parity/pre1/inventory.json"))
                .unwrap();
        let mut count = 0;
        for field in inventory["missing_settings"].as_array().unwrap() {
            if field["first_present_in_requested_release"] == "v1.3.1-pre.1" {
                continue;
            }
            let key = field["key"].as_str().unwrap();
            if key == "ToggleKeyboardShortcuts" {
                let metadata = metadata::field("ToggleShortcuts").expect("1.4 shortcut list");
                assert!(!metadata["label"].as_str().unwrap().is_empty());
                assert!(!metadata["description"].as_str().unwrap().is_empty());
                assert!(Settings::default().0["ToggleShortcuts"].is_array());
                assert!(visible_by_default("ToggleShortcuts"));
                count += 1;
                continue;
            }
            let metadata = metadata::field(key).expect(key);
            assert!(!metadata["label"].as_str().unwrap().is_empty(), "{key}");
            assert!(
                !metadata["description"].as_str().unwrap().is_empty(),
                "{key}"
            );
            let default = Settings::default().0[key].clone();
            assert!(
                default.is_boolean() || default.is_string(),
                "control type {key}"
            );
            assert!(
                visible_by_default(key) || !cfg!(windows) && key == "CursorIntoBrackets",
                "missing row {key}"
            );
            if cfg!(windows) || !["CursorIntoBrackets"].contains(&key) {
                assert!(supported(key), "hidden {key}");
            }
            count += 1;
        }
        assert_eq!(count, 28);
    }
    #[test]
    fn welcome_state_respects_saved_acknowledgement() {
        let mut settings = Settings::default();
        assert!(lifecycle::Lifecycle::new(&settings).welcome);
        settings.set("WelcomeShown", json!(true)).unwrap();
        assert!(
            lifecycle::Lifecycle::new(&settings).welcome,
            "TSF introduction is still pending"
        );
        settings.set("TsfIntroduced", json!(true)).unwrap();
        assert!(!lifecycle::Lifecycle::new(&settings).welcome);
        let migrated =
            Settings::parse(r#"{"Mode":"AutoSwitch","WelcomeShown":true,"TsfIntroduced":true}"#)
                .unwrap();
        assert!(
            lifecycle::Lifecycle::new(&migrated).welcome,
            "legacy AutoSwitch must restart the TSF introduction"
        );
    }
    #[test]
    fn window_edits_are_drafts_and_save_merges_only_changed_fields() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path());
        let mut app = App::with_store(store.clone());
        app.action(json!({"op":"settings_set","name":"LiveConversion","value":false}));
        assert!(!app.settings.boolean("LiveConversion"));
        assert!(store.settings().unwrap().boolean("LiveConversion"));
        assert!(!dir.path().join("config.json").exists());
        store
            .change_settings(|s| s.set("Enabled", json!(false)))
            .unwrap();
        assert!(app.save());
        let saved = store.settings().unwrap();
        assert!(!saved.boolean("LiveConversion"));
        assert!(!saved.boolean("Enabled"));
        app.action(json!({"op":"settings_set","name":"LiveConversion","value":true}));
        drop(app); // Closing without save (Cancel) must not flush a draft.
        assert!(!store.settings().unwrap().boolean("LiveConversion"));
    }
    #[test]
    fn dictionary_actions_do_not_replace_unsaved_settings_and_test_input_does_not_learn_on_disk() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path());
        let mut app = App::with_store(store.clone());
        app.action(json!({"op":"settings_set","name":"LiveConversion","value":false}));
        app.action(json!({"op":"dictionary_list"}));
        assert!(!app.settings.boolean("LiveConversion"));
        app.action(json!({"op":"profile_add","name":"仕事"}));
        assert_eq!(app.settings.text("ActiveProfile"), "仕事");
        assert_eq!(store.settings().unwrap().text("ActiveProfile"), "標準");
        for c in "hashi".chars() {
            app.test_session.key(0, c as i32, 0);
        }
        app.test_session.key(32, 32, 0);
        app.test_session.select(0);
        assert!(!dir.path().join("state.json").exists());
    }
    #[cfg(windows)]
    #[test]
    fn cancel_in_resident_mode_discards_the_draft_before_hiding() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path());
        let mut app = App::with_store(store.clone());
        app.resident = true;
        app.action(json!({"op":"settings_set","name":"LiveConversion","value":false}));
        app.action(json!({"op":"profile_add","name":"未保存"}));
        let ctx = egui::Context::default();
        let _ = ctx.run(egui::RawInput::default(), |ctx| app.finish(ctx, true));
        assert_eq!(app.settings.0, store.settings().unwrap().0);
        assert_eq!(app.opened.0, app.settings.0);
        assert!(!app.closing);
        assert!(!dir.path().join("config.json").exists());
    }
    #[test]
    fn restoring_backup_replaces_the_open_draft_and_cannot_be_undone_by_saving_the_window() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path().join("data"));
        store
            .change_settings(|s| s.set("Enabled", json!(false)))
            .unwrap();
        let backup = dir.path().join("settings.meltype-backup");
        std::fs::write(&backup, store.backup().unwrap()).unwrap();
        store
            .change_settings(|s| s.set("Enabled", json!(true)))
            .unwrap();
        let mut app = App::with_store(store.clone());
        app.action(
            json!({"op":"settings_set","name":"Punctuation","value":"FullWidthCommaPeriod"}),
        );
        app.restore_backup(&backup);
        let restored = store.settings().unwrap();
        assert!(!restored.boolean("Enabled"));
        assert_eq!(app.settings.0, restored.0);
        assert_eq!(app.opened.0, restored.0);
        assert_eq!(app.test_session.settings.0, restored.0);
        assert!(app.save());
        assert_eq!(store.settings().unwrap().0, restored.0);
        let preserved = Store::new(dir.path().join("before"));
        std::fs::create_dir_all(&preserved.directory).unwrap();
        std::fs::copy(
            store.directory.join("config.json.before-restore"),
            preserved.directory.join("config.json"),
        )
        .unwrap();
        assert!(preserved.settings().unwrap().boolean("Enabled"));
        app.restore_backup(&dir.path().join("missing"));
        assert!(app.result.is_null());
        assert_eq!(app.settings.0, restored.0);
    }
}
