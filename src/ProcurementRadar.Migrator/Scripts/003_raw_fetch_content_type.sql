-- D-11: the archive stores the bytes exactly as they arrived, and the character
-- encoding is resolved at parse time rather than at download time.
--
-- That works for everything the body declares about itself, but not for the
-- charset in the HTTP Content-Type header: a <meta> tag lives inside the bytes,
-- an HTTP header does not. Without this column, a page that announces
-- windows-1254 only in its header would be reprocessed as UTF-8 and every
-- Turkish character in it would be lost.
--
-- The full header value is kept rather than just the charset, because T-12 will
-- want the media type when it archives specification documents.

ALTER TABLE raw_fetch ADD COLUMN content_type text;
