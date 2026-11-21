// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
//! Per-worker engine lifetime and per-application HybridConverter policy.
use super::{
    candidates::Candidates,
    system::{Apartment, System},
    zenzai::Zenzai,
};
use crate::{Callbacks, CALLBACKS};
use meltype_core::mozc::{Mozc, Segment};
use std::{
    cell::RefCell,
    collections::HashMap,
    ffi::c_char,
    path::PathBuf,
    sync::mpsc::{self, Receiver},
};

#[derive(Clone, Copy, PartialEq, Eq)]
enum Mode {
    Hybrid,
    Mozc,
    System,
}
impl Mode {
    fn parse(mode: &str) -> Self {
        match mode {
            "Mozc" => Self::Mozc,
            "System" => Self::System,
            _ => Self::Hybrid,
        }
    }
}
struct Backend {
    mode: Mode,
    system: System,
    extras: Candidates,
    helper: Option<PathBuf>,
    profile: PathBuf,
    mozc: Option<Mozc>,
    learning: Option<std::thread::JoinHandle<(Mozc, std::io::Result<()>)>>,
    pending_learning: Vec<meltype_core::session::EngineLearning>,
    starting: Option<Receiver<std::io::Result<Mozc>>>,
    failures: u32,
    conversions: HashMap<(String, String), Option<Vec<Segment>>>,
    candidates: HashMap<String, Vec<String>>,
    zenzai: Zenzai,
    shunti: crate::shunti::Shunti,
    neural_segments: Vec<(String, String)>,
    shunti_candidates: HashMap<String, Vec<String>>,
}
impl Backend {
    fn available(&self) -> bool {
        self.failures < 3 && self.helper.as_ref().is_some_and(|p| p.is_file())
    }
    fn warm_up(&mut self) {
        self.extras.begin_request();
        self.finish_learning();
        let completed = self.starting.as_ref().and_then(|r| match r.try_recv() {
            Ok(result) => Some(result),
            Err(mpsc::TryRecvError::Disconnected) => {
                Some(Err(std::io::Error::other("Mozc startup ended")))
            }
            Err(mpsc::TryRecvError::Empty) => None,
        });
        if let Some(result) = completed {
            self.starting = None;
            match result {
                Ok(backend) => self.mozc = Some(backend),
                Err(_) => self.failures += 1,
            }
        }
        if self.mode != Mode::System
            && self.available()
            && self.mozc.is_none()
            && self.starting.is_none()
            && self.learning.is_none()
        {
            let helper = self.helper.clone().unwrap();
            let profile = self.profile.clone();
            let (sender, receiver) = mpsc::sync_channel(1);
            self.starting = Some(receiver);
            std::thread::spawn(move || {
                let _ = sender.send(Mozc::start(&helper, Some(&profile)));
            });
        }
    }
    fn remember_candidates(&mut self, segments: &[Segment], bounded: bool) {
        for segment in segments {
            if bounded && self.candidates.len() > 512 {
                self.candidates.clear();
            }
            self.candidates
                .insert(segment.reading.clone(), segment.candidates.clone());
        }
    }
    fn mozc_request(&mut self, reading: &str, context: &str) -> Option<Vec<Segment>> {
        if self.mode == Mode::System || !self.available() || reading.is_empty() {
            return None;
        }
        self.finish_learning();
        let clean = |s: &str| s.replace(['\t', '\r', '\n'], " ");
        let key = (clean(context), clean(reading));
        if let Some(known) = self.conversions.get(&key).cloned() {
            if let Some(ref segments) = known {
                self.remember_candidates(segments, false);
            }
            return known;
        }
        let response = self.mozc.as_mut()?.query(reading, context);
        match response {
            Ok(segments) => {
                self.failures = 0;
                if let Some(ref segments) = segments {
                    self.remember_candidates(segments, true);
                }
                if self.conversions.len() > 512 {
                    self.conversions.clear();
                }
                self.conversions.insert(key, segments.clone());
                segments
            }
            Err(_) => {
                self.mozc = None;
                self.failures += 1;
                self.conversions.clear();
                self.candidates.clear();
                None
            }
        }
    }
    fn finish_learning(&mut self) {
        if let Some(result) = take_finished(&mut self.learning) {
            match result {
                Ok((engine, Ok(()))) => {
                    self.mozc = Some(engine);
                    self.failures = 0;
                }
                _ => {
                    self.failures += 1;
                    self.conversions.clear();
                    self.candidates.clear();
                }
            }
        }
    }
    fn learn(&mut self, batches: Vec<meltype_core::session::EngineLearning>) {
        if self.mode == Mode::System || !self.available() {
            return;
        }
        self.pending_learning.extend(batches);
        self.finish_learning();
        if self.pending_learning.is_empty() {
            return;
        }
        let Some(mut engine) = self.mozc.take() else {
            return;
        };
        let batches = std::mem::take(&mut self.pending_learning);
        self.conversions.clear();
        self.candidates.clear();
        // Only helper IPC runs here; System and WinRT remain on their STA.
        self.learning = Some(std::thread::spawn(move || {
            let result = batches
                .into_iter()
                .try_for_each(|batch| engine.learn(&batch.context, &batch.clauses).map(|_| ()));
            (engine, result)
        }));
    }
    fn use_system(&self) -> bool {
        self.mode != Mode::Mozc || !self.available()
    }
    fn convert(&mut self, reading: &str) -> Option<String> {
        if let Some(segments) = self.mozc_request(reading, "") {
            return Some(
                segments
                    .iter()
                    .map(|s| s.candidates.first().unwrap_or(&s.reading).as_str())
                    .collect(),
            );
        }
        self.use_system()
            .then(|| {
                self.system
                    .convert(&reading.encode_utf16().collect::<Vec<_>>())
            })
            .flatten()
            .map(|text| String::from_utf16_lossy(&text))
    }
    fn clauses(&mut self, reading: &str, context: &str) -> Option<Vec<(String, String)>> {
        self.neural_segments.clear();
        self.shunti_candidates.clear();
        if let Some(clauses) = self.shunti.convert(reading, context) {
            for clause in &clauses {
                // 変換候補の一覧にも、読みと合わない綴り返し・文節混入を出さない。
                // 先頭の候補は shunti.convert で確かめてあるので、空にはならない
                let candidates: Vec<_> = clause
                    .candidates
                    .iter()
                    .filter(|candidate| {
                        meltype_core::plausible::plausible(&clause.reading, candidate)
                    })
                    .cloned()
                    .collect();
                if !candidates.is_empty() {
                    self.shunti_candidates
                        .insert(clause.reading.clone(), candidates);
                }
            }
            return Some(
                clauses
                    .into_iter()
                    .map(|c| {
                        let text = c
                            .candidates
                            .first()
                            .cloned()
                            .unwrap_or_else(|| c.reading.clone());
                        (c.reading, text)
                    })
                    .collect(),
            );
        }
        let from_mozc = self.mozc_request(reading, context);
        let ordinary = if let Some(segments) = &from_mozc {
            Some(
                segments
                    .iter()
                    .map(|s| {
                        let text = s
                            .candidates
                            .first()
                            .cloned()
                            .unwrap_or_else(|| s.reading.clone());
                        (s.reading.clone(), text)
                    })
                    .collect(),
            )
        } else if self.use_system() {
            self.system
                .clauses(
                    &reading.encode_utf16().collect::<Vec<_>>(),
                    &context.encode_utf16().collect::<Vec<_>>(),
                )
                .map(|segments| {
                    segments
                        .into_iter()
                        .map(|s| {
                            (
                                String::from_utf16_lossy(&s.reading),
                                String::from_utf16_lossy(&s.text),
                            )
                        })
                        .collect()
                })
        } else {
            None
        };
        let Some(text) = self.zenzai.convert(reading, context) else {
            return ordinary;
        };
        if ordinary.as_ref().is_some_and(|clauses| {
            clauses
                .iter()
                .map(|(_, text)| text.as_str())
                .collect::<String>()
                == text
        }) {
            self.neural_segments = ordinary.clone().unwrap_or_default();
            return ordinary;
        }
        // Mozc の文節の候補を選んでつなげると作れる答え (読みと表記が違う語: きずく → 気づく) は、
        // Mozc の文節の区切りで使う。作れない答えは、かなが読みと合うときだけ使う (壊れた答えを出さない)
        let accepted = from_mozc
            .as_ref()
            .and_then(|segments| super::zenzai::align(segments, &text))
            .or_else(|| {
                meltype_core::plausible::plausible(reading, &text)
                    .then(|| vec![(reading.to_owned(), text)])
            });
        self.neural_segments = accepted.clone().unwrap_or_default();
        accepted.or(ordinary)
    }
    fn candidates(&mut self, reading: &str) -> Vec<String> {
        let mut result = Vec::new();
        if self.mode != Mode::System && self.available() {
            self.finish_learning();
            if let Some(known) = self.candidates.get(reading) {
                result = known.clone();
            } else if let Some(segments) = self.mozc_request(reading, "") {
                result = if segments.len() == 1 {
                    segments[0].candidates.clone()
                } else {
                    vec![segments
                        .iter()
                        .map(|s| s.candidates.first().unwrap_or(&s.reading).as_str())
                        .collect()]
                };
            }
        }
        if self.mode != Mode::Mozc {
            for candidate in self.extras.get(reading) {
                if !result.contains(&candidate) {
                    result.push(candidate);
                }
            }
        }
        if let Some(candidates) = self.shunti_candidates.get(reading) {
            let mut combined = candidates.clone();
            combined.extend(result.into_iter().filter(|s| !candidates.contains(s)));
            return combined;
        }
        result
    }
}
impl Drop for Backend {
    fn drop(&mut self) {
        // Shutdown may wait for the bounded helper operation. Key processing
        // must only poll completion: the TSF deadline is shorter than Mozc IPC.
        if let Some(task) = self.learning.take() {
            let _ = task.join();
        }
    }
}
fn take_finished<T>(
    task: &mut Option<std::thread::JoinHandle<T>>,
) -> Option<std::thread::Result<T>> {
    if task.as_ref().is_some_and(|task| task.is_finished()) {
        task.take().map(|task| task.join())
    } else {
        None
    }
}
pub(crate) fn has_worker() -> bool {
    BACKEND
        .try_with(|state| state.try_borrow().ok().is_some_and(|s| s.is_some()))
        .unwrap_or(false)
}
pub(crate) fn learn(batches: Vec<meltype_core::session::EngineLearning>) {
    with_backend(|backend| backend.learn(batches));
}
thread_local! { static BACKEND: RefCell<Option<Backend>> = const { RefCell::new(None) }; }
fn with_backend<T>(f: impl FnOnce(&mut Backend) -> T) -> Option<T> {
    BACKEND
        .try_with(|state| state.try_borrow_mut().ok()?.as_mut().map(f))
        .ok()
        .flatten()
}
pub(crate) fn generated_candidates(reading: &str) -> Vec<String> {
    with_backend(|backend| {
        if let Some(values) = backend.shunti_candidates.get(reading) {
            return values.clone();
        }
        backend
            .neural_segments
            .iter()
            .filter(|(r, _)| r == reading)
            .map(|(_, t)| t.clone())
            .collect()
    })
    .unwrap_or_default()
}
unsafe extern "C" fn convert(reading: *const c_char) -> *mut c_char {
    let value = crate::read(reading).and_then(|s| with_backend(|b| b.convert(&s)).flatten());
    value.map_or(std::ptr::null_mut(), |s| crate::allocate(&s))
}
unsafe extern "C" fn clauses(reading: *const c_char, context: *const c_char) -> *mut c_char {
    let context = crate::read(context).unwrap_or_default();
    let value =
        crate::read(reading).and_then(|s| with_backend(|b| b.clauses(&s, &context)).flatten());
    value.map_or(std::ptr::null_mut(), |segments| {
        crate::allocate(
            &segments
                .into_iter()
                .map(|(r, t)| format!("{r}\t{t}"))
                .collect::<Vec<_>>()
                .join("\n"),
        )
    })
}
unsafe extern "C" fn candidates(reading: *const c_char) -> *mut c_char {
    let value = crate::read(reading).and_then(|s| with_backend(|b| b.candidates(&s)));
    value.map_or(std::ptr::null_mut(), |values| {
        crate::allocate(&values.join("\n"))
    })
}

