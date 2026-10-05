-- V001: PostgreSQL extensions required by the schema.
-- btree_gist: scalar equality inside GiST exclusion constraints (booking allocation overlap protection).
-- pg_trgm:    trigram indexes for customer name search.
-- citext:     case-insensitive text for email addresses.

CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS citext;
