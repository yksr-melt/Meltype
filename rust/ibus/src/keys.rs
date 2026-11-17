// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro, appipinopi
#![cfg_attr(not(target_os = "linux"), allow(dead_code))]
pub fn mode_key(keyval: u32, scalar: u32, state: u32) -> Option<i32> {
    Some(match keyval {
        // Escの下がUS配列のgraveで届く環境でも半角/全角を切り替える。
        // Shift・Ctrl・Alt・Superとの組み合わせは元のキーを保持する。
        0x60 if modifiers(state) == 0 => 0x19,
        // XKB jp/PC_ALT_LEVEL2 reports Alt+Zenkaku_Hankaku as Kanji.
        // The physical key occupies the US backquote position.
        0xff21 if state & 8 != 0 => 0xc0,
        0xff21 | 0xff28..=0xff2a => 0x19,
        0xffe5 => 0x14,
        0xffbe..=0xffc9 => 0x70 + (keyval - 0xffbe) as i32,
        0xff30 | 0xff22 => 0x1a,
        0xff23 => 0x1c,
        0xff27 => 0x15,
        _ => translate(keyval, scalar)?.0,
    })
}
pub fn translate(keyval: u32, scalar: u32) -> Option<(i32, i32)> {
    let vk = match keyval {
        0xff0d | 0xff8d => 13,
        0xff09 | 0xfe20 => 9,
        0x20 => 32,
        0xff08 => 8,
        0xffff => 0x2e,
        0xff1b => 27,
        0xff51 => 0x25,
        0xff52 => 0x26,
        0xff53 => 0x27,
        0xff54 => 0x28,
        0xff50 => 0x24,
        0xff57 => 0x23,
        0xff55 => 0x21,
        0xff56 => 0x22,
        0xffc3..=0xffc7 => 0x75 + (keyval - 0xffc3) as i32,
        _ => 0,
    };
    if vk != 0 {
        return Some((vk, if vk == 32 { 32 } else { 0 }));
    }
    let c = char::from_u32(scalar).filter(|c| !c.is_control())?;
    let vk = if c.is_ascii_alphabetic() {
        c.to_ascii_uppercase() as i32
    } else if c.is_ascii_digit() {
        c as i32
    } else {
        match c {
            '`' => 0xc0,
            ',' => 0xbc,
            '.' => 0xbe,
            '-' => 0xbd,
            '/' => 0xbf,
            '[' => 0xdb,
            ']' => 0xdd,
            _ => 7,
        }
    };
    Some((vk, scalar as i32))
}
pub fn modifiers(state: u32) -> i32 {
    i32::from(state & 1 != 0)
        | (i32::from(state & 4 != 0) << 1)
        | (i32::from(state & 8 != 0) << 2)
        | (i32::from(state & ((1 << 6) | (1 << 26)) != 0) << 3)
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn maps_ibus_shortcuts_unicode_and_punctuation_without_unicode_case_expansion() {
        assert_eq!(translate(0xffc3, 0), Some((0x75, 0)));
        assert_eq!(translate(0xff8d, 0), Some((13, 0)));
        assert_eq!(translate('a' as u32, 'a' as u32), Some((65, 97)));
        for c in ['İ', '😀', 'は'] {
            assert_eq!(translate(0, c as u32), Some((7, c as i32)));
        }
        assert_eq!(translate(0, 0xd800), None);
        assert_eq!(translate(0, 0), None);
        assert_eq!(modifiers(1 | 4 | 8 | (1 << 26)), 15);
        assert_eq!(modifiers((1 << 1) | (1 << 4)), 0); // Caps/Num lock
    }
    #[test]
    fn mode_keys_include_builtins_and_configurable_space() {
        for key in [0xff21, 0xff28, 0xff29, 0xff2a] {
            assert_eq!(mode_key(key, 0, 0), Some(0x19));
        }
        assert_eq!(mode_key(0xff21, 0, 8), Some(0xc0));
        assert_eq!(mode_key(0xff21, 0, 8 | 2 | 16), Some(0xc0));
        assert_eq!(mode_key(0xffe5, 0, 0), Some(0x14));
        assert_eq!(mode_key(0xffbe, 0, 0), Some(0x70));
        assert_eq!(mode_key(0xffc9, 0, 0), Some(0x7b));
        assert_eq!(mode_key(0x60, 0x60, 0), Some(0x19));
        assert_eq!(mode_key(0x60, 0x60, 2 | 16), Some(0x19));
        for state in [1, 4, 8, 64] {
            assert_eq!(mode_key(0x60, 0x60, state), Some(0xc0));
        }
        assert_eq!(mode_key(0x27, 0x27, 0), Some(7));
        assert_eq!(mode_key(0xff22, 0, 0), Some(0x1a));
        assert_eq!(mode_key(0xff23, 0, 0), Some(0x1c));
        assert_eq!(mode_key(32, 32, 0), Some(32));
    }
}
