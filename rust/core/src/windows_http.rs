// SPDX-License-Identifier: GPL-3.0-or-later
//! Bounded HTTPS JSON reads from api.github.com. Redirects are disabled.
use std::ffi::c_void;
#[link(name = "winhttp")]
unsafe extern "system" {
    fn WinHttpOpen(
        agent: *const u16,
        access: u32,
        proxy: *const u16,
        bypass: *const u16,
        flags: u32,
    ) -> *mut c_void;
    fn WinHttpConnect(
        session: *mut c_void,
        host: *const u16,
        port: u16,
        reserved: u32,
    ) -> *mut c_void;
    fn WinHttpOpenRequest(
        connect: *mut c_void,
        verb: *const u16,
        path: *const u16,
        version: *const u16,
        referer: *const u16,
        accept: *const *const u16,
        flags: u32,
    ) -> *mut c_void;
    fn WinHttpSetTimeouts(
        session: *mut c_void,
        resolve: i32,
        connect: i32,
        send: i32,
        receive: i32,
    ) -> i32;
    fn WinHttpSetOption(handle: *mut c_void, option: u32, value: *const c_void, size: u32) -> i32;
    fn WinHttpSendRequest(
        request: *mut c_void,
        headers: *const u16,
        length: u32,
        data: *const c_void,
        data_length: u32,
        total: u32,
        context: usize,
    ) -> i32;
    fn WinHttpReceiveResponse(request: *mut c_void, reserved: *mut c_void) -> i32;
    fn WinHttpQueryHeaders(
        request: *mut c_void,
        level: u32,
        name: *const u16,
        buffer: *mut c_void,
        size: *mut u32,
        index: *mut u32,
    ) -> i32;
    fn WinHttpReadData(request: *mut c_void, buffer: *mut c_void, size: u32, read: *mut u32)
        -> i32;
    fn WinHttpCloseHandle(handle: *mut c_void) -> i32;
}
struct Handle(*mut c_void);
impl Handle {
    fn new(pointer: *mut c_void) -> Option<Self> {
        (!pointer.is_null()).then_some(Self(pointer))
    }
}
impl Drop for Handle {
    fn drop(&mut self) {
        unsafe {
            WinHttpCloseHandle(self.0);
        }
    }
}
fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(Some(0)).collect()
}
/// No user text is included by this transport. Call outside input/UI locks.
/// Bounded HTTPS GET for a fixed host and path (1 MB, 5 s, no redirects).
pub fn get_text(host: &str, path: &str, accept: &str) -> Option<String> {
    if host.is_empty()
        || !host.bytes().all(|b| b.is_ascii_graphic())
        || host.contains('/')
        || !path.starts_with('/')
        || !path.bytes().all(|b| b.is_ascii_graphic())
        || !accept
            .bytes()
            .all(|b| b.is_ascii_graphic() || b == b'/' || b == b' ')
    {
        return None;
    }
    let headers = format!("Accept: {accept}\r\n");
    String::from_utf8(request(host, path, &headers)?).ok()
}

/// Bounded HTTPS JSON read from api.github.com. Redirects are disabled.
pub fn get_api_json(path: &str, token: Option<&str>) -> Option<serde_json::Value> {
    if !path.starts_with("/repos/") || !path.bytes().all(|b| b.is_ascii_graphic()) {
        return None;
    }
    let mut headers = "Accept: application/vnd.github+json\r\n".to_owned();
    if let Some(token) = token {
        // Never forward arbitrary header syntax or token to another host.
        if !token.is_empty() && token.bytes().all(|c| c.is_ascii_graphic()) {
            headers.push_str(&format!("Authorization: Bearer {token}\r\n"));
        }
    }
    serde_json::from_slice(&request("api.github.com", path, &headers)?).ok()
}

fn request(host: &str, path: &str, headers: &str) -> Option<Vec<u8>> {
    unsafe {
        let session = Handle::new(WinHttpOpen(
            wide("Meltype/1").as_ptr(),
            4,
            std::ptr::null(),
            std::ptr::null(),
            0,
        ))?;
        if WinHttpSetTimeouts(session.0, 5000, 5000, 5000, 5000) == 0 {
            return None;
        }
        let connect = Handle::new(WinHttpConnect(session.0, wide(host).as_ptr(), 443, 0))?;
        let request = Handle::new(WinHttpOpenRequest(
            connect.0,
            wide("GET").as_ptr(),
            wide(path).as_ptr(),
            std::ptr::null(),
            std::ptr::null(),
            std::ptr::null(),
            0x800000,
        ))?;
        let disable_redirects = 2u32;
        if WinHttpSetOption(request.0, 63, (&disable_redirects as *const u32).cast(), 4) == 0 {
            return None;
        }
        let headers = wide(headers);
        if WinHttpSendRequest(
            request.0,
            headers.as_ptr(),
            (headers.len() - 1) as u32,
            std::ptr::null(),
            0,
            0,
            0,
        ) == 0
            || WinHttpReceiveResponse(request.0, std::ptr::null_mut()) == 0
        {
            return None;
        }
        let mut status = 0u32;
        let mut size = 4;
        if WinHttpQueryHeaders(
            request.0,
            19 | 0x20000000,
            std::ptr::null(),
            (&mut status as *mut u32).cast(),
            &mut size,
            std::ptr::null_mut(),
        ) == 0
            || !(200..300).contains(&status)
        {
            return None;
        }
        let mut body = Vec::new();
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
        loop {
            if std::time::Instant::now() > deadline {
                return None;
            }
            let mut buffer = [0u8; 8192];
            let mut read = 0u32;
            if WinHttpReadData(
                request.0,
                buffer.as_mut_ptr().cast(),
                buffer.len() as u32,
                &mut read,
            ) == 0
            {
                return None;
            }
            if read == 0 {
                break;
            }
            if body.len() + read as usize > 1024 * 1024 {
                return None;
            }
            body.extend_from_slice(&buffer[..read as usize]);
        }
        Some(body)
    }
}
