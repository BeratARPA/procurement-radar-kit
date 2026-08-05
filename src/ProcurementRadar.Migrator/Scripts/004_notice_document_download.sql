-- The parse pipeline records a document's url the moment it sees it, and a
-- separate worker downloads it later, because the pipeline may not touch the
-- network. Two things are needed for that hand-off to be safe.
--
-- A url is recorded once per notice. Reprocessing the archive runs the same
-- parse again, and without this every pass would add another row and the
-- downloader would fetch the same file again - a politeness problem, not just a
-- tidiness one.
ALTER TABLE notice_document ADD CONSTRAINT notice_document_notice_url_key UNIQUE (notice_id, url);

-- A document that cannot be fetched has to stop being retried. The downloader
-- runs on a timer; without a counter, a url that 404s forever is requested
-- forever.
ALTER TABLE notice_document ADD COLUMN failed_attempts int NOT NULL DEFAULT 0;

CREATE INDEX ix_notice_document_pending ON notice_document (notice_id)
    WHERE downloaded_at IS NULL;
