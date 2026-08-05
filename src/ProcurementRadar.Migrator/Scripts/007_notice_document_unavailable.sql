-- Some documents are not ours to fetch, and that is a settled answer rather
-- than a failure.
--
-- The first two live sources link every one of their specification files to
-- dosyaism.saglik.gov.tr, whose robots.txt is "User-agent: * / Disallow: /".
-- The downloader obeyed it, as it must, and then recorded thirty-two retryable
-- failures - which is wrong twice over. It schedules two more attempts at
-- something that will never be permitted, and it feeds the health counters in
-- T-15 a stream of failures for a system that is behaving exactly as designed.
-- A source with a blocked file server would look, from the alarms, like a
-- source that is breaking.
--
-- So the reason is written down instead of counted. A row with a reason is
-- never listed again.
ALTER TABLE notice_document ADD COLUMN unavailable_reason text;

-- Deliberately reversible by hand. robots.txt is a current statement, not a
-- permanent one, and if an authority opens its file server later these rows
-- should be able to come back:
--
--   UPDATE notice_document SET unavailable_reason = NULL, failed_attempts = 0
--   WHERE unavailable_reason IS NOT NULL;
--
-- Nothing re-checks automatically, because re-checking means asking a server
-- that has already said no.
COMMENT ON COLUMN notice_document.unavailable_reason IS
    'Why this document will not be fetched. NULL means it is still pending.';

-- The pending index has to know about it too, or every run walks rows it will
-- never act on.
DROP INDEX IF EXISTS ix_notice_document_pending;

CREATE INDEX ix_notice_document_pending ON notice_document (notice_id)
    WHERE downloaded_at IS NULL AND unavailable_reason IS NULL;
