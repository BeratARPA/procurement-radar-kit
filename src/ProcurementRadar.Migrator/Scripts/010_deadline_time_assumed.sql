-- Some notices give a date and no hour: "Son teklif tarihi: 12.03.2026".
-- The parser has always had to assume one, and the assumption used to be 23:59 -
-- the end of the day. Authorities close at the end of the working day, so that
-- was the dangerous direction to guess in: it told the reader they still had the
-- evening, and it pushed the six-hours-left reminder to 17:59, after the real
-- cut-off.
--
-- The assumption is now 17:00, and this column says when one was made at all.
-- Without it, a stored 17:00 that came off the page cannot be told from a 17:00
-- nobody wrote down - and only one of those two is worth acting on without
-- opening the notice first.
ALTER TABLE procurement_notice
    ADD COLUMN IF NOT EXISTS bid_deadline_time_assumed boolean NOT NULL DEFAULT false;
