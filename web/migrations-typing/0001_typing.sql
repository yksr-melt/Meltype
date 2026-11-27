-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Meltype Contributors

-- タイピング調査 (/typing/) のデータベース (TYPING_DB)。報告 (DB) と分け、タイピングのデータで容量が埋まっても報告を受け付けられるようにする。
-- 参加者は名前も IP も残さず、参加ごとのランダムな番号で分ける。
CREATE TABLE typing_sessions (
  id TEXT PRIMARY KEY,
  token_hash TEXT NOT NULL UNIQUE,
  profile TEXT NOT NULL,
  -- 同意した同意文の版 (public/assets/js/typing-consent.js)。期限は無く、同意文が変わるまで同じ参加として送れる
  consent_version INTEGER NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE TABLE typing_trials (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id TEXT NOT NULL REFERENCES typing_sessions (id) ON DELETE CASCADE,
  -- お手本の id (sentences/*.txt の文と読みから作る)。使ったお手本は消さないので、id から文と読みが引ける
  sentence_id TEXT NOT NULL,
  -- 打鍵 ([code, key, 押した時刻, 離した時刻] の並び)。打った文字・一致率は、ここから計算する (public/assets/js/typing-check.js)
  keys TEXT NOT NULL,
  duration_ms INTEGER NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE INDEX typing_trials_session ON typing_trials (session_id, created_at);
CREATE INDEX typing_trials_sentence ON typing_trials (sentence_id);
