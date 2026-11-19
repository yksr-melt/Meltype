// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
mod ffi;
use ffi::*;
use meltype_core::session::{SessionResult, View};
use meltype_native as native;
use std::{
    cell::RefCell,
    ffi::{c_char, c_void, CStr, CString},
    panic::{catch_unwind, AssertUnwindSafe},
    ptr,
};

const KEY: &CStr = c"meltype-flash-state";
const ENGINE: &CStr = c"meltype-flash";
const BUS: &CStr = c"org.freedesktop.IBus.MeltypeFlash";

struct State {
    session: Obj,
    direct: bool,
    private: bool,
    no_learning: bool,
    terminal: bool,
    preedit: bool,
    caps: u32,
    selected: usize,
    predicting: bool,
    count: usize,
    timer: u32,
    async_timer: u32,
}
impl Drop for State {
    fn drop(&mut self) {
        if self.async_timer != 0 {
            unsafe {
                g_source_remove(self.async_timer);
            }
        }
        native::meltype_destroy(self.session);
    }
}
fn with<R: Default>(engine: Obj, f: impl FnOnce(&mut State) -> R) -> R {
    catch_unwind(AssertUnwindSafe(|| unsafe {
        let cell = g_object_get_data(engine, KEY.as_ptr()).cast::<RefCell<State>>();
        if cell.is_null() {
            return R::default();
        }
        let Ok(mut state) = (*cell).try_borrow_mut() else {
            return R::default();
        };
        f(&mut state)
    }))
    .unwrap_or_default()
}
unsafe fn result(pointer: *mut c_char) -> Option<SessionResult> {
    if pointer.is_null() {
        return None;
    }
    let parsed = serde_json::from_slice(CStr::from_ptr(pointer).to_bytes()).ok();
    native::meltype_free(pointer.cast());
    parsed
}
fn text(value: &str) -> Obj {
    // IBus's text ABI is NUL-terminated; never silently truncate a commit.
    let value = CString::new(value.replace('\0', "�")).unwrap();
    unsafe { ibus_text_new_from_string(value.as_ptr()) }
}
unsafe extern "C" fn free_state(data: Obj) {
    drop(Box::from_raw(data.cast::<RefCell<State>>()));
}
unsafe fn connect(engine: Obj, signal: &CStr, callback: *const c_void) {
    g_signal_connect_data(engine, signal.as_ptr(), callback, ptr::null_mut(), None, 0);
}
unsafe extern "C" fn initialize(engine: Obj, _class: Obj) {
    let _ = catch_unwind(AssertUnwindSafe(|| {
        let state = State {
            session: native::meltype_create(),
            direct: false,
            private: false,
            no_learning: false,
            terminal: false,
            preedit: false,
            caps: 0,
            selected: 0,
            predicting: false,
            count: 0,
            timer: 0,
            async_timer: 0,
        };
        native::meltype_set_direct_detection(state.session, 1);
        native::meltype_set_can_move_caret(state.session, 0);
        g_object_set_data_full(
            engine,
            KEY.as_ptr(),
            Box::into_raw(Box::new(RefCell::new(state))).cast(),
            Some(free_state),
        );
        connect(engine, c"process-key-event", process_key as *const c_void);
        for signal in [c"focus-out", c"reset", c"disable"] {
            connect(engine, signal, flush as *const c_void);
        }
        connect(engine, c"focus-in", focus_in as *const c_void);
        if g_signal_lookup(c"focus-in-id".as_ptr(), ibus_engine_get_type()) != 0 {
            connect(engine, c"focus-in-id", focus_in_id as *const c_void);
            connect(engine, c"focus-out-id", focus_out_id as *const c_void);
        }
        connect(engine, c"set-capabilities", capabilities as *const c_void);
        connect(engine, c"set-content-type", content_type as *const c_void);
        connect(engine, c"candidate-clicked", candidate as *const c_void);
        connect(engine, c"cursor-up", previous as *const c_void);
        connect(engine, c"cursor-down", next as *const c_void);
        connect(engine, c"page-up", page_up as *const c_void);
        connect(engine, c"page-down", page_down as *const c_void);
        connect(engine, c"property-activate", property as *const c_void);
        connect(engine, c"destroy", destroy as *const c_void);
    }));
}
impl State {
    fn sync_attributes(&self) {
        let flags = u32::from(self.private)
            | (u32::from(self.no_learning) << 1)
            | (u32::from(self.terminal) << 2);
        native::meltype_set_input_attributes(self.session, flags);
        // Synchronous-only clients explicitly forbid forwarded key events.
        native::meltype_set_can_move_caret(
            self.session,
            i32::from(self.caps & 8 != 0 && self.caps & (1 << 7) == 0),
        );
    }
    fn cancel_timer(&mut self) {
        if self.timer != 0 {
            unsafe {
                g_source_remove(self.timer);
            }
            self.timer = 0;
        }
    }
    fn cancel_async_timer(&mut self) {
        if self.async_timer != 0 {
            unsafe {
                g_source_remove(self.async_timer);
            }
            self.async_timer = 0;
        }
    }
    fn schedule_async_poll(&mut self, engine: Obj) {
        if self.async_timer != 0 {
            return;
        }
        let poll = Box::new(AsyncPoll {
            engine: unsafe { g_object_ref(engine) },
            session: self.session,
        });
        self.async_timer = unsafe {
            g_timeout_add_full(
                0,
                10,
                Some(poll_async),
                Box::into_raw(poll).cast(),
                Some(free_async),
            )
        };
    }
    fn hide(&mut self, engine: Obj) {
        self.cancel_timer();
        self.cancel_async_timer();
        unsafe {
            if self.preedit {
                ibus_engine_update_preedit_text_with_mode(engine, text(""), 0, 0, 0);
            }
            ibus_engine_hide_lookup_table(engine);
            ibus_engine_hide_auxiliary_text(engine);
        }
        self.preedit = false;
        self.count = 0;
    }
    fn surrounding(&self, engine: Obj) -> Option<(String, String)> {
        if self.private || self.caps & (1 << 5) == 0 {
            return None;
        }
        unsafe {
            let mut value = ptr::null_mut();
            let mut cursor = 0;
            let mut anchor = 0;
            ibus_engine_get_surrounding_text(engine, &mut value, &mut cursor, &mut anchor);
            if value.is_null() {
                return None;
            }
            let raw = ibus_text_get_text(value);
            let value_text = if raw.is_null() {
                String::new()
            } else {
                CStr::from_ptr(raw).to_string_lossy().into_owned()
            };
            g_object_unref(value);
            // Editing nonempty selections cannot use backward-only edits.
            if cursor != anchor || cursor as usize > value_text.chars().count() {
                return None;
            }
            let before: String = value_text.chars().take(cursor as usize).collect();
            let after: String = value_text
                .chars()
                .skip(cursor as usize)
                .take(4000)
                .collect();
            let before: String = before
                .chars()
                .rev()
                .take(4000)
                .collect::<String>()
                .chars()
                .rev()
                .collect();
            Some((before, after))
        }
    }
    fn apply(&mut self, engine: Obj, result: Option<SessionResult>) {
        let Some(result) = result else {
            return;
        };
        for edit in result.commits {
            if edit.caret_back != 0
                && (edit.caret_back != 1
                    || !meltype_core::shortcuts::is_bracket_pair(&edit.text)
                    || self.caps & 8 == 0
                    || self.caps & (1 << 7) != 0)
            {
                unsafe {
                    result_free(native::meltype_reset(self.session));
                }
                self.hide(engine);
                return;
            }
            self.hide(engine);
            if edit.delete_before > 0 {
                let expected = edit.expect.as_deref().unwrap_or("");
                if expected.is_empty()
                    || expected.encode_utf16().count() != edit.delete_before
                    || !self
                        .surrounding(engine)
                        .is_some_and(|(before, _)| before.ends_with(expected))
                {
                    unsafe {
                        result_free(native::meltype_reset(self.session));
                    }
                    return;
                }
                unsafe {
                    ibus_engine_delete_surrounding_text(
                        engine,
                        -(expected.chars().count() as i32),
                        expected.chars().count() as u32,
                    );
                }
            }
            if !edit.text.is_empty() {
                unsafe {
                    ibus_engine_commit_text(engine, text(&edit.text));
                    if edit.caret_back != 0 {
                        ibus_engine_forward_key_event(engine, 0xff51, 0, 0);
                        ibus_engine_forward_key_event(engine, 0xff51, 0, 1 << 30);
                    }
                }
            }
        }
        if let Some(view) = result.view {
            self.render(engine, view);
        } else {
            self.hide(engine);
        }
    }
    fn render(&mut self, engine: Obj, view: View) {
        self.cancel_timer();
        unsafe {
            let preedit = text(&view.text);
            if view.converting && !view.clauses.is_empty() {
                let mut offset = 0;
                for (index, clause) in view.clauses.iter().enumerate() {
                    let end = offset + clause.chars().count() as u32;
                    ibus_text_append_attribute(preedit, 1, 1, offset, end as i32);
                    if index == view.selected_clause {
                        ibus_text_append_attribute(preedit, 3, 0x4ca0ff, offset, end as i32);
                        ibus_text_append_attribute(preedit, 2, 0xffffff, offset, end as i32);
                    }
                    offset = end;
                }
            } else {
                ibus_text_append_attribute(preedit, 1, 1, 0, view.text.chars().count() as i32);
            }
            self.preedit = !view.text.is_empty();
            ibus_engine_update_preedit_text_with_mode(
                engine,
                preedit,
                view.text.chars().count() as u32,
                self.preedit as i32,
                1,
            );
            let hint = match view.suggestion.as_deref() {
                Some(s) => format!("{s}　{}", view.hint),
                None => view.hint.clone(),
            };
            if hint.is_empty() {
                ibus_engine_hide_auxiliary_text(engine);
            } else {
                ibus_engine_update_auxiliary_text(engine, text(&hint), 1);
            }
            self.predicting = !view.converting && view.candidates.len() <= 1;
            let candidates = if self.predicting {
                &view.predictions
            } else {
                &view.candidates
            };
            self.selected = if self.predicting {
                view.selected_prediction.max(0) as usize
            } else {
                view.selected_index
            };
            self.count = candidates.len();
            if self.count > usize::from(!self.predicting) {
                let table =
                    ibus_lookup_table_new(9, self.selected.min(self.count - 1) as u32, 1, 1);
                ibus_lookup_table_set_orientation(table, 1);
                for (i, candidate) in candidates.iter().enumerate() {
                    let annotation = if self.predicting {
                        None
                    } else {
                        view.notes.get(i).and_then(|n| n.as_deref())
                    };
                    let value = annotation
                        .map_or_else(|| candidate.clone(), |note| format!("{candidate}  {note}"));
                    ibus_lookup_table_append_candidate(table, text(&value));
                }
                ibus_engine_update_lookup_table(engine, table, 1);
                if let Some(meaning) = view.meaning.filter(|s| !s.is_empty()) {
                    let timer = Box::new(Meaning {
                        engine: g_object_ref(engine),
                        text: meaning,
                    });
                    self.timer = g_timeout_add_full(
                        0,
                        1500,
                        Some(show_meaning),
                        Box::into_raw(timer).cast(),
                        Some(free_meaning),
                    );
                }
            } else {
                ibus_engine_hide_lookup_table(engine);
            }
        }
    }
    fn key(&mut self, engine: Obj, vk: i32, ch: i32, modifiers: i32) -> bool {
        if native::meltype_async_enabled(self.session) != 0
            && native::meltype_async_can_begin(self.session) != 0
            && matches!(vk, 0x20 | 0x1c)
        {
            native::meltype_begin_async_from_session(self.session);
            self.schedule_async_poll(engine);
            return true;
        }
        self.sync_attributes();
        let surrounding = if self.preedit {
            None
        } else {
            self.surrounding(engine)
        };
        let before = surrounding
            .as_ref()
            .and_then(|(b, _)| CString::new(b.as_str()).ok());
        let after = surrounding
            .as_ref()
            .and_then(|(_, a)| CString::new(a.as_str()).ok());
        // Preedit keeps the starting context; every returned deletion is checked
        // against a new selection-free snapshot in apply() before it is sent.
        let can_delete = if self.preedit {
            !self.private && self.caps & (1 << 5) != 0
        } else {
            surrounding.is_some()
        };
        native::meltype_set_can_delete(self.session, i32::from(can_delete));
        let response = unsafe {
            result(native::meltype_handle_key(
                self.session,
                vk,
                ch,
                modifiers,
                before.as_ref().map_or(ptr::null(), |s| s.as_ptr()),
                after.as_ref().map_or(ptr::null(), |s| s.as_ptr()),
            ))
        };
        let consumed = response.as_ref().is_some_and(|r| r.consumed);
        self.apply(engine, response);
        consumed
    }
}
unsafe fn result_free(pointer: *mut c_char) {
    if !pointer.is_null() {
        native::meltype_free(pointer.cast());
    }
}
struct Meaning {
    engine: Obj,
    text: String,
}
unsafe extern "C" fn show_meaning(data: Obj) -> i32 {
    let meaning = &*data.cast::<Meaning>();
    with(meaning.engine, |s| {
        s.timer = 0;
        if s.count > 0 {
            ibus_engine_update_auxiliary_text(meaning.engine, text(&meaning.text), 1);
        }
    });
    0
}
unsafe extern "C" fn free_meaning(data: Obj) {
    let meaning = Box::from_raw(data.cast::<Meaning>());
    g_object_unref(meaning.engine);
}
struct AsyncPoll {
    engine: Obj,
    session: Obj,
}
unsafe extern "C" fn poll_async(data: Obj) -> i32 {
    let poll = &*data.cast::<AsyncPoll>();
    if native::meltype_async_ready(poll.session) != 0 {
        let response = result(native::meltype_poll_async_conversion(poll.session));
        with(poll.engine, |s| {
            s.async_timer = 0;
            if let Some(response) = response {
                s.apply(poll.engine, Some(response));
            }
            if native::meltype_async_pending(poll.session) != 0 {
                s.schedule_async_poll(poll.engine);
            }
        });
    } else if native::meltype_async_pending(poll.session) != 0 {
        with(poll.engine, |s| {
            s.async_timer = 0;
            s.schedule_async_poll(poll.engine);
        });
    } else {
        with(poll.engine, |s| {
            s.async_timer = 0;
        });
    }
    0
}
unsafe extern "C" fn free_async(data: Obj) {
    let poll = Box::from_raw(data.cast::<AsyncPoll>());
    g_object_unref(poll.engine);
}
unsafe extern "C" fn process_key(
    engine: Obj,
    keyval: u32,
    _keycode: u32,
    state: u32,
    _data: Obj,
) -> i32 {
    if state & (1 << 30) != 0 {
        // IBus reports key release with IBUS_RELEASE_MASK. Forward it to the
        // core so NICOLA press/release-order chording and thumb taps work.
        with(engine, |s| {
            if s.session.is_null() || s.private {
                return 0;
            }
            if let Some((vk, ch)) = crate::keys::translate(keyval, ibus_keyval_to_unicode(keyval)) {
                result_free(native::meltype_handle_key_up(
                    s.session,
                    vk,
                    ch,
                    crate::keys::modifiers(state),
                ));
            }
            0
        });
        return 0;
    }
    with(engine, |s| {
        if s.session.is_null() || s.private {
            return 0;
        }
        if !s.direct {
            let role = match keyval {
                0xff23 => Some(0x1c),
                0xff22 => Some(0x1d),
                0xff27 => Some(0x15),
                _ => None,
            };
            if let Some(vk) = role {
                let pointer = native::meltype_role_key(s.session, vk);
                if !pointer.is_null() {
                    s.apply(engine, result(pointer));
                    return 1;
                }
            }
        }
        if let Some(vk) = crate::keys::mode_key(keyval, ibus_keyval_to_unicode(keyval), state) {
            let pointer = native::meltype_mode_key(s.session, vk, crate::keys::modifiers(state));
            if !pointer.is_null() {
                s.apply(engine, result(pointer));
                s.direct = native::meltype_input_mode(s.session) == 1;
                return 1;
            }
        }
        match crate::keys::translate(keyval, ibus_keyval_to_unicode(keyval)) {
            Some((vk, ch)) => s.key(engine, vk, ch, crate::keys::modifiers(state)) as i32,
            None => 0,
        }
    })
}
unsafe extern "C" fn flush(engine: Obj, _data: Obj) {
    with(engine, |s| {
        // IBus's COMMIT focus mode commits the displayed text before it sends
        // FocusOut/Reset. Learn that text, but do not insert it a second time.
        result_free(native::meltype_commit_preedit(s.session));
        s.hide(engine);
        result_free(native::meltype_reset(s.session));
        native::meltype_caret_moved(s.session, 0, 0);
        s.direct = native::meltype_input_mode(s.session) == 1;
    });
}
unsafe extern "C" fn capabilities(engine: Obj, caps: u32, _data: Obj) {
    with(engine, |s| {
        s.caps = caps;
        s.sync_attributes();
    });
}
unsafe extern "C" fn content_type(engine: Obj, purpose: u32, hints: u32, _data: Obj) {
    with(engine, |s| {
        // IBus DIGITS, NUMBER, PHONE, EMAIL, PASSWORD and PIN expect literal input.
        let private = matches!(purpose, 2 | 3 | 4 | 6 | 8 | 9) || hints & (1 << 12) != 0;
        let no_learning = private || hints & (1 << 11) != 0;
        if private != s.private || no_learning != s.no_learning {
            s.hide(engine);
            result_free(native::meltype_reset(s.session));
        }
        s.private = private;
        s.no_learning = no_learning;
        s.terminal = purpose == 10;
        s.sync_attributes();
    });
}
unsafe extern "C" fn candidate(engine: Obj, index: u32, _button: u32, _state: u32, _data: Obj) {
    with(engine, |s| {
        s.sync_attributes();
        let index = s.selected / 9 * 9 + index as usize;
        if index >= s.count {
            return;
        }
        let pointer = if s.predicting {
            native::meltype_select_prediction(s.session, index as i32)
        } else {
            native::meltype_select_candidate(s.session, index as i32)
        };
        s.apply(engine, result(pointer));
    });
}
unsafe extern "C" fn previous(engine: Obj, _data: Obj) {
    with(engine, |s| {
        s.key(engine, 0x26, 0, 0);
    });
}
unsafe extern "C" fn next(engine: Obj, _data: Obj) {
    with(engine, |s| {
        s.key(engine, 0x28, 0, 0);
    });
}
unsafe extern "C" fn page_up(engine: Obj, _data: Obj) {
    with(engine, |s| {
        for _ in 0..9 {
            s.key(engine, 0x26, 0, 0);
        }
    });
}
unsafe extern "C" fn page_down(engine: Obj, _data: Obj) {
    with(engine, |s| {
        for _ in 0..9 {
            s.key(engine, 0x28, 0, 0);
        }
    });
}
unsafe extern "C" fn focus_in(engine: Obj, _data: Obj) {
    with(engine, |s| {
        native::meltype_caret_moved(s.session, 0, 0);
        s.direct = native::meltype_input_mode(s.session) == 1;
    });
    let _ = catch_unwind(AssertUnwindSafe(|| {
        let properties = ibus_prop_list_new();
        for (key, label) in [
            (c"settings", "設定..."),
            (c"report", "不具合の報告・提案..."),
        ] {
            let property = ibus_property_new(
                key.as_ptr(),
                0,
                text(label),
                c"".as_ptr(),
                text(label),
                1,
                1,
                0,
                ptr::null_mut(),
            );
            ibus_prop_list_append(properties, property);
        }
        ibus_engine_register_properties(engine, properties);
    }));
}
unsafe extern "C" fn focus_in_id(
    engine: Obj,
    _path: *const c_char,
    client: *const c_char,
    data: Obj,
) {
    with(engine, |state| {
        let client = if client.is_null() {
            ""
        } else {
            CStr::from_ptr(client).to_str().unwrap_or("")
        };
        let identity = meltype_core::app_identity::ibus_application(client);
        let identity = CString::new(identity).unwrap_or_default();
        native::meltype_set_application(state.session, identity.as_ptr());
        state.sync_attributes();
    });
    focus_in(engine, data);
}
unsafe extern "C" fn focus_out_id(engine: Obj, _path: *const c_char, data: Obj) {
    flush(engine, data);
}

