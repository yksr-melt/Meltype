// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! Offline, memory-only iOS keyboard sessions. No document deletion or learning.
use meltype_core::{Session, SessionResult};
use std::{
    collections::HashMap,
    ffi::{c_char, CString},
    panic::{catch_unwind, AssertUnwindSafe},
    sync::{
        atomic::{AtomicU64, Ordering},
        Mutex, OnceLock,
    },
};

fn ai() -> &'static Mutex<meltype_ai::shunti::Runtime> {
    static AI: OnceLock<Mutex<meltype_ai::shunti::Runtime>> = OnceLock::new();
    AI.get_or_init(|| Mutex::new(Default::default()))
}
fn sessions() -> &'static Mutex<HashMap<u64, Session>> {
    static SESSIONS: OnceLock<Mutex<HashMap<u64, Session>>> = OnceLock::new();
    SESSIONS.get_or_init(|| Mutex::new(HashMap::new()))
}
fn run(handle: u64, action: impl FnOnce(&mut Session) -> SessionResult) -> *mut c_char {
    catch_unwind(AssertUnwindSafe(|| {
        let mut all = sessions().lock().ok()?;
        let result = action(all.get_mut(&handle)?);
        CString::new(serde_json::to_string(&result).ok()?)
            .ok()
            .map(CString::into_raw)
    }))
    .ok()
    .flatten()
    .unwrap_or(std::ptr::null_mut())
}
#[no_mangle]
pub extern "C" fn meltype_ios_create() -> u64 {
    static NEXT: AtomicU64 = AtomicU64::new(1);
    let mut session = Session::default();
    session.settings.remove_neural_settings();
    session.can_delete = false;
    session.no_learning = true;
    let handle = NEXT.fetch_add(1, Ordering::Relaxed);
    match sessions().lock() {
        Ok(mut all) => {
            all.insert(handle, session);
            handle
        }
        Err(_) => 0,
    }
}
#[no_mangle]
pub extern "C" fn meltype_ios_destroy(handle: u64) {
    if let Ok(mut all) = sessions().lock() {
        all.remove(&handle);
    }
}
#[no_mangle]
pub extern "C" fn meltype_ios_key(
    handle: u64,
    vk: i32,
    scalar: i32,
    modifiers: i32,
) -> *mut c_char {
    let was_converting = sessions()
        .lock()
        .ok()
        .and_then(|all| all.get(&handle).map(Session::is_converting))
        .unwrap_or(false);
    let result = run(handle, |session| session.key(vk, scalar, modifiers));
    if was_converting {
        return result;
    }
    let request = sessions()
        .lock()
        .ok()
        .and_then(|all| all.get(&handle).and_then(Session::ai_request));
    if let Some((reading, context)) = request {
        let candidates = ai()
            .lock()
            .ok()
            .and_then(|mut ai| ai.convert(&reading, &context));
        if let Some(candidates) = candidates {
            let applied = sessions().lock().ok().and_then(|mut all| {
                let session = all.get_mut(&handle)?;
                session
                    .apply_ai_candidates(&reading, candidates)
                    .then(|| session.result(true))
            });
            if let Some(applied) = applied {
                if !result.is_null() {
                    let previous: Option<SessionResult> = unsafe {
                        serde_json::from_slice(std::ffi::CStr::from_ptr(result).to_bytes()).ok()
                    };
                    unsafe {
                        meltype_ios_free(result);
                    }
                    let mut applied = applied;
                    if let Some(previous) = previous {
                        applied.commits = previous.commits;
                    }
                    return CString::new(serde_json::to_string(&applied).unwrap())
                        .unwrap()
                        .into_raw();
                }
            }
        }
    }
    result
}
/// Start selection reconversion without modifying the host document.
/// # Safety
/// `text` must point to `length` readable UTF-8 bytes for the duration of the call.
#[no_mangle]
pub unsafe extern "C" fn meltype_ios_reconvert(
    handle: u64,
    text: *const c_char,
    length: u32,
) -> *mut c_char {
    if text.is_null() || length == 0 || length > 4096 {
        return std::ptr::null_mut();
    }
    let bytes = std::slice::from_raw_parts(text.cast::<u8>(), length as usize);
    let Ok(text) = std::str::from_utf8(bytes) else {
        return std::ptr::null_mut();
    };
    run(handle, |session| {
        let result = session.reconvert_selection(text, None);
        if result.consumed {
            result
        } else {
            session.reconvert_selection(text, Some(text))
        }
    })
}
/// Configure the optional local model from the containing app's shared directory.
/// # Safety
/// directory points to length UTF-8 bytes, or is null when disabling.
#[no_mangle]
pub unsafe extern "C" fn meltype_ios_set_ai(
    handle: u64,
    directory: *const c_char,
    length: u32,
    enabled: i32,
) -> i32 {
    let path = if enabled != 0 {
        if directory.is_null() || length == 0 || length > 32768 {
            return 0;
        }
        let Ok(text) = std::str::from_utf8(std::slice::from_raw_parts(
            directory.cast::<u8>(),
            length as usize,
        )) else {
            return 0;
        };
        let path = std::path::PathBuf::from(text);
        if !path.is_absolute()
            || !path.join("kkc_lex.bin").is_file()
            || !path.join("kkc_model.bin").is_file()
        {
            return 0;
        }
        Some(path)
    } else {
        None
    };
    if let Ok(mut all) = sessions().lock() {
        let Some(session) = all.get_mut(&handle) else {
            return 0;
        };
        meltype_core::ai_model::AiModel::Shunti.select(&mut session.settings, path.is_some());
        if path.is_none() {
            meltype_core::ai_model::AiModel::None.select(&mut session.settings, false);
        }
    } else {
        return 0;
    }
    if let Ok(mut runtime) = ai().lock() {
        runtime.configure(path.as_deref());
        1
    } else {
        0
    }
}

