-- Append-only audit trail. tenant_id is NULL for platform-level and authentication events.
-- old_values/new_values are written already redacted by the application (no hashes, tokens or secrets).
CREATE TABLE audit_logs (
    id             uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id      uuid        NULL,
    user_id        uuid        NULL,
    entity_type    text        NOT NULL,
    entity_id      uuid        NULL,
    action         text        NOT NULL,
    old_values     jsonb       NULL,
    new_values     jsonb       NULL,
    ip             text        NULL,
    user_agent     text        NULL,
    correlation_id text        NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_audit_logs PRIMARY KEY (id),
    CONSTRAINT fk_audit_logs_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT,
    CONSTRAINT ck_audit_logs_entity_type CHECK (length(entity_type) BETWEEN 1 AND 100),
    CONSTRAINT ck_audit_logs_action CHECK (length(action) BETWEEN 1 AND 100)
);

CREATE INDEX ix_audit_logs_tenant_created ON audit_logs (tenant_id, created_at DESC);
CREATE INDEX ix_audit_logs_tenant_entity ON audit_logs (tenant_id, entity_type, entity_id);

-- Audit rows are never updated or deleted by the application.
CREATE FUNCTION audit_logs_prevent_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'audit_logs is append-only';
END;
$$;

CREATE TRIGGER trg_audit_logs_append_only
    BEFORE UPDATE OR DELETE ON audit_logs
    FOR EACH ROW EXECUTE FUNCTION audit_logs_prevent_change();