unsafe extern "C" fn create_engine(factory: Obj, name: *const c_char, data: Obj) -> Obj {
    catch_unwind(AssertUnwindSafe(|| {
        if name.is_null() || CStr::from_ptr(name) != ENGINE {
            return ptr::null_mut();
        }
        let type_ = data as usize;
        let class = g_type_class_ref(type_);
        let supported = !g_object_class_find_property(class, c"has-focus-id".as_ptr()).is_null();
        g_type_class_unref(class);
        if !supported {
            return ptr::null_mut();
        } // Let the old IBus factory create it.
        static NEXT: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);
        let id = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let path = CString::new(format!("/org/freedesktop/IBus/Engine/MeltypeFlash{id}")).unwrap();
        g_object_new(
            type_,
            c"engine-name".as_ptr(),
            name,
            c"object-path".as_ptr(),
            path.as_ptr(),
            c"connection".as_ptr(),
            ibus_service_get_connection(factory),
            c"has-focus-id".as_ptr(),
            1_i32,
            ptr::null::<c_char>(),
        )
    }))
    .unwrap_or(ptr::null_mut())
}
unsafe extern "C" fn property(_engine: Obj, name: *const c_char, _state: u32, _data: Obj) {
    let _ = catch_unwind(AssertUnwindSafe(|| {
        if name.is_null() {
            return;
        }
        if CStr::from_ptr(name) == c"settings" {
            if let Ok(exe) = std::env::current_exe() {
                let _ = std::process::Command::new(exe.with_file_name("meltype-flash")).spawn();
            }
        } else if CStr::from_ptr(name) == c"report" {
            let _ = std::process::Command::new("xdg-open")
                .arg("https://github.com/appipinopi/Meltype-Flash/issues/new/choose")
                .spawn();
        }
    }));
}
unsafe extern "C" fn destroy(engine: Obj, _data: Obj) {
    with(engine, |s| {
        s.cancel_timer();
        native::meltype_destroy(s.session);
        s.session = ptr::null_mut();
    });
}
unsafe extern "C" fn disconnected(_bus: Obj, loop_: Obj) {
    g_main_loop_quit(loop_);
}

