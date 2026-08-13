-- Quarantine instead of a one-way door.
--
-- Until now a source could only move one way. The health rule set is_active =
-- false after ten consecutive failures, ListActive stopped returning it, and the
-- failure counter could only be cleared by a success that would never be
-- attempted again. Coming back required a person to notice.
--
-- It cost something measurable. ANFA A.Ş. was switched off on 8 Aug 2026 because
-- the authority's hosting account was suspended; the note said "when the site is
-- back the only job is is_active = true". The site came back and nothing looked
-- again for three days.
--
-- Two columns say why a source is off, and a third paces the re-probe. The
-- reason is a closed set rather than free text: the whole point is to be able to
-- ask "which of these might have healed", and notes cannot be asked that.

ALTER TABLE source ADD COLUMN IF NOT EXISTS deactivated_at       timestamptz;
ALTER TABLE source ADD COLUMN IF NOT EXISTS deactivated_reason   text;
ALTER TABLE source ADD COLUMN IF NOT EXISTS quarantine_probed_at timestamptz;

-- health_rule       ten failures in a row, set by the monitor        -> re-probe
-- site_unreachable  a person switched it off, the server was down    -> re-probe
-- manual            a person switched it off, reason in notes        -> leave alone
-- no_tender_list    the page carries no tender listing at all        -> leave alone
-- robots            robots.txt forbids it                            -> never
-- superseded        another source already covers this authority     -> never
ALTER TABLE source DROP CONSTRAINT IF EXISTS ck_source_deactivated_reason;
ALTER TABLE source ADD CONSTRAINT ck_source_deactivated_reason CHECK (
    deactivated_reason IS NULL OR deactivated_reason IN (
        'health_rule', 'site_unreachable', 'manual',
        'no_tender_list', 'robots', 'superseded'));

-- Backfill from the 11 Aug 2026 audit, where all 19 switched-off sources were
-- checked one by one against their live pages and their whole archive. Written
-- as explicit ids because that audit is the evidence; a rule over
-- consecutive_failures would only have recognised the one automatic case.
UPDATE source SET deactivated_reason = 'health_rule'
WHERE is_active = false AND id = 15;                       -- Beypazarı, dns dead everywhere

UPDATE source SET deactivated_reason = 'site_unreachable'
WHERE is_active = false AND id IN (99, 101);               -- server-side faults, template known good

UPDATE source SET deactivated_reason = 'superseded'
WHERE is_active = false AND id IN (45, 104, 1690);         -- covered by the json_list sources 1989 / 2005

UPDATE source SET deactivated_reason = 'no_tender_list'
WHERE is_active = false AND deactivated_reason IS NULL;    -- the rest: the page simply has no tenders on it

-- Anything switched off from here on records when. The backfilled rows keep a
-- null: inventing a timestamp would be worse than admitting we do not know.

-- Only the re-probe set is indexed. Nulls first so a source that has never been
-- probed goes to the head of the queue.
DROP INDEX IF EXISTS ix_source_quarantined;
CREATE INDEX ix_source_quarantined ON source (quarantine_probed_at NULLS FIRST)
    WHERE is_active = false AND deactivated_reason IN ('health_rule', 'site_unreachable');
