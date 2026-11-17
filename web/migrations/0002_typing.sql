-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Yukishiro

-- タイピング調査 (/typing/)。参加者は名前も IP も残さず、ブラウザーごとのランダムな番号だけで分ける。
CREATE TABLE typing_sessions (
  id TEXT PRIMARY KEY,
  token_hash TEXT NOT NULL UNIQUE,
  profile TEXT NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE TABLE typing_trials (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id TEXT NOT NULL REFERENCES typing_sessions (id) ON DELETE CASCADE,
  sentence_id TEXT NOT NULL,
  typed TEXT NOT NULL,
  keys TEXT NOT NULL,
  duration_ms INTEGER NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE INDEX typing_trials_session ON typing_trials (session_id);

-- 回数制限を報告とタイピング調査で分ける (調査の参加で報告の枠を使い切らないように)
ALTER TABLE rate_events ADD COLUMN kind TEXT NOT NULL DEFAULT 'report';
DROP INDEX rate_events_ip;
CREATE INDEX rate_events_ip ON rate_events (kind, ip_hash, created_at);