/// Set the currency rate source from the containing app's shared settings.
/// # Safety
/// `mode` and `manual` point to length UTF-8 bytes, or are null when empty.
#[no_mangle]
pub unsafe extern "C" fn meltype_ios_set_currency(
    handle: u64,
    mode: *const c_char,
    mode_length: u32,
    manual: *const c_char,
    manual_length: u32,
    decimals: i32,
) -> i32 {
    let text = |pointer: *const c_char, length: u32| -> Option<String> {
        if pointer.is_null() || length == 0 || length > 4096 {
            return None;
        }
        let bytes = std::slice::from_raw_parts(pointer.cast::<u8>(), length as usize);
        std::str::from_utf8(bytes).ok().map(str::to_owned)
    };
    let mode = text(mode, mode_length).unwrap_or_else(|| "Fixed".to_owned());
    let Some(mode) = meltype_core::currency::Mode::parse(&mode) else {
        return 0;
    };
    let manual = text(manual, manual_length).unwrap_or_default();
    let decimals = decimals.clamp(0, meltype_core::currency::MAX_DECIMALS as i32);
    let Ok(mut all) = sessions().lock() else {
        return 0;
    };
    let Some(session) = all.get_mut(&handle) else {
        return 0;
    };
    let settings = &mut session.settings;
    if settings
        .set("CurrencyRateMode", serde_json::json!(mode.value()))
        .is_err()
        || settings
            .set("CurrencyManualRates", serde_json::json!(manual))
            .is_err()
        || settings
            .set("CurrencyDecimals", serde_json::json!(decimals))
            .is_err()
    {
        return 0;
    }
    1
}

