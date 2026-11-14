// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
//! 画面に出す版番号。アプリ内では Cargo の版をそのまま表示する。
//! GitHub のリリースタグだけがゼロ埋め表記（例: flash-v0001.0001.0019）になる。

/// `1.1.19` を `v1.1.19` のように表示する。
/// 版の比較・タグ・報告先には元の文字列を使う。
pub fn display(version: &str) -> String {
    format!("v{version}")
}

/// 「ばーじょん」の候補。不具合の報告にそのまま貼れるように、OS とフロントも書く (#1868)。
pub fn candidate(front: &str) -> String {
    format!("Meltype-FLASH {} ({front})", env!("CARGO_PKG_VERSION"))
}

/// OS と入力のフロント。Windows は設定の動作モードでフロントが決まる。
/// Linux の IBus・fcitx5 は Core からは区別できないので OS だけにする。
pub fn front(settings: &crate::settings::Settings) -> String {
    if cfg!(windows) {
        let keyboard = settings.text("Mode") == "Keyboard";
        format!(
            "Windows / {}",
            if keyboard {
                "キーボード方式"
            } else {
                "IME方式"
            }
        )
    } else {
        default_front()
    }
}

/// 設定を知らない所 (辞書だけを使う所) で書く OS の名前。
pub fn default_front() -> String {
    if cfg!(windows) {
        "Windows".into()
    } else if cfg!(target_os = "macos") {
        "macOS".into()
    } else if cfg!(target_os = "android") {
        "Android".into()
    } else if cfg!(target_os = "ios") {
        "iOS".into()
    } else if cfg!(target_os = "linux") {
        "Linux".into()
    } else {
        "?".into()
    }
}

#[cfg(test)]
mod tests {
    use super::{candidate, default_front, display, front};
    use crate::settings::Settings;
    use serde_json::json;

    #[test]
    fn shows_the_cargo_version_without_padding() {
        assert_eq!(display("1.1.19"), "v1.1.19");
        assert_eq!(display("1.1.7"), "v1.1.7");
        assert_eq!(display("0.1.0-pre.7"), "v0.1.0-pre.7");
        assert_eq!(display("dev"), "vdev");
    }

    #[test]
    fn names_the_os_and_the_windows_front() {
        let mut settings = Settings::default();
        settings.set("Mode", json!("Tsf")).unwrap();
        let ime = front(&settings);
        settings.set("Mode", json!("Keyboard")).unwrap();
        let keyboard = front(&settings);
        if cfg!(windows) {
            assert_eq!(ime, "Windows / IME方式");
            assert_eq!(keyboard, "Windows / キーボード方式");
        } else {
            assert_eq!(ime, default_front());
            assert_eq!(keyboard, default_front());
        }
        assert_eq!(
            candidate("Windows / IME方式"),
            format!(
                "Meltype-FLASH {} (Windows / IME方式)",
                env!("CARGO_PKG_VERSION")
            )
        );
    }
}
