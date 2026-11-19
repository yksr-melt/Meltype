// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! In-process adapter for upstream's TSF JSON protocol. Each frontend owns a
//! connection; session IDs cannot address sessions in other connections.
use super::*;
use meltype_core::{settings::Settings, storage::Store};
use serde_json::{json, Value};

struct AppSettings {
    directory: std::path::PathBuf,
    process: String,
    bytes: Option<Vec<u8>>,
    settings: std::rc::Rc<Settings>,
}

fn config_bytes(store: &Store) -> std::io::Result<Option<Vec<u8>>> {
    match std::fs::read(store.directory.join("config.json")) {
        Ok(bytes) => Ok(Some(bytes)),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(error) => Err(error),
    }
}

fn app_settings(
    cache: &mut Option<AppSettings>,
    store: &Store,
    process: &str,
) -> std::rc::Rc<Settings> {
    let bytes = config_bytes(store);
    if let (Ok(current), Some(entry)) = (&bytes, cache.as_ref()) {
        if entry.directory == store.directory && entry.process == process && entry.bytes == *current
        {
            return entry.settings.clone();
        }
    }
    let loaded = store.settings();
    let valid = loaded.is_ok();
    let settings = std::rc::Rc::new(loaded.unwrap_or_default().for_app(process));
    // Settings migration or a parallel edit may replace config.json while loading.
    if let Ok(bytes) = bytes {
        if valid && config_bytes(store).is_ok_and(|current| current == bytes) {
            *cache = Some(AppSettings {
                directory: store.directory.clone(),
                process: process.into(),
                bytes,
                settings: settings.clone(),
            });
        }
    }
    settings
}

