-- Initial schema, transcribed from docs/spec.md section 4.
--
-- D-02: an applied script is never edited. If the schema has to change, open
-- 002_*.sql instead of touching this file.
--
-- D-08: every point in time is timestamptz holding UTC. Source pages are in
-- Europe/Istanbul and the conversion happens inside Parse(), never here.

-- A single page to be polled.
CREATE TABLE source (
    id                    bigserial   PRIMARY KEY,
    authority_name        text        NOT NULL,
    authority_type        text        NOT NULL,   -- university | municipality | provincial_directorate
                                                  -- | hospital | soe_subsidiary | ekap
    province              text        NOT NULL,
    district              text,
    url                   text        NOT NULL,
    adapter_key           text        NOT NULL,   -- which parser class handles this
    adapter_config        jsonb,                  -- CSS selectors for GenericTableAdapter (T-07)
    poll_interval_minutes int         NOT NULL DEFAULT 15,
    is_active             boolean     NOT NULL DEFAULT true,
    robots_checked_at     timestamptz,
    last_fetch_at         timestamptz,
    last_success_at       timestamptz,
    last_new_notice_at    timestamptz,
    consecutive_failures  int         NOT NULL DEFAULT 0,
    notes                 text
);

-- Raw record of every fetch. Never deleted: when a parser breaks we fix it and
-- reprocess the archive.
CREATE TABLE raw_fetch (
    id             bigserial   PRIMARY KEY,
    source_id      bigint      NOT NULL REFERENCES source(id),
    fetched_at     timestamptz NOT NULL,
    http_status    int,
    etag           text,
    last_modified  text,
    content_hash   text        NOT NULL,          -- sha256, change detection
    body_path      text        NOT NULL,          -- key in the local archive
    parser_version text,
    is_processed   boolean     NOT NULL DEFAULT false
);

-- Normalized notice.
CREATE TABLE procurement_notice (
    id               bigserial   PRIMARY KEY,
    source_id        bigint      NOT NULL REFERENCES source(id),
    raw_fetch_id     bigint      NOT NULL REFERENCES raw_fetch(id),
    external_key     text        NOT NULL,        -- IKN if present, otherwise a computed hash
    ikn              text,                        -- Turkish tender registration number
    authority_name   text        NOT NULL,
    title            text        NOT NULL,
    description      text,
    procurement_type text,                        -- direct_procurement | tender | exempt
    procedure_code   text,                        -- art22d | art21f | open | restricted
    okas_codes       text[],                      -- Turkish equivalent of CPV codes
    estimated_cost   numeric,
    currency         char(3)     NOT NULL DEFAULT 'TRY',
    bid_deadline_at  timestamptz,                 -- MOST CRITICAL FIELD
    opening_at       timestamptz,
    source_url       text        NOT NULL,
    first_seen_at    timestamptz NOT NULL,        -- the competitive edge, never recomputed
    last_seen_at     timestamptz NOT NULL,
    status           text        NOT NULL DEFAULT 'new',
                                                  -- new | reviewed | bid_submitted | missed | not_relevant
    UNIQUE (external_key)
);

CREATE TABLE notice_document (
    id             bigserial PRIMARY KEY,
    notice_id      bigint NOT NULL REFERENCES procurement_notice(id),
    url            text   NOT NULL,
    file_name      text,
    mime_type      text,
    size_bytes     bigint,
    storage_path   text,
    downloaded_at  timestamptz,
    extracted_text text                           -- from pdf / docx / rar contents
);

CREATE TABLE notice_score (
    notice_id          bigint PRIMARY KEY REFERENCES procurement_notice(id),
    rule_score         int,
    llm_score          int,                       -- stays null in v1 (D-03)
    rationale          text,
    estimated_man_days int,
    risk_note          text,
    model              text,
    scored_at          timestamptz
);

CREATE TABLE notification (
    id         bigserial PRIMARY KEY,
    notice_id  bigint NOT NULL REFERENCES procurement_notice(id),
    channel    text   NOT NULL,                   -- telegram
    sent_at    timestamptz,
    status     text
);

CREATE TABLE award (
    notice_id       bigint PRIMARY KEY REFERENCES procurement_notice(id),
    winner_name     text,
    contract_amount numeric,
    bid_count       int,
    awarded_at      timestamptz
);

CREATE INDEX ix_notice_deadline ON procurement_notice (bid_deadline_at)
    WHERE status = 'new';
CREATE INDEX ix_notice_first_seen ON procurement_notice (first_seen_at DESC);
CREATE INDEX ix_raw_fetch_unprocessed ON raw_fetch (source_id, fetched_at)
    WHERE is_processed = false;