/// Pins COM to the worker's original STA thread and restores callbacks on drop.
pub struct WorkerConverters {
    previous: Callbacks,
    _apartment: Apartment,
}
impl Drop for WorkerConverters {
    fn drop(&mut self) {
        BACKEND.with(|state| {
            state.borrow_mut().take();
        });
        if let Ok(mut callbacks) = CALLBACKS.lock() {
            *callbacks = self.previous;
        }
    }
}
pub fn install() -> Option<WorkerConverters> {
    // Private SDK hosts exercise document mechanics without an installed engine.
    if std::env::var_os("MELTYPE_TEST_TIP_NO_CONVERTERS").is_some_and(|v| v == "1") {
        return None;
    }
    if !BACKEND
        .try_with(|state| state.try_borrow().ok().is_some_and(|s| s.is_none()))
        .ok()?
    {
        return None;
    }
    let apartment = Apartment::enter()?;
    let helper = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|p| p.join("mozc/meltype_mozc_helper.exe")));
    let mut callbacks = CALLBACKS.lock().ok()?;
    let previous = *callbacks;
    BACKEND.with(|state| {
        *state.borrow_mut() = Some(Backend {
            mode: Mode::Hybrid,
            system: System::default(),
            extras: Candidates::default(),
            helper,
            profile: meltype_core::storage::Store::default_directory().join("mozc"),
            mozc: None,
            learning: None,
            pending_learning: Vec::new(),
            starting: None,
            failures: 0,
            conversions: HashMap::new(),
            candidates: HashMap::new(),
            zenzai: Zenzai::default(),
            shunti: crate::shunti::Shunti::default(),
            neural_segments: Vec::new(),
            shunti_candidates: HashMap::new(),
        })
    });
    *callbacks = Callbacks {
        clauses: Some(clauses),
        candidates: Some(candidates),
        convert: Some(convert),
    };
    Some(WorkerConverters {
        previous,
        _apartment: apartment,
    })
}
pub(crate) struct Scope(Option<Mode>);
impl Drop for Scope {
    fn drop(&mut self) {
        if let Some(previous) = self.0 {
            with_backend(|b| b.mode = previous);
        }
    }
}
pub(crate) fn scope(settings: &meltype_core::settings::Settings) -> Scope {
    crate::currency_auto::start();
    Scope(
        with_backend(|backend| {
            if backend.shunti.configure(settings) {
                backend.shunti_candidates.clear();
            }
            backend.zenzai.configure(settings);
            backend.zenzai.begin_request();
            if settings.boolean("Enabled") {
                let previous = backend.mode;
                backend.mode = Mode::parse(settings.text("ConversionEngine"));
                backend.warm_up();
                Some(previous)
            } else {
                None
            }
        })
        .flatten(),
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn slow_learning_is_polled_without_blocking_the_input_thread() {
        let (release, waiting) = mpsc::channel();
        let mut task = Some(std::thread::spawn(move || {
            waiting
                .recv_timeout(std::time::Duration::from_secs(5))
                .unwrap();
            42
        }));
        // Completion is impossible until the input thread releases the worker.
        // A blocking join here would time out and panic inside the worker.
        assert!(take_finished(&mut task).is_none());
        assert!(task.is_some());
        release.send(()).unwrap();
        task.as_ref().unwrap().thread().unpark();
        while !task.as_ref().unwrap().is_finished() {
            std::thread::yield_now();
        }
        assert_eq!(take_finished(&mut task).unwrap().unwrap(), 42);
        assert!(task.is_none());
    }
}
