-- Two rules from spec Bölüm 10 that the schema can enforce rather than the code
-- remembering:
--
--   "Aynı notice_id için ikinci bildirim yok"
--   "bid_deadline_at'e 6 saat kala ve status = 'new' ise tek bir hatırlatma"
--
-- Both are "at most one", but of different things, so the kind has to be part of
-- the key. Without it the reminder either cannot be sent or the first message
-- can be sent twice.

ALTER TABLE notification ADD COLUMN kind text NOT NULL DEFAULT 'new_notice';

ALTER TABLE notification
    ADD CONSTRAINT notification_notice_channel_kind_key UNIQUE (notice_id, channel, kind);

-- The notifier asks "what have I not sent yet" on every pass.
CREATE INDEX ix_notification_notice ON notification (notice_id, kind);