#[no_mangle]
pub extern "C" fn meltype_ios_select(handle: u64, index: u32) -> *mut c_char {
    run(handle, |session| session.select(index as usize))
}
#[no_mangle]
pub extern "C" fn meltype_ios_commit(handle: u64) -> *mut c_char {
    run(handle, Session::commit)
}
#[no_mangle]
pub extern "C" fn meltype_ios_reset(handle: u64) -> *mut c_char {
    run(handle, |session| {
        session.reset();
        session.result(false)
    })
}
/// # Safety
/// Pass a non-null string returned by this library exactly once, or null.
#[no_mangle]
pub unsafe extern "C" fn meltype_ios_free(text: *mut c_char) {
    if !text.is_null() {
        drop(CString::from_raw(text));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::CStr;
    fn read(text: *mut c_char) -> serde_json::Value {
        assert!(!text.is_null());
        let value =
            unsafe { serde_json::from_str(CStr::from_ptr(text).to_str().unwrap()).unwrap() };
        unsafe {
            meltype_ios_free(text);
        }
        value
    }
    #[test]
    fn selection_reconversion_is_staged_bounded_and_cancellable() {
        let handle = meltype_ios_create();
        let text = CString::new("にほんご").unwrap();
        let result = read(unsafe {
            meltype_ios_reconvert(handle, text.as_ptr(), text.as_bytes().len() as u32)
        });
        assert_eq!(result["selectionExpect"], "にほんご");
        assert!(result["commits"].as_array().unwrap().is_empty());
        let candidates = result["view"]["candidates"].as_array().unwrap();
        let index = candidates.iter().position(|s| s == "ニホンゴ").unwrap();
        let result = read(meltype_ios_select(handle, index as u32));
        assert_eq!(result["commits"][0]["text"], "ニホンゴ");
        assert_eq!(result["commits"][0]["deleteBefore"], 0);
        read(unsafe { meltype_ios_reconvert(handle, text.as_ptr(), text.as_bytes().len() as u32) });
        read(meltype_ios_reset(handle));
        assert!(read(meltype_ios_commit(handle))["commits"]
            .as_array()
            .unwrap()
            .is_empty());
        assert!(unsafe { meltype_ios_reconvert(handle, std::ptr::null(), 1) }.is_null());
        assert!(unsafe { meltype_ios_reconvert(handle, text.as_ptr(), 4097) }.is_null());
        let invalid = [0xffu8];
        assert!(unsafe { meltype_ios_reconvert(handle, invalid.as_ptr().cast(), 1) }.is_null());
        meltype_ios_destroy(handle);
    }
    #[test]
    fn flick_kana_composes_converts_and_replaces_without_host_deletion() {
        let handle = meltype_ios_create();
        let mut last = serde_json::Value::Null;
        for ch in "にほんご".chars() {
            last = read(meltype_ios_key(handle, ch as i32, ch as i32, 0));
            assert!(last["commits"].as_array().unwrap().is_empty());
        }
        assert_eq!(last["view"]["text"], "にほんご");
        let conversion = read(meltype_ios_key(handle, 32, 32, 0));
        assert!(
            conversion["view"]["candidates"]
                .as_array()
                .unwrap()
                .iter()
                .any(|s| s == "ニホンゴ"),
            "{conversion}"
        );
        read(meltype_ios_reset(handle));
        for ch in "は".chars() {
            read(meltype_ios_key(handle, ch as i32, ch as i32, 0));
        }
        read(meltype_ios_key(handle, 8, 0, 0));
        for ch in "ぱっゅ".chars() {
            read(meltype_ios_key(handle, ch as i32, ch as i32, 0));
        }
        let result = read(meltype_ios_commit(handle));
        assert_eq!(result["commits"][0]["text"], "ぱっゅ");
        assert_eq!(result["commits"][0]["deleteBefore"], 0);
        meltype_ios_destroy(handle);
    }
    #[test]
    fn input_commit_reset_and_stale_handles() {
        let handle = meltype_ios_create();
        let mut last = serde_json::Value::Null;
        for ch in "nihongo".chars() {
            last = read(meltype_ios_key(
                handle,
                ch.to_ascii_uppercase() as i32,
                ch as i32,
                0,
            ));
        }
        assert_eq!(last["view"]["text"], "にほんご");
        let committed = read(meltype_ios_commit(handle));
        assert_eq!(committed["commits"][0]["text"], "にほんご");
        assert_eq!(committed["commits"][0]["deleteBefore"], 0);
        read(meltype_ios_key(handle, 75, 107, 0));
        assert!(read(meltype_ios_reset(handle))["view"].is_null());
        meltype_ios_destroy(handle);
        assert!(meltype_ios_key(handle, 65, 97, 0).is_null());
        meltype_ios_destroy(handle);
    }
    #[test]
    fn sessions_are_isolated_and_long_input_does_not_request_host_deletion() {
        let a = meltype_ios_create();
        let b = meltype_ios_create();
        for ch in "nihongo".repeat(80).chars() {
            let result = read(meltype_ios_key(
                a,
                ch.to_ascii_uppercase() as i32,
                ch as i32,
                0,
            ));
            for edit in result["commits"].as_array().unwrap() {
                assert_eq!(edit["deleteBefore"], 0);
            }
        }
        assert!(read(meltype_ios_commit(b))["commits"]
            .as_array()
            .unwrap()
            .is_empty());
        meltype_ios_destroy(a);
        meltype_ios_destroy(b);
    }
}
