-- ─── Performance + safety pragmas (applied each connection-open via Database.cs) ──
-- (Pragmas are not actually run from this file — they live in code so every connection
--  uses them consistently. This comment is here for migration auditing.)

-- ─── Media: one row per (chat, message) pair we know about ────────────────────
CREATE TABLE IF NOT EXISTS Media (
    chat_id         INTEGER NOT NULL,
    message_id      INTEGER NOT NULL,

    -- Telegram document_id for native media; NULL for URL/torrent items
    document_id     INTEGER,

    -- For URL/torrent items: SHA-1 of the canonicalised URL/magnet
    -- (so the same link in different chats can be deduped optionally)
    url_hash        TEXT,

    kind            TEXT    NOT NULL,    -- Video|Photo|Audio|File|Url|Torrent|Unknown
    size_bytes      INTEGER NOT NULL DEFAULT 0,
    date_utc        TEXT    NOT NULL,    -- ISO-8601 of the original message timestamp
    file_name       TEXT,

    status          TEXT    NOT NULL,    -- pending|queued|in_progress|done|failed|skipped
    attempts        INTEGER NOT NULL DEFAULT 0,
    last_error      TEXT,
    downloaded_path TEXT,                -- set when status='done'

    discovered_at   TEXT NOT NULL DEFAULT (datetime('now')),
    queued_at       TEXT,
    started_at      TEXT,
    completed_at    TEXT,

    PRIMARY KEY (chat_id, message_id)
);

CREATE INDEX IF NOT EXISTS IX_Media_Status        ON Media(status);
CREATE INDEX IF NOT EXISTS IX_Media_ChatStatus    ON Media(chat_id, status);
CREATE INDEX IF NOT EXISTS IX_Media_DocumentId    ON Media(document_id)  WHERE document_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_Media_UrlHash       ON Media(url_hash)     WHERE url_hash    IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_Media_DateUtc       ON Media(date_utc);

-- ─── ChatScanState: per-chat watermarks for incremental scans ────────────────
CREATE TABLE IF NOT EXISTS ChatScanState (
    chat_id              INTEGER PRIMARY KEY,
    last_scanned_msg_id  INTEGER NOT NULL DEFAULT 0,
    last_scanned_date    TEXT,
    bootstrap_complete   INTEGER NOT NULL DEFAULT 0,    -- 0/1
    last_forward_at      TEXT
);

-- ─── LegacyDedup: imported once from downloaded_ids.json on first launch ─────
-- Older WPF-app installs dedup by document_id alone (no chat_id / message_id).
-- We keep those IDs in this table so the new flow doesn't re-download files the
-- user already has, even though we don't know which chat/message they came from.
CREATE TABLE IF NOT EXISTS LegacyDedup (
    document_id INTEGER PRIMARY KEY,
    imported_at TEXT NOT NULL DEFAULT (datetime('now'))
);
