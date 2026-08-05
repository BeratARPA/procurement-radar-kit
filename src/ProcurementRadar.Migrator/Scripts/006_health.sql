-- Spec Bölüm 11: the dangerous failure is not a crash, it is silence. Two things
-- are missing before that can be watched for.

-- How many notices a parse produced. Not how many were new - a healthy source
-- re-offers the same notices for days and adds none. "The parser returned
-- nothing at all from a page that had changed" is the signal, and there is
-- currently nowhere to see it: an archive row exists only when the content hash
-- differed, so a processed row carrying zero is exactly the condition the spec
-- describes.
ALTER TABLE raw_fetch ADD COLUMN notice_count int;

-- Raised alarms, so the same one is not sent every minute for a week.
CREATE TABLE health_alert (
    id        bigserial   PRIMARY KEY,
    kind      text        NOT NULL,   -- fetch_failures | source_deactivated | parser_broken
                                      -- | silent_source | nothing_collected
    source_id bigint      REFERENCES source(id),   -- null when it is about the system
    raised_at timestamptz NOT NULL,
    detail    text
);

CREATE INDEX ix_health_alert_recent ON health_alert (kind, source_id, raised_at DESC);
