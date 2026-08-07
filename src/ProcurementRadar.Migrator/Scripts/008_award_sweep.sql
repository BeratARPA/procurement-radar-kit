-- The award table has been in the schema since 001 and empty ever since: the
-- result announcement is published weeks after the deadline, on a page nothing
-- was looking at. AwardWorker now goes back for it, and needs somewhere to
-- record that it looked.
--
-- A row with a null winner means "asked, no result published yet". Without it
-- every sweep would ask about every concluded tender for ever, and a tender
-- whose result never appears - they happen; a cancelled procedure produces no
-- award - would be asked about weekly until the archive outlives us.
ALTER TABLE award ADD COLUMN IF NOT EXISTS checked_at timestamptz;
ALTER TABLE award ADD COLUMN IF NOT EXISTS attempts   int NOT NULL DEFAULT 0;

-- The sweep reads notices whose deadline has passed and joins to this table.
CREATE INDEX IF NOT EXISTS ix_award_unresolved ON award (notice_id)
    WHERE winner_name IS NULL;
