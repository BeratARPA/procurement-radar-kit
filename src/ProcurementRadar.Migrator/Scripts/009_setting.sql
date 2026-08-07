-- Settings move out of config/*.json and into the database (D-16).
--
-- Not because files are bad - they are why keyword lists are data and not
-- strings in a .cs file - but because a file cannot be edited from a phone and
-- a running worker does not notice when it changes. The files stay: they are the
-- seed, read only for keys this table does not have yet.
--
-- The value is jsonb and not a column per setting on purpose. What a setting
-- contains is the shape of the file it came from - a keyword list with weights,
-- a message template set, a regex catalogue - and none of those want a schema
-- change every time a field is added to them.
CREATE TABLE setting (
    key        text        PRIMARY KEY,
    value      jsonb       NOT NULL,
    updated_at timestamptz NOT NULL,
    updated_by text
);

-- Every previous value, kept. A settings screen that cannot be undone is worse
-- than no settings screen: one mistyped keyword silences the notifications and
-- nothing about the system looks broken while it happens.
CREATE TABLE setting_history (
    id          bigserial   PRIMARY KEY,
    key         text        NOT NULL,
    value       jsonb       NOT NULL,
    replaced_at timestamptz NOT NULL,
    replaced_by text
);

CREATE INDEX ix_setting_history_key ON setting_history (key, replaced_at DESC);
