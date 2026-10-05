-- Idempotency keys (DATABASE.md §6). The row is inserted as the first statement of the business transaction, so a
-- failed operation rolls its key back and a retry re-executes safely. Purged by the Worker after expires_at.
-- response_body is json (not jsonb) so a replay returns the original body byte for byte.
CREATE TABLE idempotency_keys (
    id              uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id       uuid        NOT NULL,
    scope           text        NOT NULL,
    key             text        NOT NULL,
    request_hash    bytea       NOT NULL,
    status          text        NOT NULL DEFAULT 'IN_PROGRESS',
    response_status integer     NULL,
    response_body   json        NULL,
    resource_id     uuid        NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    expires_at      timestamptz NOT NULL,
    CONSTRAINT pk_idempotency_keys PRIMARY KEY (id),
    CONSTRAINT uq_idempotency_keys_scope_key UNIQUE (tenant_id, scope, key),
    CONSTRAINT fk_idempotency_keys_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT,
    CONSTRAINT ck_idempotency_keys_scope CHECK (length(scope) BETWEEN 1 AND 200),
    CONSTRAINT ck_idempotency_keys_key CHECK (length(key) BETWEEN 1 AND 100),
    CONSTRAINT ck_idempotency_keys_hash CHECK (octet_length(request_hash) = 32),
    CONSTRAINT ck_idempotency_keys_status CHECK (status IN ('IN_PROGRESS', 'COMPLETED')),
    CONSTRAINT ck_idempotency_keys_completed CHECK (status <> 'COMPLETED' OR response_status IS NOT NULL),
    CONSTRAINT ck_idempotency_keys_expiry CHECK (expires_at > created_at)
);

CREATE INDEX ix_idempotency_keys_expires_at ON idempotency_keys (expires_at);
