// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
use std::path::Path;
fn escape(text: &str) -> String {
    text.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}
pub fn keyboard_layout(value: &str) -> Result<&'static std::ffi::CStr, &'static str> {
    match value {
        "jp" => Ok(c"jp"),
        "us" => Ok(c"us"),
        "default" => Ok(c"default"),
        _ => Err("--keyboard-layout must be jp, us or default"),
    }
}
pub fn xml(executable: &Path, layout: &std::ffi::CStr) -> String {
    let layout = layout.to_str().expect("validated keyboard layout");
    let icon = escape(&executable.with_file_name("icon.png").to_string_lossy());
    let setup = shell_path(&executable.with_file_name("meltype-flash"));
    let executable = escape(&executable.to_string_lossy());
    // IBus executes this command with GLib shell parsing, not an actual shell.
    // Single quotes are escaped for that parser before the XML escaping pass.
    let command = format!(
        "'{}' --ibus --keyboard-layout {layout}",
        executable.replace("&apos;", "&apos;\\&apos;&apos;")
    );
    format!(
        r#"<?xml version="1.0" encoding="utf-8"?>
<component>
 <name>org.freedesktop.IBus.MeltypeFlash</name>
 <description>Meltype-FLASH 日本語入力</description>
 <exec>{command}</exec>
 <version>{}</version><license>GPL-3.0-or-later</license>
 <author>Yukishiro, appipinopi</author>
 <homepage>https://github.com/appipinopi/Meltype-Flash</homepage>
 <engines><engine>
  <name>meltype-flash</name><longname>Meltype-FLASH</longname>
  <description>日本語と英語を打ち分ける日本語入力</description>
  <language>ja</language><license>GPL-3.0-or-later</license>
  <icon>{icon}</icon><setup>{setup}</setup>
  <author>Yukishiro, appipinopi</author><layout>{layout}</layout><symbol>あ</symbol><rank>0</rank>
 </engine></engines>
</component>
"#,
        env!("CARGO_PKG_VERSION")
    )
}

fn shell_path(path: &Path) -> String {
    escape(&format!(
        "'{}'",
        path.to_string_lossy().replace('\'', "'\\''")
    ))
}

// Exec はシェル用の引用とDesktop Entryの文字列エスケープを分ける。
pub fn desktop(executable: &Path) -> String {
    let path = executable.with_file_name("meltype-flash");
    let path = path.to_string_lossy();
    let quoted = path
        .replace('\\', "\\\\")
        .replace('"', "\\\"")
        .replace('`', "\\`")
        .replace('$', "\\$")
        .replace('%', "%%");
    let command = format!("\"{quoted}\"")
        .replace('\\', "\\\\")
        .replace('\n', "\\n")
        .replace('\r', "\\r")
        .replace('\t', "\\t");
    format!("[Desktop Entry]\nType=Application\nName=Meltype-FLASH 設定\nExec=/usr/bin/env -- {command}\nNoDisplay=true\nTerminal=false\nX-Meltype-Flash-IBus=true\n")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn selected_layout_is_used_for_registration_and_engine_startup() {
        for name in ["jp", "us", "default"] {
            let document = xml(
                Path::new("/tmp/日本語 & test/meltype-ibus"),
                keyboard_layout(name).unwrap(),
            );
            assert!(document.contains(&format!("<layout>{name}</layout>")));
            assert!(document.contains(&format!("--ibus --keyboard-layout {name}")));
        }
    }

    #[test]
    fn invalid_keyboard_layout_is_rejected() {
        for name in ["", "jp; command", "<layout>", "../us"] {
            assert!(keyboard_layout(name).is_err());
        }
    }
}
