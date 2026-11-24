-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Yukishiro

-- 打鍵から打った文字を組み立てる決まりの版 (public/assets/js/typing-check.js の TYPING_VERSION)。
-- 前からある記録は、Backspace で 1 文字消していたときのもの (版 1)
ALTER TABLE typing_trials ADD COLUMN version INTEGER NOT NULL DEFAULT 1;
