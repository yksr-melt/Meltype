// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
//! fcitx event orchestration; no locks are held across SDK callbacks.
use super::*;
use crate::fcitx::*;
use crate::fcitx_view::{meltype_fcitx_apply_utf8, Callback};
#[repr(C)]
#[derive(Default)]
pub struct Surrounding {
    before: *const u8,
    before_length: u32,
    after: *const u8,
    after_length: u32,
}
type Query = unsafe extern "C" fn(*mut c_void, u32, *mut Surrounding) -> i32;
unsafe fn reply(pointer: *mut c_char) -> Option<(Vec<u8>, serde_json::Value)> {
    if pointer.is_null() {
        return None;
    }
    let bytes = CStr::from_ptr(pointer).to_bytes().to_vec();
    meltype_free(pointer.cast());
    serde_json::from_slice(&bytes)
        .ok()
        .map(|value| (bytes, value))
}
unsafe fn context_string(pointer: *const u8, length: u32) -> Option<CString> {
    if pointer.is_null() {
        return None;
    }
    let bytes = std::slice::from_raw_parts(pointer, length as usize);
    // The former c_str() input path truncated embedded NUL.
    CString::new(bytes.split(|b| *b == 0).next().unwrap_or_default()).ok()
}
unsafe fn sync(handle: *mut c_void, context: *mut c_void, query: Query) -> bool {
    if meltype_fcitx_state(handle) < 0 {
        return false;
    }
    let capability = query(context, 0, &mut Surrounding::default());
    if capability < 0 || meltype_fcitx_state(handle) < 0 {
        return false;
    }
    meltype_set_can_delete(handle, i32::from(capability > 0));
    true
}
unsafe fn apply(handle: *mut c_void, bytes: &[u8], context: *mut c_void, callback: Callback) {
    meltype_fcitx_apply_utf8(
        handle,
        bytes.as_ptr(),
        bytes.len() as u32,
        context,
        Some(callback),
    );
}
/// Dispatch a hardware key. Return 1 only when the SDK event should be accepted.
/// Query 0 supplies deletion capability; query 1 supplies before/after context.
/// Operation callback uses the apply ABI plus mode label(14), after committing
/// and updating direct mode. Release/stale events do not query the host.
/// # Safety
/// Callbacks/context are live for this synchronous call. Query text buffers are
/// readable for their declared lengths until the next query or return. Callbacks
/// must not throw across the ABI; negative query results stop dispatch.
#[no_mangle]
#[allow(clippy::too_many_arguments)] // Explicit synchronous C ABI, shared with SDK bridge.
pub unsafe extern "C" fn meltype_fcitx_key_event(
    handle: *mut c_void,
    symbol: u32,
    scalar: u32,
    states: u32,
    release: i32,
    context: *mut c_void,
    query: Option<Query>,
    callback: Option<Callback>,
) -> i32 {
    let (Some(query), Some(callback)) = (query, callback) else {
        return 0;
    };
    if release != 0 || !sync(handle, context, query) {
        return 0;
    }
    let state = meltype_fcitx_state(handle);
    let mode = meltype_fcitx_mode(symbol, state & 1);
    let role = ((mode >> 8) & 255) as i32;
    if role != 0 {
        if let Some((bytes, _)) = reply(meltype_role_key(handle, role)) {
            apply(handle, &bytes, context, callback);
            return 1;
        }
    }
    let packed = meltype_fcitx_key(symbol, scalar);
    let vk = packed as u32 as i32;
    let mode_vk = match symbol {
        // US配列で届いたEsc下のキーも半角/全角として扱う。
        0x60 if meltype_fcitx_modifiers(states) == 0 => 0x19,
        0xff21 if states & 8 != 0 => 0xc0,
        0xff21 | 0xff28..=0xff2a => 0x19,
        0xff30 | 0xff22 => 0x1a,
        0xff23 => 0x1c,
        0xff27 => 0x15,
        _ => vk,
    };
    let modifiers = meltype_fcitx_modifiers(states) | i32::from(symbol == 0xfe20);
    if let Some((bytes, _)) = reply(meltype_mode_key(handle, mode_vk, modifiers)) {
        apply(handle, &bytes, context, callback);
        if let Some(direct) = sync_input_mode(handle) {
            let label = if direct { "A" } else { "あ" };
            callback(context, 14, 0, label.as_ptr(), label.len() as u32);
        }
        return 1;
    }
    if mode & 1 != 0 {
        if let Some((bytes, _)) = reply(meltype_commit(handle)) {
            apply(handle, &bytes, context, callback);
        }
        let direct = ((mode >> 1) & 1) as i32;
        meltype_fcitx_set_direct(handle, direct);
        let label = if direct != 0 { "A" } else { "あ" };
        callback(context, 14, 0, label.as_ptr(), label.len() as u32);
        return 1;
    }
    if vk == 0 {
        return 0;
    }
    let mut surrounding = Surrounding::default();
    let availability = if state & 2 == 0 {
        query(context, 1, &mut surrounding)
    } else {
        0
    };
    if availability < 0 {
        return 0;
    }
    let available = availability > 0;
    if meltype_fcitx_state(handle) < 0 {
        return 0;
    }
    let before = available
        .then(|| context_string(surrounding.before, surrounding.before_length))
        .flatten();
    let after = available
        .then(|| context_string(surrounding.after, surrounding.after_length))
        .flatten();
    let pointer = meltype_handle_key(
        handle,
        vk,
        (packed >> 32) as i32,
        modifiers,
        before.as_ref().map_or(std::ptr::null(), |s| s.as_ptr()),
        after.as_ref().map_or(std::ptr::null(), |s| s.as_ptr()),
    );
    let Some((bytes, value)) = reply(pointer) else {
        return 0;
    };
    apply(handle, &bytes, context, callback);
    i32::from(value.get("consumed").and_then(serde_json::Value::as_bool) == Some(true))
}
/// Actions: 0 commit, 1 select the displayed list, 2 remember host-committed
/// preedit without reinsertion, 3 cancel without commit or learning,
/// 4 commit exactly the displayed preedit (server-side preedit).
/// # Safety
/// Same callback/buffer contract as meltype_fcitx_key_event.
#[no_mangle]
pub unsafe extern "C" fn meltype_fcitx_action(
    handle: *mut c_void,
    action: u32,
    index: i32,
    context: *mut c_void,
    query: Option<Query>,
    callback: Option<Callback>,
) -> i32 {
    let (Some(query), Some(callback)) = (query, callback) else {
        return 0;
    };
    if action > 4 || !sync(handle, context, query) {
        return 0;
    }
    let pointer = match action {
        0 => meltype_commit(handle),
        1 if predicting(handle) => meltype_select_prediction(handle, index),
        1 => meltype_select_candidate(handle, index),
        2 => {
            let pointer = meltype_commit_preedit(handle);
            if pointer.is_null() {
                return 0;
            }
            meltype_free(pointer.cast());
            let reset = meltype_reset(handle);
            meltype_free(reset.cast());
            meltype_caret_moved(handle, 0, 0);
            sync_input_mode(handle);
            set_predicting(handle, false);
            meltype_fcitx_set_preedit(handle, 0);
            return i32::from(callback(context, 8, 0, b"".as_ptr(), 0) > 0);
        }
        3 => meltype_reset(handle),
        4 => meltype_commit_preedit(handle),
        _ => unreachable!(),
    };
    let Some((bytes, _)) = reply(pointer) else {
        return 0;
    };
    apply(handle, &bytes, context, callback);
    if action == 4 {
        meltype_caret_moved(handle, 0, 0);
        sync_input_mode(handle);
    }
    1
}
