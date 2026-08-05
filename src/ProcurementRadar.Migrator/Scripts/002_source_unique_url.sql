-- The seed command in T-04 has to be idempotent, which needs a conflict target.
--
-- url is the identity of a source: one listing page, one row. The obvious
-- alternative, (authority_name, adapter_key), cannot work because a single
-- authority often publishes on several pages -- docs/spec.md section 5 calls out
-- universities with separate rectorate, finance and IT listings, and those would
-- collide under the same adapter_key.
--
-- Seeding the same url twice must also never produce two rows: polling one page
-- twice would break the one-request-per-host politeness rule.

ALTER TABLE source ADD CONSTRAINT source_url_key UNIQUE (url);
