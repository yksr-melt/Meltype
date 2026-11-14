// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! Optional desktop zenz/llama-server conversion. Startup never blocks input;
//! only an already healthy loopback server may receive bounded requests.
use meltype_core::{mozc::Segment, settings::Settings};
use serde_json::{json, Value};
#[cfg(windows)]
use std::os::windows::{io::AsRawHandle, process::CommandExt};
use std::{
    collections::{HashMap, HashSet},
    io::{self, Read, Write},
    net::{Ipv4Addr, SocketAddr, SocketAddrV4, TcpListener, TcpStream},
    path::PathBuf,
    process::{Child, Command, Stdio},
    sync::{
        atomic::{AtomicBool, AtomicU8, Ordering},
        Arc, Mutex,
    },
    time::{Duration, Instant},
};
#[cfg(windows)]
use windows::{
    core::PCWSTR,
    Win32::{
        Foundation::{CloseHandle, HANDLE},
        System::JobObjects::*,
    },
};

const TIMEOUT: Duration = Duration::from_millis(400);
const BACKOFF: Duration = Duration::from_secs(5);
const MAX_RESPONSE: usize = 64 * 1024;
const MAX_CACHE: usize = 256;
type Cache = HashMap<(String, String), Option<String>>;

#[cfg(windows)]
struct Process {
    child: Child,
    job: HANDLE,
}
#[cfg(windows)]
unsafe impl Send for Process {}
#[cfg(windows)]
impl Process {
    fn start(command: &mut Command) -> io::Result<Self> {
        let mut child = command.creation_flags(0x08000000).spawn()?;
        let result = unsafe {
            (|| {
                let job = CreateJobObjectW(None, PCWSTR::null()).map_err(io::Error::other)?;
                let info = JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
                    BasicLimitInformation: JOBOBJECT_BASIC_LIMIT_INFORMATION {
                        LimitFlags: JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                        ..Default::default()
                    },
                    ..Default::default()
                };
                let assigned = SetInformationJobObject(
                    job,
                    JobObjectExtendedLimitInformation,
                    (&info as *const JOBOBJECT_EXTENDED_LIMIT_INFORMATION).cast(),
                    std::mem::size_of_val(&info) as u32,
                )
                .and_then(|_| AssignProcessToJobObject(job, HANDLE(child.as_raw_handle())));
                if let Err(error) = assigned {
                    let _ = CloseHandle(job);
                    return Err(io::Error::other(error));
                }
                Ok(job)
            })()
        };
        match result {
            Ok(job) => Ok(Self { child, job }),
            Err(error) => {
                let _ = child.kill();
                let _ = child.wait();
                Err(error)
            }
        }
    }
}
#[cfg(windows)]
impl Drop for Process {
    fn drop(&mut self) {
        unsafe {
            let _ = TerminateJobObject(self.job, 1);
            let _ = CloseHandle(self.job);
        }
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

#[cfg(any(target_os = "linux", target_os = "macos"))]
struct Process {
    child: Child,
}
#[cfg(any(target_os = "linux", target_os = "macos"))]
impl Process {
    fn start(command: &mut Command) -> io::Result<Self> {
        use std::os::unix::process::CommandExt;
        command.process_group(0);
        #[cfg(target_os = "linux")]
        let parent = unsafe { libc::getpid() };
        // The model server must also end if IBus is killed without running Drop.
        #[cfg(target_os = "linux")]
        unsafe {
            command.pre_exec(move || {
                if libc::prctl(libc::PR_SET_PDEATHSIG, libc::SIGKILL) != 0 {
                    return Err(io::Error::last_os_error());
                }
                if libc::getppid() != parent {
                    return Err(io::Error::other("AI owner exited"));
                }
                Ok(())
            });
        }
        Ok(Self {
            child: command.spawn()?,
        })
    }
}
#[cfg(any(target_os = "linux", target_os = "macos"))]
impl Drop for Process {
    fn drop(&mut self) {
        #[cfg(target_os = "macos")]
        unsafe {
            libc::kill(-(self.child.id() as i32), libc::SIGKILL);
        }
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

struct Server {
    endpoint: SocketAddrV4,
    state: Arc<AtomicU8>, // 0 starting, 1 healthy, 2 failed
    cancelled: Arc<AtomicBool>,
    process: Arc<Mutex<Option<Process>>>,
}
impl Server {
    fn start(
        server: PathBuf,
        model: PathBuf,
        device: &str,
        zenz_tokenizer: bool,
    ) -> io::Result<Self> {
        let port = TcpListener::bind((Ipv4Addr::LOCALHOST, 0))?
            .local_addr()?
            .port();
        let result = Self {
            endpoint: SocketAddrV4::new(Ipv4Addr::LOCALHOST, port),
            state: Arc::new(AtomicU8::new(0)),
            cancelled: Arc::new(AtomicBool::new(false)),
            process: Arc::new(Mutex::new(None)),
        };
        let (endpoint, state, cancelled, process) = (
            result.endpoint,
            result.state.clone(),
            result.cancelled.clone(),
            result.process.clone(),
        );
        let device = device.to_owned();
        std::thread::Builder::new()
            .name("meltype-zenzai-start".into())
            .spawn(move || {
                let outcome = (|| -> io::Result<()> {
                    if cancelled.load(Ordering::Acquire) {
                        return Ok(());
                    }
                    let mut command = Command::new(server);
                    command
                        .arg("-m")
                        .arg(model)
                        .args([
                            "--host",
                            "127.0.0.1",
                            "--port",
                            &port.to_string(),
                            "-c",
                            "1024",
                            "-ngl",
                            "0",
                            "--parallel",
                            "1",
                        ])
                        .stdin(Stdio::null())
                        .stdout(Stdio::null())
                        .stderr(Stdio::null());
                    if !device.trim().is_empty() {
                        command.args(["--device", device.trim()]);
                    }
                    if zenz_tokenizer {
                        command.args(["--override-kv", "tokenizer.ggml.pre=str:gpt-2"]);
                    }
                    let child = Process::start(&mut command)?;
                    {
                        let mut slot = process.lock().unwrap_or_else(|e| e.into_inner());
                        if cancelled.load(Ordering::Acquire) {
                            return Ok(());
                        }
                        *slot = Some(child);
                    }
                    let deadline = Instant::now() + Duration::from_secs(30);
                    while !cancelled.load(Ordering::Acquire) && Instant::now() < deadline {
                        let alive = process
                            .lock()
                            .unwrap_or_else(|e| e.into_inner())
                            .as_mut()
                            .is_some_and(|p| p.child.try_wait().ok() == Some(None));
                        if !alive {
                            return Err(io::Error::other("Zenzai server exited"));
                        }
                        if http(endpoint, "GET", "/health", "", TIMEOUT).is_ok() {
                            state.store(1, Ordering::Release);
                            return Ok(());
                        }
                        std::thread::sleep(Duration::from_millis(200));
                    }
                    Err(io::Error::new(
                        io::ErrorKind::TimedOut,
                        "Zenzai startup timed out",
                    ))
                })();
                if outcome.is_err() {
                    process.lock().unwrap_or_else(|e| e.into_inner()).take();
                    state.store(2, Ordering::Release);
                }
                // Linux parent-death ownership follows the spawning thread.
                // Keep it alive for the complete server lifetime, and detect exits.
                #[cfg(any(target_os = "linux", target_os = "macos"))]
                while outcome.is_ok() && !cancelled.load(Ordering::Acquire) {
                    let alive = process
                        .lock()
                        .unwrap_or_else(|e| e.into_inner())
                        .as_mut()
                        .is_some_and(|p| p.child.try_wait().ok() == Some(None));
                    if !alive {
                        state.store(2, Ordering::Release);
                        break;
                    }
                    std::thread::sleep(Duration::from_millis(200));
                }
            })?;
        Ok(result)
    }
}
impl Drop for Server {
    fn drop(&mut self) {
        self.cancelled.store(true, Ordering::Release);
        self.process
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .take();
    }
}

#[derive(Default)]
pub(crate) struct Zenzai {
    paths: Option<(PathBuf, PathBuf)>,
    variant: String,
    protocol: String,
    device: String,
    zenz_tokenizer: bool,
    server: Option<Server>,
    cache: Cache,
    paused_until: Option<Instant>,
    retry_at: Option<Instant>,
    failures: u32,
    errors: u32,
    request_deadline: Option<Instant>,
}
impl Zenzai {
    /// All Japanese gaps and registered-phrase pieces in one input request
    /// share a deadline. Several short readings must not each wait 400ms.
    pub(crate) fn begin_request(&mut self) {
        self.request_deadline = Some(Instant::now() + TIMEOUT);
    }
    pub(crate) fn configure(&mut self, settings: &Settings) {
        let paths = (settings.boolean("Enabled")
            && settings.boolean("UseZenzai")
            && meltype_core::ai_model::AiModel::selected(settings).is_gguf())
        .then(|| {
            (
                settings.text("ZenzaiServerPath").trim(),
                settings.text("ZenzaiModelPath").trim(),
            )
        })
        .filter(|(server, model)| !server.is_empty() && !model.is_empty())
        .map(|(server, model)| (PathBuf::from(server), PathBuf::from(model)));
        let spec = meltype_core::ai_catalog::selected(
            settings,
            meltype_core::ai_model::AiModel::selected(settings),
        );
        let variant = spec.map(|m| m.id.clone()).unwrap_or_default();
        let protocol = spec.map(|m| m.protocol.clone()).unwrap_or_default();
        let device = settings.text("ZenzaiDevice").trim().to_owned();
        if self.paths != paths || self.variant != variant || self.device != device {
            *self = Self {
                paths,
                variant,
                protocol,
                device,
                zenz_tokenizer: spec.is_some_and(|m| m.family == "Zenz"),
                ..Self::default()
            };
        }
        self.warm_up();
    }
    fn warm_up(&mut self) {
        if self
            .server
            .as_ref()
            .is_some_and(|server| server.state.load(Ordering::Acquire) == 2)
        {
            self.fail();
        }
        if self.server.is_some() || self.retry_at.is_some_and(|at| Instant::now() < at) {
            return;
        }
        let Some((server, model)) = &self.paths else {
            return;
        };
        let result = if server.is_file() && model.is_file() {
            Server::start(
                server.clone(),
                model.clone(),
                &self.device,
                self.zenz_tokenizer,
            )
        } else {
            Err(io::Error::new(
                io::ErrorKind::NotFound,
                "Zenzai files unavailable",
            ))
        };
        match result {
            Ok(server) => self.server = Some(server),
            Err(_) => self.fail(),
        }
    }
    fn fail(&mut self) {
        self.server = None;
        self.cache.clear();
        self.failures = self.failures.saturating_add(1);
        self.retry_at = Some(
            Instant::now()
                + Duration::from_secs((300u64 << self.failures.min(5).saturating_sub(1)).min(3600)),
        );
    }
    pub(crate) fn convert(&mut self, reading: &str, before: &str) -> Option<String> {
        if reading.is_empty()
            || reading.encode_utf16().count() > 80
            || reading
                .chars()
                .any(|c| c.is_control() || ('\u{ee00}'..='\u{ee08}').contains(&c))
            || !reading.chars().any(meltype_core::linguistics::kana)
            || self.paused_until.is_some_and(|at| Instant::now() < at)
        {
            return None;
        }
        let context = context(before);
        // Context-free short readings have many plausible answers; retain the
        // established engine's candidate ranking rather than guessing a name.
        if context == "。" && reading.encode_utf16().count() <= 2 {
            return None;
        }
        let key = (context, reading.to_owned());
        if let Some(cached) = self.cache.get(&key) {
            return cached.clone();
        }
        let timeout = match self.request_deadline {
            Some(deadline) => deadline
                .checked_duration_since(Instant::now())?
                .min(TIMEOUT),
            None => TIMEOUT,
        };
        if timeout.is_zero() {
            return None;
        }
        self.warm_up();
        let server = self
            .server
            .as_ref()
            .filter(|server| server.state.load(Ordering::Acquire) == 1)?;
        let body = json!({"prompt":meltype_core::ai_catalog::prompt(&self.protocol, reading, &key.0), "n_predict":96, "temperature":0,
            "top_k":1, "cache_prompt":true, "parse_special":true})
        .to_string();
        let started = Instant::now();
        let response = http(server.endpoint, "POST", "/completion", &body, timeout)
            .and_then(|body| serde_json::from_slice::<Value>(&body).map_err(io::Error::other));
        if started.elapsed() >= timeout
            || response.as_ref().err().is_some_and(|e| {
                matches!(
                    e.kind(),
                    io::ErrorKind::TimedOut | io::ErrorKind::WouldBlock
                )
            })
        {
            self.paused_until = Some(Instant::now() + BACKOFF);
            return None;
        }
        let answer = response
            .ok()
            .and_then(|value| clean(value["content"].as_str()?));
        let Some(answer) = answer else {
            self.errors += 1;
            if self.errors >= 3 {
                self.errors = 0;
                self.fail();
            }
            return None;
        };
        self.failures = 0;
        self.errors = 0;
        let answer = (answer != reading).then_some(answer);
        if self.cache.len() >= MAX_CACHE {
            self.cache.clear();
        }
        self.cache.insert(key, answer.clone());
        answer
    }
}

fn context(before: &str) -> String {
    let text: String = before
        .chars()
        .filter(|c| !c.is_control() && !('\u{ee00}'..='\u{ee08}').contains(c))
        .collect();
    let text = text.trim();
    if text.is_empty() {
        return "。".into();
    }
    let mut units = 0;
    text.chars()
        .rev()
        .take_while(|c| {
            units += c.len_utf16();
            units <= 100
        })
        .collect::<String>()
        .chars()
        .rev()
        .collect()
}
#[cfg(test)]
fn prompt(reading: &str, context: &str) -> String {
    format!(
        "\u{ee02}{context}\u{ee00}{}\u{ee01}",
        meltype_core::romaji::katakana(reading)
    )
}
fn clean(content: &str) -> Option<String> {
    let text = content
        .split(|c| ('\u{ee00}'..='\u{ee08}').contains(&c) || c == '\n')
        .next()?
        .replace("</s>", "")
        .replace("<s>", "");
    let text = text.trim();
    (!text.is_empty() && text.encode_utf16().count() <= 192 && !text.chars().any(char::is_control))
        .then(|| text.into())
}

// A numeric loopback endpoint avoids DNS, proxies and redirects. Each I/O call
// receives the remaining total budget, including response-body transfer.
fn http(
    endpoint: SocketAddrV4,
    method: &str,
    path: &str,
    body: &str,
    timeout: Duration,
) -> io::Result<Vec<u8>> {
    if !endpoint.ip().is_loopback() {
        return Err(io::Error::other("Zenzai endpoint must be loopback"));
    }
    let deadline = Instant::now() + timeout;
    let remaining = || {
        deadline
            .checked_duration_since(Instant::now())
            .filter(|d| !d.is_zero())
            .ok_or_else(|| io::Error::new(io::ErrorKind::TimedOut, "Zenzai request timed out"))
    };
    let mut stream = TcpStream::connect_timeout(&SocketAddr::V4(endpoint), remaining()?)?;
    let request = format!("{method} {path} HTTP/1.1\r\nHost: {endpoint}\r\nConnection: close\r\nContent-Type: application/json\r\nContent-Length: {}\r\n\r\n{body}", body.len());
    let mut sent = 0;
    while sent < request.len() {
        stream.set_write_timeout(Some(remaining()?))?;
        let count = stream.write(&request.as_bytes()[sent..])?;
        if count == 0 {
            return Err(io::Error::new(
                io::ErrorKind::WriteZero,
                "Zenzai connection closed",
            ));
        }
        sent += count;
    }
    let mut response = Vec::new();
    let mut block = [0; 4096];
    loop {
        stream.set_read_timeout(Some(remaining()?))?;
        let count = stream.read(&mut block)?;
        if count == 0 {
            break;
        }
        response.extend_from_slice(&block[..count]);
        if response.len() > MAX_RESPONSE {
            return Err(io::Error::other("Zenzai response too large"));
        }
        // Honor Content-Length even if a server ignores Connection: close.
        if let Some(end) = response.windows(4).position(|p| p == b"\r\n\r\n") {
            let headers = std::str::from_utf8(&response[..end]).map_err(io::Error::other)?;
            if headers
                .lines()
                .next()
                .and_then(|line| line.split_whitespace().nth(1))
                != Some("200")
            {
                return Err(io::Error::other("Zenzai HTTP error"));
            }
            if header(headers, "transfer-encoding")
                .is_some_and(|s| s.eq_ignore_ascii_case("chunked"))
            {
                match chunks(&response[end + 4..]) {
                    Ok(body) => return Ok(body),
                    Err(error) if error.kind() == io::ErrorKind::UnexpectedEof => {}
                    Err(error) => return Err(error),
                }
            } else if let Some(length) = header(headers, "content-length") {
                let length: usize = length.parse().map_err(io::Error::other)?;
                let total = (end + 4)
                    .checked_add(length)
                    .filter(|length| *length <= MAX_RESPONSE)
                    .ok_or_else(|| io::Error::other("Zenzai response too large"))?;
                if response.len() >= total {
                    break;
                }
            }
        }
    }
    let end = response
        .windows(4)
        .position(|p| p == b"\r\n\r\n")
        .ok_or_else(|| io::Error::other("invalid Zenzai HTTP response"))?;
    let headers = std::str::from_utf8(&response[..end]).map_err(io::Error::other)?;
    if headers
        .lines()
        .next()
        .and_then(|line| line.split_whitespace().nth(1))
        != Some("200")
    {
        return Err(io::Error::other("Zenzai HTTP error"));
    }
    let body = &response[end + 4..];
    if header(headers, "transfer-encoding").is_some_and(|s| s.eq_ignore_ascii_case("chunked")) {
        return chunks(body);
    }
    if let Some(length) = header(headers, "content-length") {
        let length: usize = length.parse().map_err(io::Error::other)?;
        return body
            .get(..length)
            .map(Vec::from)
            .ok_or_else(|| io::Error::other("truncated Zenzai response"));
    }
    Ok(body.to_vec())
}
fn header<'a>(headers: &'a str, key: &str) -> Option<&'a str> {
    headers
        .lines()
        .filter_map(|line| line.split_once(':'))
        .find(|(name, _)| name.eq_ignore_ascii_case(key))
        .map(|(_, value)| value.trim())
}
fn chunks(mut bytes: &[u8]) -> io::Result<Vec<u8>> {
    let mut result = Vec::new();
    loop {
        let end = bytes
            .windows(2)
            .position(|p| p == b"\r\n")
            .ok_or_else(|| io::Error::new(io::ErrorKind::UnexpectedEof, "incomplete HTTP chunk"))?;
        let size = std::str::from_utf8(&bytes[..end])
            .map_err(io::Error::other)?
            .split(';')
            .next()
            .unwrap_or("");
        let size = usize::from_str_radix(size.trim(), 16).map_err(io::Error::other)?;
        bytes = &bytes[end + 2..];
        if size == 0 {
            return if bytes.starts_with(b"\r\n") || bytes.windows(4).any(|p| p == b"\r\n\r\n") {
                Ok(result)
            } else {
                Err(io::Error::new(
                    io::ErrorKind::UnexpectedEof,
                    "incomplete HTTP trailers",
                ))
            };
        }
        let chunk = bytes
            .get(..size)
            .ok_or_else(|| io::Error::new(io::ErrorKind::UnexpectedEof, "truncated HTTP chunk"))?;
        result.extend_from_slice(chunk);
        bytes = bytes
            .get(size..)
            .and_then(|rest| rest.strip_prefix(b"\r\n"))
            .ok_or_else(|| {
                io::Error::new(io::ErrorKind::UnexpectedEof, "invalid HTTP chunk ending")
            })?;
    }
}

/// Match the neural text with one existing candidate per Mozc clause. Failed
/// states are memoized, preserving clause controls without repeated engine IPC.
pub(crate) fn align(segments: &[Segment], text: &str) -> Option<Vec<(String, String)>> {
    fn normalize(text: &str, reading: &str) -> String {
        let digits = reading.chars().any(|c| c.is_ascii_digit());
        let letters = reading.chars().any(|c| c.is_ascii_alphabetic());
        text.chars()
            .map(|c| {
                if digits && ('０'..='９').contains(&c)
                    || letters && (('Ａ'..='Ｚ').contains(&c) || ('ａ'..='ｚ').contains(&c))
                {
                    char::from_u32(c as u32 - 0xfee0).unwrap()
                } else {
                    c
                }
            })
            .collect()
    }
    fn fill(
        index: usize,
        position: usize,
        segments: &[Segment],
        text: &str,
        dead: &mut HashSet<(usize, usize)>,
    ) -> Option<Vec<(String, String)>> {
        if index == segments.len() {
            return (position == text.len()).then(Vec::new);
        }
        if !dead.insert((index, position)) {
            return None;
        }
        let segment = &segments[index];
        let particle = [
            "は", "が", "を", "に", "で", "と", "も", "へ", "の", "や", "か", "から", "まで",
            "より",
        ]
        .contains(&segment.reading.as_str());
        for candidate in if particle {
            std::slice::from_ref(&segment.reading)
        } else {
            &segment.candidates
        } {
            let candidate = normalize(candidate, &segment.reading);
            let Some(rest) = text.get(position..) else {
                continue;
            };
            let units = candidate.chars().count();
            let length = rest
                .char_indices()
                .nth(units)
                .map_or(rest.len(), |(offset, _)| offset);
            if candidate.is_empty() || normalize(&rest[..length], &segment.reading) != candidate {
                continue;
            }
            if let Some(mut tail) = fill(index + 1, position + length, segments, text, dead) {
                tail.insert(0, (segment.reading.clone(), candidate));
                return Some(tail);
            }
        }
        None
    }
    if segments.len() > 128 || text.len() > 4096 {
        return None;
    }
    fill(0, 0, segments, text, &mut HashSet::new())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{sync::mpsc, thread};

    struct Reply {
        bytes: String,
        delay: Duration,
        keep_open: Duration,
    }
    impl Reply {
        fn content(content: &str) -> Self {
            let body = json!({"content":content}).to_string();
            Self {
                bytes: format!(
                    "HTTP/1.1 200 OK\r\nContent-Length: {}\r\n\r\n{body}",
                    body.len()
                ),
                delay: Duration::ZERO,
                keep_open: Duration::ZERO,
            }
        }
    }
    fn fake(replies: Vec<Reply>) -> (Zenzai, mpsc::Receiver<String>, thread::JoinHandle<()>) {
        let listener = TcpListener::bind((Ipv4Addr::LOCALHOST, 0)).unwrap();
        let SocketAddr::V4(endpoint) = listener.local_addr().unwrap() else {
            unreachable!()
        };
        listener.set_nonblocking(true).unwrap();
        let (sender, requests) = mpsc::channel();
        let task = thread::spawn(move || {
            for reply in replies {
                let deadline = Instant::now() + Duration::from_secs(5);
                let mut stream = loop {
                    match listener.accept() {
                        Ok((stream, _)) => break stream,
                        Err(error)
                            if error.kind() == io::ErrorKind::WouldBlock
                                && Instant::now() < deadline =>
                        {
                            thread::sleep(Duration::from_millis(1))
                        }
                        Err(error) => panic!("fake server did not receive request: {error}"),
                    }
                };
                // 接続待ちだけを非同期にし、受信は既存の期限付き同期readで行う。
                stream.set_nonblocking(false).unwrap();
                stream
                    .set_read_timeout(Some(Duration::from_secs(2)))
                    .unwrap();
                let mut request = Vec::new();
                loop {
                    let mut block = [0; 1024];
                    let count = stream.read(&mut block).unwrap();
                    assert_ne!(count, 0);
                    request.extend_from_slice(&block[..count]);
                    if let Some(end) = request.windows(4).position(|p| p == b"\r\n\r\n") {
                        let headers = std::str::from_utf8(&request[..end]).unwrap();
                        let length: usize =
                            header(headers, "content-length").unwrap().parse().unwrap();
                        if request.len() >= end + 4 + length {
                            break;
                        }
                    }
                }
                sender.send(String::from_utf8(request).unwrap()).unwrap();
                thread::sleep(reply.delay);
                let _ = stream.write_all(reply.bytes.as_bytes());
                thread::sleep(reply.keep_open);
            }
        });
        let zenzai = Zenzai {
            server: Some(Server {
                endpoint,
                state: Arc::new(AtomicU8::new(1)),
                cancelled: Arc::new(AtomicBool::new(false)),
                process: Arc::new(Mutex::new(None)),
            }),
            ..Default::default()
        };
        (zenzai, requests, task)
    }

    #[test]
    fn prompt_context_and_cleaning_match_the_model_protocol() {
        assert_eq!(
            prompt("きょう", "昨日の続き"),
            "\u{ee02}昨日の続き\u{ee00}キョウ\u{ee01}"
        );
        assert_eq!(context("\n\u{ee02}"), "。");
        assert_eq!(context("English context"), "English context");
        let long = format!("{}{}", "古い".repeat(80), "😀".repeat(50));
        assert_eq!(context(&long), "😀".repeat(50));
        assert_eq!(clean(" 今日</s>\u{ee01}捨てる "), Some("今日".into()));
        assert_eq!(clean("<s>  </s>"), None);
        assert_eq!(clean("今日\r後続"), None);
        for marker in '\u{ee00}'..='\u{ee08}' {
            assert_eq!(clean(&format!("今日{marker}前の文章")), Some("今日".into()));
        }
        // #1505: a legitimate answer may repeat the preceding context's suffix.
        assert_eq!(clean("確認"), Some("確認".into()));
    }

    #[test]
    fn completion_uses_bounded_context_and_caches_success_and_unchanged_answers() {
        let (mut zenzai, requests, task) = fake(vec![
            Reply::content("今日です</s>"),
            Reply::content("かんたんなもんだい"),
        ]);
        assert_eq!(
            zenzai.convert("きょうです", "昨日の文章"),
            Some("今日です".into())
        );
        let request = requests.recv().unwrap();
        assert!(request.starts_with("POST /completion HTTP/1.1\r\n"));
        let body: Value = serde_json::from_str(request.split_once("\r\n\r\n").unwrap().1).unwrap();
        assert_eq!(
            body["prompt"],
            "\u{ee02}昨日の文章\u{ee00}キョウデス\u{ee01}"
        );
        assert_eq!(body["temperature"], 0);
        assert_eq!(body["top_k"], 1);
        assert_eq!(
            zenzai.convert("きょうです", "昨日の文章"),
            Some("今日です".into())
        );
        assert_eq!(zenzai.convert("かんたんなもんだい", ""), None);
        requests.recv().unwrap();
        for _ in 0..4 {
            assert_eq!(zenzai.convert("かんたんなもんだい", ""), None);
        }
        assert_eq!(zenzai.errors, 0);
        assert_eq!(zenzai.cache.len(), 2);
        task.join().unwrap();
        assert!(requests.try_recv().is_err());
    }

    #[test]
    fn short_readings_without_context_and_invalid_readings_never_request_ai() {
        let (mut zenzai, requests, task) = fake(vec![]);
        for reading in [
            "",
            "はし",
            "English",
            "きょ\u{ee00}う",
            "きょう\n",
            &"あ".repeat(81),
        ] {
            assert_eq!(zenzai.convert(reading, ""), None);
        }
        task.join().unwrap();
        assert!(requests.try_recv().is_err());
    }

    #[test]
    fn slow_reply_falls_back_and_backs_off_without_repeating_the_request() {
        let mut reply = Reply::content("遅い返答");
        reply.delay = TIMEOUT + Duration::from_millis(200);
        let (mut zenzai, requests, task) = fake(vec![reply]);
        assert_eq!(zenzai.convert("おそいへんとう", ""), None);
        assert!(zenzai.paused_until.is_some_and(|at| at > Instant::now()));
        requests.recv().unwrap();
        for _ in 0..5 {
            assert_eq!(zenzai.convert("おそいへんとう", ""), None);
        }
        assert!(zenzai.cache.is_empty());
        task.join().unwrap();
        assert!(requests.try_recv().is_err());
    }

    #[test]
    fn japanese_gaps_share_one_request_deadline_and_new_input_resets_it() {
        let (mut zenzai, requests, task) =
            fake(vec![Reply::content("今日です"), Reply::content("明日です")]);
        zenzai.begin_request();
        let deadline = zenzai.request_deadline;
        assert_eq!(zenzai.convert("きょうです", ""), Some("今日です".into()));
        requests.recv().unwrap();
        assert_eq!(
            zenzai.request_deadline, deadline,
            "conversion must not grant each gap a fresh budget"
        );
        // Represent work consumed by preceding gaps without a speed assertion.
        zenzai.request_deadline = Some(Instant::now() - Duration::from_millis(1));
        assert_eq!(zenzai.convert("あしたです", ""), None);
        assert!(requests.try_recv().is_err());
        zenzai.begin_request();
        assert_eq!(zenzai.convert("あしたです", ""), Some("明日です".into()));
        requests.recv().unwrap();
        task.join().unwrap();
    }

    #[test]
    fn malformed_replies_fall_back_and_stop_after_three_errors() {
        let mut invalid = Reply::content("unused");
        invalid.bytes = "HTTP/1.1 200 OK\r\nContent-Length: 13\r\n\r\n{\"content\":4}".into();
        let replies = (0..3)
            .map(|_| Reply {
                bytes: invalid.bytes.clone(),
                delay: Duration::ZERO,
                keep_open: Duration::ZERO,
            })
            .collect();
        let (mut zenzai, requests, task) = fake(replies);
        for _ in 0..3 {
            assert_eq!(zenzai.convert("きょうです", ""), None);
            requests.recv().unwrap();
        }
        assert!(zenzai.server.is_none());
        assert!(zenzai.retry_at.is_some_and(|at| at > Instant::now()));
        assert_eq!(zenzai.convert("きょうです", ""), None);
        task.join().unwrap();
        assert!(requests.try_recv().is_err());
    }

    #[test]
    fn http_finishes_complete_chunked_reply_without_waiting_for_eof() {
        let reply = Reply { bytes: "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3\r\none\r\n3;ext=1\r\ntwo\r\n0\r\n\r\n".into(),
            delay: Duration::ZERO, keep_open: TIMEOUT + Duration::from_millis(200) };
        let (zenzai, requests, task) = fake(vec![reply]);
        assert_eq!(
            http(
                zenzai.server.as_ref().unwrap().endpoint,
                "GET",
                "/health",
                "",
                TIMEOUT
            )
            .unwrap(),
            b"onetwo"
        );
        requests.recv().unwrap();
        task.join().unwrap();
        assert!(chunks(b"3\r\na").is_err());
        assert!(chunks(b"0\r\n").is_err());
    }

    #[test]
    fn http_rejects_oversized_lengths_and_nonloopback_endpoints() {
        let reply = Reply {
            bytes: format!("HTTP/1.1 200 OK\r\nContent-Length: {}\r\n\r\n", usize::MAX),
            delay: Duration::ZERO,
            keep_open: Duration::ZERO,
        };
        let (zenzai, requests, task) = fake(vec![reply]);
        assert!(http(
            zenzai.server.as_ref().unwrap().endpoint,
            "GET",
            "/health",
            "",
            TIMEOUT
        )
        .is_err());
        requests.recv().unwrap();
        task.join().unwrap();
        assert!(http(
            SocketAddrV4::new(Ipv4Addr::new(192, 0, 2, 1), 80),
            "GET",
            "/health",
            "",
            TIMEOUT
        )
        .is_err());
    }

    #[test]
    fn alignment_keeps_mozc_boundaries_normalizes_width_and_excludes_particle_numbers() {
        let segments = vec![
            Segment {
                reading: "きしゃが".into(),
                candidates: vec!["記者が".into(), "汽車が".into()],
            },
            Segment {
                reading: "きた".into(),
                candidates: vec!["来た".into(), "着た".into()],
            },
        ];
        assert_eq!(
            align(&segments, "汽車が着た"),
            Some(vec![
                ("きしゃが".into(), "汽車が".into()),
                ("きた".into(), "着た".into())
            ])
        );
        assert_eq!(align(&segments, "別の文"), None);
        let digit = [Segment {
            reading: "15にち".into(),
            candidates: vec!["１５日".into()],
        }];
        assert_eq!(
            align(&digit, "15日"),
            Some(vec![("15にち".into(), "15日".into())])
        );
        let particle = [Segment {
            reading: "に".into(),
            candidates: vec!["2".into(), "に".into()],
        }];
        assert_eq!(align(&particle, "2"), None);
        assert_eq!(
            align(&particle, "に"),
            Some(vec![("に".into(), "に".into())])
        );
        let overlap = [
            Segment {
                reading: "a".into(),
                candidates: vec!["AB".into(), "A".into()],
            },
            Segment {
                reading: "b".into(),
                candidates: vec!["BC".into()],
            },
        ];
        assert_eq!(
            align(&overlap, "ABC"),
            Some(vec![("a".into(), "A".into()), ("b".into(), "BC".into())])
        );
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn linux_server_remains_alive_after_health_check() {
        use std::os::unix::fs::PermissionsExt;
        let directory = tempfile::tempdir().unwrap();
        let script = directory.path().join("server");
        std::fs::write(&script, r#"#!/usr/bin/python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
import http.server, sys
class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        data = b'{}'
        self.send_response(200)
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)
http.server.HTTPServer(('127.0.0.1', int(sys.argv[sys.argv.index('--port') + 1])), Handler).serve_forever()
"#).unwrap();
        std::fs::set_permissions(&script, std::fs::Permissions::from_mode(0o755)).unwrap();
        let server = Server::start(script, directory.path().join("model"), "", true).unwrap();
        let deadline = Instant::now() + Duration::from_secs(5);
        while server.state.load(Ordering::Acquire) == 0 {
            assert!(Instant::now() < deadline);
            thread::sleep(Duration::from_millis(20));
        }
        assert_eq!(server.state.load(Ordering::Acquire), 1);
        thread::sleep(Duration::from_millis(500));
        assert!(server
            .process
            .lock()
            .unwrap()
            .as_mut()
            .unwrap()
            .child
            .try_wait()
            .unwrap()
            .is_none());
        assert!(http(server.endpoint, "GET", "/health", "", TIMEOUT).is_ok());
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn disabling_linux_ai_terminates_and_reaps_the_server() {
        let mut command = Command::new("/bin/sleep");
        command.arg("60");
        let process = Process::start(&mut command).unwrap();
        let pid = process.child.id();
        let mut ai = Zenzai {
            paths: Some(("server".into(), "model".into())),
            server: Some(Server {
                endpoint: SocketAddrV4::new(Ipv4Addr::LOCALHOST, 1),
                state: Arc::new(AtomicU8::new(1)),
                cancelled: Arc::new(AtomicBool::new(false)),
                process: Arc::new(Mutex::new(Some(process))),
            }),
            ..Zenzai::default()
        };
        let mut settings = Settings::default();
        settings.set("UseZenzai", json!(false)).unwrap();
        ai.configure(&settings);
        assert!(ai.server.is_none());
        assert_eq!(unsafe { libc::kill(pid as i32, 0) }, -1);
        assert_eq!(io::Error::last_os_error().raw_os_error(), Some(libc::ESRCH));
    }

    #[cfg(windows)]
    fn child_process() -> (Process, tempfile::TempDir) {
        let directory = tempfile::tempdir().unwrap();
        let ready = directory.path().join("ready");
        let mut command = Command::new(std::env::current_exe().unwrap());
        command
            .args([
                "--ignored",
                "--exact",
                "windows_converter::zenzai::tests::child_process_fixture",
            ])
            .env("MELTYPE_ZENZAI_TEST_READY", &ready)
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null());
        let mut process = Process::start(&mut command).unwrap();
        let deadline = Instant::now() + Duration::from_secs(5);
        while !ready.exists() {
            assert!(
                process.child.try_wait().unwrap().is_none(),
                "child fixture exited before ready"
            );
            assert!(
                Instant::now() < deadline,
                "child fixture did not become ready"
            );
            thread::sleep(Duration::from_millis(5));
        }
        (process, directory)
    }
    #[test]
    #[ignore = "child fixture invoked only by the owned process lifecycle test"]
    #[cfg(windows)]
    fn child_process_fixture() {
        let Some(ready) = std::env::var_os("MELTYPE_ZENZAI_TEST_READY") else {
            return;
        };
        std::fs::write(ready, b"ready").unwrap();
        loop {
            thread::sleep(Duration::from_secs(30));
        }
    }
    #[test]
    #[ignore = "parent fixture invoked only by the forced shutdown lifecycle test"]
    #[cfg(windows)]
    fn parent_process_fixture() {
        let Some(ready) = std::env::var_os("MELTYPE_ZENZAI_PARENT_READY") else {
            return;
        };
        let (process, _directory) = child_process();
        std::fs::write(ready, process.child.id().to_string()).unwrap();
        loop {
            thread::sleep(Duration::from_secs(30));
        }
    }

    #[test]
    #[cfg(windows)]
    fn killing_the_owner_process_closes_its_job_and_ends_the_model_server() {
        use windows::Win32::{
            Foundation::WAIT_OBJECT_0,
            System::Threading::{OpenProcess, WaitForSingleObject, PROCESS_SYNCHRONIZE},
        };
        let directory = tempfile::tempdir().unwrap();
        let ready = directory.path().join("child-pid");
        let mut command = Command::new(std::env::current_exe().unwrap());
        command
            .args([
                "--ignored",
                "--exact",
                "windows_converter::zenzai::tests::parent_process_fixture",
            ])
            .env("MELTYPE_ZENZAI_PARENT_READY", &ready)
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null());
        // Keep the outer job open while ending only the process owning the
        // server's inner job. Its handle closing must terminate the server.
        let mut parent = Process::start(&mut command).unwrap();
        let deadline = Instant::now() + Duration::from_secs(5);
        while !ready.exists() {
            assert!(parent.child.try_wait().unwrap().is_none());
            assert!(
                Instant::now() < deadline,
                "owner fixture did not become ready"
            );
            thread::sleep(Duration::from_millis(5));
        }
        let pid = loop {
            if let Ok(pid) = std::fs::read_to_string(&ready)
                .unwrap_or_default()
                .parse::<u32>()
            {
                break pid;
            }
            assert!(
                Instant::now() < deadline,
                "owner fixture did not write child pid"
            );
            thread::yield_now();
        };
        let wait = unsafe { OpenProcess(PROCESS_SYNCHRONIZE, false, pid) }.unwrap();
        parent.child.kill().unwrap();
        parent.child.wait().unwrap();
        assert_eq!(unsafe { WaitForSingleObject(wait, 2000) }, WAIT_OBJECT_0);
        unsafe {
            let _ = CloseHandle(wait);
        }
    }

    #[test]
    #[cfg(windows)]
    fn turning_off_or_changing_paths_terminates_the_owned_server() {
        use windows::Win32::{
            Foundation::WAIT_OBJECT_0,
            System::Threading::{OpenProcess, WaitForSingleObject, PROCESS_SYNCHRONIZE},
        };
        for disable in [false, true] {
            let (process, _directory) = child_process();
            let wait =
                unsafe { OpenProcess(PROCESS_SYNCHRONIZE, false, process.child.id()) }.unwrap();
            let (mut zenzai, _requests, task) = fake(vec![]);
            task.join().unwrap();
            *zenzai.server.as_ref().unwrap().process.lock().unwrap() = Some(process);
            zenzai.paths = Some(("old-server".into(), "old-model".into()));
            let mut settings = Settings::default();
            if !disable {
                settings.0.insert("UseZenzai".into(), json!(true));
                settings
                    .0
                    .insert("ZenzaiServerPath".into(), json!("new-missing-server"));
                settings
                    .0
                    .insert("ZenzaiModelPath".into(), json!("new-missing-model"));
            }
            zenzai.configure(&settings);
            assert_eq!(unsafe { WaitForSingleObject(wait, 2000) }, WAIT_OBJECT_0);
            unsafe {
                let _ = CloseHandle(wait);
            }
            assert!(zenzai.server.is_none());
            assert!(zenzai.cache.is_empty());
        }
    }
}
