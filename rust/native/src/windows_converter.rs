// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
//! Windows engine adapter. COM objects remain on their originating STA thread.
mod candidates;
mod worker;
mod zenzai;
pub use worker::install as install_worker;
pub(crate) use worker::{generated_candidates, has_worker, learn, scope};
mod system;
use serde_json::json;
use std::ffi::c_char;

/// Read-only diagnostic using the same adapter as the worker. It does not
/// activate an IME or access a user document. All COM ownership ends on return.
/// # Safety
/// Request is a valid UTF-8 NUL-terminated JSON string. Result uses meltype_free.
#[no_mangle]
pub unsafe extern "C" fn meltype_windows_converter_probe(request: *const c_char) -> *mut c_char {
    std::panic::catch_unwind(|| {
        let request: serde_json::Value = serde_json::from_str(&super::read(request)?).ok()?;
        let _apartment = system::Apartment::enter()?;
        let mut converter = system::System::default();
        let mut extras = candidates::Candidates::default();
        let mut probe = |request: &serde_json::Value| -> Option<serde_json::Value> {
        let text = request["text"].as_str().unwrap_or("").encode_utf16().collect::<Vec<_>>();
        let context = request["context"].as_str().unwrap_or("").encode_utf16().collect::<Vec<_>>();
        if text.len() > 8192 || context.len() > 8192 { return None; }
        let available = converter.available();
        let converted = converter.convert(&text).map(|s| String::from_utf16_lossy(&s));
        let clauses = converter.clauses(&text, &context).map(|clauses| clauses.into_iter().map(|s| {
            json!({"reading":String::from_utf16_lossy(&s.reading),"text":String::from_utf16_lossy(&s.text)})
        }).collect::<Vec<_>>());
        let candidates = extras.get(request["text"].as_str().unwrap_or(""));
        let mut reply=json!({"available":available,"converted":converted,"clauses":clauses,"candidates":candidates});
        if request["trace"]==true { reply["candidateTimedOut"]=json!(extras.timed_out); }
        Some(reply)
        };
        let reply = if let Some(requests) = request["requests"].as_array() {
            // Diagnostic batches follow the worker's persistent STA lifecycle.
            // Bound work and retain input order; no user document is modified.
            if requests.is_empty() || requests.len() > 256 { return None; }
            let replies = requests.iter().map(&mut probe).collect::<Option<Vec<_>>>()?;
            json!({"responses": replies})
        } else {
            probe(&request)?
        };
        Some(super::allocate(&reply.to_string()))
    }).ok().flatten().unwrap_or(std::ptr::null_mut())
}
