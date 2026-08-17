-- Tells an empty listing apart from a listing that could not be read.
--
-- The health rule in spec.md Bölüm 11 fires on HTTP 200 + zero notices + a
-- changed hash, and it is the only rule that catches a redesigned site in hours
-- rather than in a month. It also fires on a municipality that has no open
-- tender this week, which looks identical from the database. Kahramankazan
-- served an empty <ul> for three days and raised the alarm on every one of
-- them, with correct selectors throughout.
--
-- The adapter is the only thing that can tell, because only it knows where the
-- rows were supposed to be. False is the honest default: an adapter that cannot
-- tell keeps alarming, and every row written before this column existed keeps
-- the meaning it had.
ALTER TABLE raw_fetch
    ADD COLUMN IF NOT EXISTS list_was_empty boolean NOT NULL DEFAULT false;