static CONNECTIONS: OnceLock<Mutex<HashMap<usize, HashMap<String, usize>>>> = OnceLock::new();
fn connections() -> &'static Mutex<HashMap<usize, HashMap<String, usize>>> {
    CONNECTIONS.get_or_init(|| Mutex::new(HashMap::new()))
}
#[no_mangle]
pub extern "C" fn meltype_tip_create() -> *mut c_void {
    let Ok(mut all) = connections().lock() else {
        return std::ptr::null_mut();
    };
    let id = NEXT.fetch_add(1, Ordering::Relaxed);
    all.insert(id, HashMap::new());
    id as *mut c_void
}
#[no_mangle]
pub extern "C" fn meltype_tip_destroy(handle: *mut c_void) {
    #[cfg(not(test))]
    flush_key_count();
    if let Ok(mut all) = connections().lock() {
        if let Some(sessions) = all.remove(&(handle as usize)) {
            for (_, id) in sessions {
                meltype_destroy(id as *mut c_void);
            }
        }
    }
}
/// # Safety
/// request must be a valid NUL-terminated UTF-8 JSON string of at most 64 KiB.
#[no_mangle]
pub unsafe extern "C" fn meltype_tip_request(
    connection: *mut c_void,
    request: *const c_char,
) -> *mut c_char {
    catch_unwind(AssertUnwindSafe(|| request_inner(connection, request)))
        .ok()
        .flatten()
        .map(|s| allocate(&s))
        .unwrap_or(std::ptr::null_mut())
}
unsafe fn request_inner(connection: *mut c_void, request: *const c_char) -> Option<String> {
    let text = read(request)?;
    if text.len() > 65536 {
        return None;
    }
    let request: Value = serde_json::from_str(&text).ok()?;
    let mut all = connections().lock().ok()?;
    let sessions = all.get_mut(&(connection as usize))?;
    let inactive = || json!({"active":false,"consumed":false,"commits":[],"view":null}).to_string();
    let op = request["op"].as_str()?;
    #[cfg(not(test))]
    let settings = {
        thread_local! { static LAST: std::cell::RefCell<Option<AppSettings>> = const { std::cell::RefCell::new(None) }; }
        let store = Store::new(Store::default_directory());
        LAST.with_borrow_mut(|cache| {
            app_settings(cache, &store, request["process"].as_str().unwrap_or(""))
        })
    };
    #[cfg(test)]
    let settings = meltype_core::settings::Settings::default()
        .for_app(request["process"].as_str().unwrap_or(""));
    let code = settings.text("AppProfile") == "Code";
    #[cfg(windows)]
    let _engine = crate::windows_converter::scope(&settings);
    let direct_detection = settings.boolean("DirectModeAutoDetect") && !code;
    if op == "hello" {
        if !settings.boolean("Enabled") {
            return Some(inactive());
        }
        return Some(json!({"active":true,"code":code,"directDetection":direct_detection,"fullWidthSpace":settings.boolean("FullWidthSpaceByDefault"),"consumed":false,"commits":[],"view":null}).to_string());
    }
    let sid = request["sid"].as_str()?;
    if sid.len() > 256 {
        return None;
    }
    if op == "close" {
        if let Some(id) = sessions.remove(sid) {
            meltype_destroy(id as *mut c_void);
        }
        return Some(inactive());
    }
    if !sessions.contains_key(sid) {
        if request["composing"] == true
            || sessions.len() >= 16
            || ![
                "key",
                "mode",
                "toggle",
                "reconvert",
                "async_begin",
                "async_complete",
                "async_cancel",
                "async_poll",
                "async_apply",
                "async_begin_session",
                "async_poll_apply",
            ]
            .contains(&op)
        {
            return Some(inactive());
        }
        let handle = meltype_create();
        if handle.is_null() {
            return None;
        }
        sessions.insert(sid.to_owned(), handle as usize);
    }
    let handle = *sessions.get(sid)? as *mut c_void;
    let number = |name: &str| {
        request[name]
            .as_i64()
            .and_then(|n| i32::try_from(n).ok())
            .unwrap_or(0)
    };
    let strings = |name: &str| -> Vec<String> {
        request[name]
            .as_array()
            .map(|values| {
                values
                    .iter()
                    .filter_map(|value| value.as_str().map(str::to_owned))
                    .collect()
            })
            .unwrap_or_default()
    };
    set(handle, |s| {
        if op.starts_with("async_") {
            return;
        }
        if op == "key" && request["is_up"] == true {
            // A release must not rewrite process/direct/caret metadata that
            // the preceding key-down established.
            return;
        }
        s.set_process(request["process"].as_str().unwrap_or(""));
        if matches!(op, "key" | "reconvert") {
            s.no_learning = request["private"] == true;
        }
        // Older TSF clients cannot notify caret movements or toggle code lines.
        s.code_input &= request["code"] == true;
        s.direct_detection = direct_detection;
        s.can_delete = request["canDelete"] == true;
        s.set_context(request["before"].as_str(), request["after"].as_str());
        if request["moved"] == true {
            s.caret_moved(0, false);
        }
        if let Some(passed) = request["passed"]
            .as_i64()
            .and_then(|n| i32::try_from(n).ok())
        {
            s.caret_moved(passed, request["ctrl"] == true);
        }
    });
    let result = match op {
        "reconvert" => {
            meltype_set_direct(handle, i32::from(request["direct"] == true));
            let text = request["text"]
                .as_str()
                .and_then(|s| CString::new(s).ok())?;
            let reading = request["reading"]
                .as_str()
                .and_then(|s| CString::new(s).ok());
            meltype_reconvert_selection(
                handle,
                text.as_ptr(),
                reading.as_ref().map_or(std::ptr::null(), |s| s.as_ptr()),
            )
        }
        "key" => {
            if request["is_up"] == true {
                // Key-up: record the release in the core session for
                // NICOLA press/release-order chording and replay. Do not
                // touch direct mode, key counting or caret metadata.
                let now_ms = request["now_ms"].as_u64().unwrap_or(0);
                run(handle, |s| {
                    s.key_up_at(number("vk"), number("ch"), number("mods"), now_ms)
                })
            } else {
                #[cfg(not(test))]
                if request["private"] != true {
                    count_key();
                }
                meltype_set_direct(handle, i32::from(request["direct"] == true));
                let before = request["before"]
                    .as_str()
                    .and_then(|s| CString::new(s).ok());
                let after = request["after"].as_str().and_then(|s| CString::new(s).ok());
                meltype_handle_key(
                    handle,
                    number("vk"),
                    number("ch"),
                    number("mods"),
                    before.as_ref().map_or(std::ptr::null(), |s| s.as_ptr()),
                    after.as_ref().map_or(std::ptr::null(), |s| s.as_ptr()),
                )
            }
        }
        "async_begin_session" => {
            let active = request["private"] != true
                && request["composing"] != true
                && request["sid"].as_str().is_some();
            run(handle, |s| {
                if active && s.settings.boolean("AsyncConversion") && !s.raw().is_empty() {
                    let reading = s.reading();
                    s.begin_async_conversion(&reading, "");
                }
                s.result(false)
            })
        }
        "async_poll_apply" => run(handle, |s| {
            if let Some(ac) = s.async_conversion() {
                let id = ac.id;
                let reading = ac.reading.clone();
                let phase = ac.phase;
                if phase == meltype_core::session::AsyncConversionPhase::Pending {
                    let candidates = s.dictionary_candidates(&reading);
                    s.complete_async_conversion(id, candidates);
                }
                if let Some(ac) = s.async_conversion() {
                    if ac.phase == meltype_core::session::AsyncConversionPhase::Ready {
                        let candidates = ac.candidates.first().cloned().unwrap_or_default();
                        s.try_apply_async_conversion(&reading, candidates);
                    }
                }
            }
            s.result(true)
        }),
        "async_begin" => {
            let reading = request["reading"].as_str().unwrap_or("").to_owned();
            let context = request["context"].as_str().unwrap_or("").to_owned();
            run(handle, |s| {
                s.begin_async_conversion(&reading, &context);
                s.result(false)
            })
        }
        "async_complete" => {
            let id = request["id"].as_u64().unwrap_or(0);
            let candidates = strings("candidates");
            run(handle, |s| {
                let ok = s.complete_async_conversion(id, candidates);
                s.result(ok)
            })
        }
        "async_cancel" => run(handle, |s| {
            s.cancel_async_conversion();
            s.result(true)
        }),
        "async_poll" => {
            let now_ms = request["now_ms"].as_u64().unwrap_or(0);
            run(handle, |s| {
                let phase = s.timeout_async_conversion(now_ms);
                s.result(phase.is_some())
            })
        }
        "async_apply" => {
            let reading = request["reading"].as_str().unwrap_or("").to_owned();
            let candidates = strings("candidates");
            run(handle, |s| {
                let ok = s.try_apply_async_conversion(&reading, candidates);
                s.result(ok)
            })
        }
        "commit" => meltype_commit(handle),
        "reset" => run(handle, |s| {
            s.reset();
            s.direct = request["direct"] == true;
            s.result(false)
        }),
        "select" => {
            if request["prediction"] == true {
                meltype_select_prediction(handle, number("index"))
            } else {
                run(handle, |s| s.select_composing(number("index") as usize))
            }
        }
        "mode" => run(handle, |s| s.result(false)),
        "toggle" => run(handle, |s| {
            let consumed = s.set_code_line(request["japanese"] == true);
            s.result(consumed)
        }),
        _ => return Some(inactive()),
    };
    let text = take(result)?;
    let mut value: Value = serde_json::from_str(&text).ok()?;
    let (active, effective_code, english, switched, english_word) = super::sessions()
        .lock()
        .ok()?
        .get(&(handle as usize))
        .map(|s| {
            (
                s.settings.boolean("Enabled"),
                s.code_input,
                s.direct || s.code_english(),
                request["direct"] == true && !s.direct,
                s.direct_english_word,
            )
        })?;
    value["active"] = json!(active);
    value["code"] = json!(effective_code);
    value["english"] = json!(english);
    value["directDetection"] = json!(direct_detection && active);
    value["fullWidthSpace"] = json!(active && settings.boolean("FullWidthSpaceByDefault"));
    value["switchToJapanese"] = json!(switched);
    value["directEnglishWord"] = json!(english_word);
    if op == "async_begin_session" {
        let active = super::sessions()
            .lock()
            .ok()
            .and_then(|all| {
                all.get(&(handle as usize)).map(|s| {
                    s.settings.boolean("AsyncConversion") && s.async_conversion().is_some()
                })
            })
            .unwrap_or(false);
        value["asyncActive"] = json!(active);
    }
    if op == "async_begin" {
        let id = super::sessions()
            .lock()
            .ok()
            .and_then(|all| {
                all.get(&(handle as usize))
                    .and_then(|s| s.async_conversion().map(|ac| ac.id))
            })
            .unwrap_or(0);
        value["asyncId"] = json!(id);
    }
    if op == "async_poll" {
        let phase = super::sessions()
            .lock()
            .ok()
            .and_then(|all| {
                all.get(&(handle as usize))
                    .and_then(|s| s.async_conversion().map(|ac| ac.phase))
            })
            .map_or("idle", |phase| match phase {
                meltype_core::session::AsyncConversionPhase::Idle => "idle",
                meltype_core::session::AsyncConversionPhase::Pending => "pending",
                meltype_core::session::AsyncConversionPhase::Ready => "ready",
                meltype_core::session::AsyncConversionPhase::Cancelled => "cancelled",
                meltype_core::session::AsyncConversionPhase::TimedOut => "timed_out",
            });
        value["asyncPhase"] = json!(phase);
    }
    Some(value.to_string())
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn selected_text_reconversion_reaches_core_and_preserves_cancel_in_direct_mode() {
        unsafe {
            let connection = meltype_tip_create();
            let call = |request: Value| -> Value {
                let request = CString::new(request.to_string()).unwrap();
                serde_json::from_str(
                    &take(meltype_tip_request(connection, request.as_ptr())).unwrap(),
                )
                .unwrap()
            };
            let result =
                call(json!({"op":"reconvert","sid":"selected","text":"nihongo","direct":true}));
            assert_eq!(result["selectionExpect"], "nihongo");
            assert_eq!(result["consumed"], true);
            assert_eq!(result["commits"], json!([]));
            assert!(result["view"]["candidates"]
                .as_array()
                .unwrap()
                .contains(&json!("nihongo")));
            let cancelled = call(json!({"op":"key","sid":"selected","vk":27,"direct":true}));
            assert_eq!(cancelled["commits"][0]["text"], "nihongo");
            assert!(cancelled["view"].is_null());
            let private =
                call(json!({"op":"reconvert","sid":"private","text":"nihongo","private":true}));
            assert_eq!(private["consumed"], true);
            assert_eq!(private["selectionExpect"], "nihongo");
            assert!(private["view"].is_object());
            meltype_tip_destroy(connection);
        }
    }
    #[test]
    fn direct_detection_requires_deletion_capability_and_switches_mode() {
        // Exclude debug-build dictionary initialization from the user's
        // inter-key interval; timeout boundaries have deterministic fixtures.
        meltype_core::detection::evaluate(
            "ka",
            &[75, 65],
            false,
            &meltype_core::settings::Settings::default(),
        );
        unsafe {
            let connection = meltype_tip_create();
            let call = |can_delete: bool| -> Value {
                let request = CString::new(json!({"op":"key","sid":"direct","process":"notepad.exe","code":true,"direct":true,"canDelete":can_delete,"before":"kon","vk":78,"ch":110}).to_string()).unwrap();
                serde_json::from_str(
                    &take(meltype_tip_request(connection, request.as_ptr())).unwrap(),
                )
                .unwrap()
            };
            assert_eq!(call(false)["consumed"], false);
            let value = call(true);
            // Text observed before this session is not replay history. The
            // original timed host also leaves this n unconsumed and deletes
            // none of the existing kon (upstream-direct-time.json).
            assert_eq!(value["consumed"], false);
            assert_eq!(value["commits"], json!([]));
            for (ch, before) in [('k', ""), ('o', "k"), ('n', "ko"), ('n', "kon")] {
                let request = CString::new(json!({"op":"key","sid":"typed","process":"notepad.exe","direct":true,"canDelete":true,"before":before,"vk":ch.to_ascii_uppercase() as i32,"ch":ch as i32}).to_string()).unwrap();
                let result: Value = serde_json::from_str(
                    &take(meltype_tip_request(connection, request.as_ptr())).unwrap(),
                )
                .unwrap();
                if before == "kon" {
                    assert_eq!(result["switchToJapanese"], true);
                    assert_eq!(result["commits"][0]["expect"], "kon");
                    assert_eq!(result["view"]["text"], "こん");
                }
            }
            meltype_tip_destroy(connection);
        }
    }
    #[test]
    fn code_toggle_and_caret_notification_reach_session() {
        unsafe {
            let connection = meltype_tip_create();
            let call = |op: &str, extra: Value| -> Value {
                let mut request = json!({"op":op,"sid":"code","process":"Code.exe","code":true,"before":"let value = "});
                request
                    .as_object_mut()
                    .unwrap()
                    .extend(extra.as_object().unwrap().clone());
                let request = CString::new(request.to_string()).unwrap();
                serde_json::from_str(
                    &take(meltype_tip_request(connection, request.as_ptr())).unwrap(),
                )
                .unwrap()
            };
            assert_eq!(call("mode", json!({}))["english"], true);
            let result = call("toggle", json!({"japanese":true}));
            assert_eq!(result["consumed"], true);
            assert_eq!(result["english"], false);
            assert_eq!(call("mode", json!({"passed":37}))["english"], true);
            assert_eq!(
                call("mode", json!({"before":"// comment "}))["english"],
                false
            );
            meltype_tip_destroy(connection);
        }
    }
    #[test]
    fn async_conversion_ops_round_trip_and_timeout() {
        unsafe {
            let connection = meltype_tip_create();
            let call = |request: Value| -> Value {
                let request = CString::new(request.to_string()).unwrap();
                serde_json::from_str(
                    &take(meltype_tip_request(connection, request.as_ptr())).unwrap(),
                )
                .unwrap()
            };
            for c in "hashi".chars() {
                call(json!({"op":"key","sid":"async","vk":0,"ch":c as i32}));
            }
            let begin = call(json!({
                "op":"async_begin","sid":"async","reading":"はし","context":""
            }));
            let id = begin["asyncId"].as_u64().unwrap();
            assert!(id > 0);
            let complete = call(json!({
                "op":"async_complete","sid":"async","id":id,"candidates":["橋","箸"]
            }));
            assert_eq!(complete["consumed"], true);
            let apply = call(json!({
                "op":"async_apply","sid":"async","reading":"はし","candidates":["橋","箸"]
            }));
            assert_eq!(apply["consumed"], true);
            assert_eq!(apply["view"]["converting"], true);

            let begin = call(json!({
                "op":"async_begin","sid":"async","reading":"かき","context":""
            }));
            let id = begin["asyncId"].as_u64().unwrap();
            let poll = call(json!({
                "op":"async_poll","sid":"async","now_ms":1000000
            }));
            assert_eq!(poll["asyncPhase"], "timed_out");
            let complete = call(json!({
                "op":"async_complete","sid":"async","id":id,"candidates":["柿"]
            }));
            assert_eq!(complete["consumed"], false);
            let apply = call(json!({
                "op":"async_apply","sid":"async","reading":"かき","candidates":["柿"]
            }));
            assert_eq!(apply["consumed"], false);
            meltype_tip_destroy(connection);
        }
    }
    #[test]
    fn key_up_event_reaches_core_without_consuming() {
        unsafe {
            let connection = meltype_tip_create();
            let request = CString::new(
                json!({"op":"key","sid":"up","vk":28,"ch":0,"is_up":true}).to_string(),
            )
            .unwrap();
            let value: Value = serde_json::from_str(
                &take(meltype_tip_request(connection, request.as_ptr())).unwrap(),
            )
            .unwrap();
            assert_eq!(value["consumed"], false);
            meltype_tip_destroy(connection);
        }
    }
    #[test]
    fn sessions_are_scoped_to_connection() {
        unsafe {
            let a = meltype_tip_create();
            let b = meltype_tip_create();
            let key = CString::new(r#"{"op":"key","sid":"same","ch":107}"#).unwrap();
            let result = meltype_tip_request(a, key.as_ptr());
            assert!(!result.is_null());
            meltype_free(result.cast());
            let commit = CString::new(r#"{"op":"commit","sid":"same"}"#).unwrap();
            let value: Value =
                serde_json::from_str(&take(meltype_tip_request(b, commit.as_ptr())).unwrap())
                    .unwrap();
            assert_eq!(value["active"], false);
            meltype_tip_destroy(a);
            meltype_tip_destroy(b);
        }
    }
}

#[cfg(test)]
mod profile_cache_tests {
    use super::*;
    #[test]
    fn settings_follow_the_field_and_same_length_rewrites() {
        let dir = tempfile::tempdir().unwrap();
        let store = Store::new(dir.path());
        let mut cache = None;
        let first = app_settings(&mut cache, &store, "chrome");
        assert!(std::rc::Rc::ptr_eq(
            &first,
            &app_settings(&mut cache, &store, "chrome")
        ));
        assert!(!std::rc::Rc::ptr_eq(
            &first,
            &app_settings(&mut cache, &store, "notepad")
        ));
        for (value, expected) in [("true ", true), ("false", false)] {
            let data = format!(r#"{{"SettingsVersion":5,"DirectModeAutoDetect":{value}}}"#);
            std::fs::write(store.directory.join("config.json"), data).unwrap();
            assert_eq!(
                app_settings(&mut cache, &store, "notepad").boolean("DirectModeAutoDetect"),
                expected
            );
        }
    }
}