pub fn run(layout: &std::ffi::CStr) -> Result<(), String> {
    // Also check on IME service startup when there is no user systemd session.
    // The worker reads AutoUpdate, serializes checks, and limits them to daily.
    let root = meltype_core::storage::Store::default_directory();
    let worker = root.join("background-updates.py");
    let normal_profile = std::env::var_os("MELTYPE_FLASH_DATA_DIR").is_none()
        && std::env::var_os("MELTYPE_RUST_DATA_DIR").is_none();
    if (!worker.is_file() || !root.join("update-linux.py").is_file())
        && normal_profile
        && meltype_core::storage::Store::new(&root)
            .settings()
            .is_ok_and(|settings| settings.boolean("AutoUpdate"))
    {
        let _ = meltype_core::storage::atomic_write(
            &worker,
            include_bytes!("../../../linux-background-updates.py"),
        );
        let _ = meltype_core::storage::atomic_write(
            &root.join("update-linux.py"),
            include_bytes!("../../../update-linux.py"),
        );
    }
    if worker.is_file() {
        if let Ok(exe) = std::env::current_exe() {
            if let Some(directory) = exe.parent() {
                if let Ok(mut child) = std::process::Command::new("python3")
                    .arg(worker)
                    .arg("run")
                    .arg(&root)
                    .arg(directory)
                    .arg(env!("CARGO_PKG_VERSION"))
                    .stdin(std::process::Stdio::null())
                    .stdout(std::process::Stdio::null())
                    .stderr(std::process::Stdio::null())
                    .spawn()
                {
                    std::thread::spawn(move || {
                        let _ = child.wait();
                    });
                }
            }
        }
    }

    unsafe {
        ibus_init();
        let bus = ibus_bus_new();
        if bus.is_null() || ibus_bus_is_connected(bus) == 0 {
            if !bus.is_null() {
                g_object_unref(bus);
            }
            return Err("IBusにつながりません。".into());
        }
        let mut query = GTypeQuery::default();
        g_type_query(ibus_engine_get_type(), &mut query);
        let type_ = g_type_register_static_simple(
            ibus_engine_get_type(),
            c"MeltypeFlashEngine".as_ptr(),
            query.class_size,
            None,
            query.instance_size,
            Some(initialize),
            0,
        );
        let factory = ibus_factory_new(ibus_bus_get_connection(bus));
        ibus_factory_add_engine(factory, ENGINE.as_ptr(), type_);
        g_signal_connect_data(
            factory,
            c"create-engine".as_ptr(),
            create_engine as *const c_void,
            type_ as Obj,
            None,
            0,
        );
        if let Ok(exe) = std::env::current_exe() {
            let helper = std::env::var_os("MELTYPE_MOZC_HELPER")
                .map(std::path::PathBuf::from)
                .unwrap_or_else(|| exe.with_file_name("mozc").join("meltype_mozc_helper"));
            if helper.is_file() {
                if let Ok(path) = CString::new(helper.to_string_lossy().as_ref()) {
                    if native::meltype_init_mozc(path.as_ptr(), ptr::null()) == 0 {
                        eprintln!("Meltype: Mozcを起動できませんでした。");
                    }
                }
            }
        }
        if std::env::args().any(|a| a == "--ibus") {
            if ibus_bus_request_name(bus, BUS.as_ptr(), 4) != 1 {
                g_object_unref(factory);
                g_object_unref(bus);
                return Err("IBusの名前を取得できません。".into());
            }
        } else {
            let version = CString::new(env!("CARGO_PKG_VERSION")).unwrap();
            let component = ibus_component_new(
                BUS.as_ptr(),
                c"Meltype-FLASH".as_ptr(),
                version.as_ptr(),
                c"GPL-3.0-or-later".as_ptr(),
                c"Yukishiro, appipinopi".as_ptr(),
                c"https://github.com/appipinopi/Meltype-Flash".as_ptr(),
                c"".as_ptr(),
                c"".as_ptr(),
            );
            g_object_ref_sink(component);
            ibus_component_add_engine(
                component,
                ibus_engine_desc_new(
                    ENGINE.as_ptr(),
                    c"Meltype-FLASH".as_ptr(),
                    c"Meltype-FLASH".as_ptr(),
                    c"ja".as_ptr(),
                    c"GPL-3.0-or-later".as_ptr(),
                    c"Yukishiro, appipinopi".as_ptr(),
                    c"".as_ptr(),
                    layout.as_ptr(),
                ),
            );
            let registered = ibus_bus_register_component(bus, component);
            g_object_unref(component);
            if registered == 0 {
                g_object_unref(factory);
                g_object_unref(bus);
                return Err("IBusへ登録できません。".into());
            }
        }
        let loop_ = g_main_loop_new(ptr::null_mut(), 0);
        g_signal_connect_data(
            bus,
            c"disconnected".as_ptr(),
            disconnected as *const c_void,
            loop_,
            None,
            0,
        );
        g_main_loop_run(loop_);
        g_object_unref(factory);
        g_object_unref(bus);
        g_main_loop_unref(loop_);
    }
    Ok(())
}
