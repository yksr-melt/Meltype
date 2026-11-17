-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Yukishiro

-- タイピング調査 (/typing/)。参加者は名前も IP も残さず、参加ごとのランダムな番号で分ける。
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
  -- 打ったときのお手本。お手本 (sentences/*.txt) から消した文のデータも、何を打ったかが分かるように写しておく
  sentence_text TEXT NOT NULL,
  reading TEXT NOT NULL,
  -- 打鍵 ([code, key, 押した時刻, 離した時刻] の並び)。打った文字は、ここから組み立てる (public/assets/js/typing-check.js の replay)
  keys TEXT NOT NULL,
  -- 打鍵の数と、かなにしたときのお手本との一致率 (0〜1)。集計と、お手本とかけ離れた送信を見つけるのに使う
  keystrokes INTEGER NOT NULL,
  accuracy REAL NOT NULL,
  duration_ms INTEGER NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE INDEX typing_trials_session ON typing_trials (session_id);
CREATE INDEX typing_trials_accuracy ON typing_trials (accuracy);

-- 回数制限を報告とタイピング調査で分ける (調査の参加で報告の枠を使い切らないように)
ALTER TABLE rate_events ADD COLUMN kind TEXT NOT NULL DEFAULT 'report';
DROP INDEX rate_events_ip;
CREATE INDEX rate_events_ip ON rate_events (kind, ip_hash, created_at);
