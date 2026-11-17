-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Yukishiro

-- タイピング調査 (/typing/)。参加者は名前を残さず、参加ごとのランダムな番号で分ける。
-- ip_hash は悪質な送信を止める (typing_bans) ためだけに持ち、書き出しには入れない。
CREATE TABLE typing_sessions (
  id TEXT PRIMARY KEY,
  token_hash TEXT NOT NULL UNIQUE,
  ip_hash TEXT NOT NULL,
  profile TEXT NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE TABLE typing_bans (
  ip_hash TEXT PRIMARY KEY,
  created_at INTEGER NOT NULL
);

CREATE TABLE typing_trials (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id TEXT NOT NULL REFERENCES typing_sessions (id) ON DELETE CASCADE,
  sentence_id TEXT NOT NULL,
  typed TEXT NOT NULL,
  keys TEXT NOT NULL,
  -- 打鍵の数と、かなにしたときのお手本との一致率 (0〜1)。集計と、お手本とかけ離れた送信を見つけるのに使う
  keystrokes INTEGER NOT NULL,
  accuracy REAL NOT NULL,
  -- どこで打ったか。web はブラウザー (キーの code と離した時刻がある)、cli はターミナル (打った文字だけで、離した時刻は null)
  client TEXT NOT NULL,
  duration_ms INTEGER NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE INDEX typing_trials_session ON typing_trials (session_id);
CREATE INDEX typing_trials_accuracy ON typing_trials (accuracy);

-- 回数制限を報告とタイピング調査で分ける (調査の参加で報告の枠を使い切らないように)
ALTER TABLE rate_events ADD COLUMN kind TEXT NOT NULL DEFAULT 'report';
DROP INDEX rate_events_ip;
CREATE INDEX rate_events_ip ON rate_events (kind, ip_hash, created_at);
