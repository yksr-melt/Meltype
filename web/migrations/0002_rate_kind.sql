-- SPDX-License-Identifier: GPL-3.0-or-later
-- Copyright (C) 2026 Meltype Contributors

-- 回数制限を報告とタイピング調査で分ける (調査の参加で報告の枠を使い切らないように)
ALTER TABLE rate_events ADD COLUMN kind TEXT NOT NULL DEFAULT 'report';
DROP INDEX rate_events_ip;
CREATE INDEX rate_events_ip ON rate_events (kind, ip_hash, created_at);
