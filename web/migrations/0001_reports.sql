CREATE TABLE reports (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  form TEXT NOT NULL,
  title TEXT NOT NULL,
  fields TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'new',
  created_at INTEGER NOT NULL,
  upload_token_hash TEXT,
  upload_expires_at INTEGER
);

CREATE INDEX reports_form_id ON reports (form, id DESC);

CREATE TABLE attachments (
  report_id INTEGER NOT NULL REFERENCES reports (id) ON DELETE CASCADE,
  idx INTEGER NOT NULL,
  name TEXT NOT NULL,
  mime TEXT NOT NULL,
  grp TEXT NOT NULL,
  size INTEGER NOT NULL,
  uploaded_at INTEGER,
  PRIMARY KEY (report_id, idx)
);

CREATE TABLE rate_events (
  ip_hash TEXT NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE INDEX rate_events_ip ON rate_events (ip_hash, created_at);
CREATE INDEX rate_events_time ON rate_events (created_at);
