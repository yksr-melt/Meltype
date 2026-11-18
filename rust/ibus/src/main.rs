// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
mod component;
mod keys;
#[cfg(target_os = "linux")]
mod platform;

fn main() {
    let args: Vec<_> = std::env::args_os().collect();
    let layout_name = match args.iter().position(|arg| arg == "--keyboard-layout") {
        Some(index) => args
            .get(index + 1)
            .and_then(|value| value.to_str())
            .unwrap_or(""),
        None => "jp",
    };
    let layout = component::keyboard_layout(layout_name).unwrap_or_else(|error| {
        eprintln!("{error}");
        std::process::exit(2);
    });
    if std::env::args().any(|arg| arg == "--print-component" || arg == "--print-setup-desktop") {
        let args: Vec<_> = std::env::args_os().collect();
        let executable =
            if let Some(index) = args.iter().position(|arg| arg == "--component-executable") {
                args.get(index + 1)
                    .map(std::path::PathBuf::from)
                    .ok_or_else(|| std::io::Error::other("--component-executable requires a path"))
            } else {
                std::env::current_exe()
            };
        match executable {
            Ok(exe) => print!(
                "{}",
                if args.iter().any(|arg| arg == "--print-setup-desktop") {
                    component::desktop(&exe)
                } else {
                    component::xml(&exe, layout)
                }
            ),
            Err(error) => {
                eprintln!("{error}");
                std::process::exit(1);
            }
        }
        return;
    }
    if std::env::args().any(|arg| arg == "--version") {
        println!("Meltype-FLASH IBus {}", env!("CARGO_PKG_VERSION"));
        return;
    }
    #[cfg(target_os = "linux")]
    if let Err(error) = platform::run(layout) {
        eprintln!("Meltype IBus: {error}");
        std::process::exit(1);
    }
    #[cfg(not(target_os = "linux"))]
    {
        eprintln!("Meltype IBus requires Linux.");
        std::process::exit(1);
    }
}
