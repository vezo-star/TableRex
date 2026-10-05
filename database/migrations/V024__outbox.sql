-- Transactional outbox (DATABASE.md §7). Rows are written in the business transaction, one per destination,
-- and claimed by dispatchers with FOR UPDATE SKIP LOCKED. Payloads never contain secrets.
CREATE TABLE outbox_messages (
    id            uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id     uuid        NULL,
    destination   text        NOT NULL,
    type          text        NOT NULL,
    payload       jsonb       NOT NULL,
    status        text        NOT NULL DEFAULT 'PENDING',
    attempt_count integer     NOT NULL DEFAULT 0,
    available_at  timestamptz NOT NULL DEFAULT now(),
    locked_until  timestamptz NULL,
    locked_by     text        NULL,
    processed_at  timestamptz NULL,
    last_error    text        NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_outbox_messages PRIMARY KEY (id),
    CONSTRAINT fk_outbox_messages_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT,
    CONSTRAINT ck_outbox_messages_destination CHECK (destination IN ('REALTIME', 'NOTIFICATION')),
    CONSTRAINT ck_outbox_messages_type CHECK (length(type) BETWEEN 1 AND 100),
    CONSTRAINT ck_outbox_messages_payload CHECK (jsonb_typeof(payload) = 'object'),
    CONSTRAINT ck_outbox_messages_status CHECK (status IN ('PENDING', 'PROCESSING', 'PROCESSED', 'FAILED', 'DEAD')),
    CONSTRAINT ck_outbox_messages_attempts CHECK (attempt_count >= 0),
    CONSTRAINT ck_outbox_messages_processed CHECK ((status = 'PROCESSED') = (processed_at IS NOT NULL)),
    CONSTRAINT ck_outbox_messages_last_error CHECK (last_error IS NULL OR length(last_error) <= 2000),
    CONSTRAINT ck_outbox_messages_locked_by CHECK (locked_by IS NULL OR length(locked_by) <= 200)
);

CREATE INDEX ix_outbox_messages_pending ON outbox_messages (destination, available_at)
    WHERE status IN ('PENDING', 'FAILED');
CREATE INDEX ix_outbox_messages_processing ON outbox_messages (locked_until) WHERE status = 'PROCESSING';
CREATE INDEX ix_outbox_messages_processed ON outbox_messages (processed_at) WHERE status = 'PROCESSED';
