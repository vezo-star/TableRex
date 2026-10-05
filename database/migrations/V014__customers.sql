-- Customers are shared across a tenant's venues. Minimal PII; anonymised (not deleted) on erasure.
CREATE TABLE customers (
    id                uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id         uuid        NOT NULL,
    first_name        text        NOT NULL,
    last_name         text        NULL,
    email             citext      NULL,
    phone             text        NULL,
    phone_normalized  text        NULL,
    notes             text        NULL,
    preferences       jsonb       NOT NULL DEFAULT '{}'::jsonb,
    marketing_consent boolean     NOT NULL DEFAULT false,
    anonymized_at     timestamptz NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_customers PRIMARY KEY (id),
    CONSTRAINT uq_customers_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_customers_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT,
    CONSTRAINT ck_customers_first_name CHECK (length(btrim(first_name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_customers_last_name CHECK (last_name IS NULL OR length(last_name) <= 100),
    CONSTRAINT ck_customers_email CHECK (email IS NULL OR length(email) <= 254),
    CONSTRAINT ck_customers_phone CHECK (phone IS NULL OR length(phone) <= 40),
    CONSTRAINT ck_customers_phone_normalized CHECK (phone_normalized IS NULL OR phone_normalized ~ '^\+?[0-9]{4,20}$'),
    CONSTRAINT ck_customers_notes CHECK (notes IS NULL OR length(notes) <= 2000),
    CONSTRAINT ck_customers_preferences CHECK (jsonb_typeof(preferences) = 'object')
);

CREATE INDEX ix_customers_tenant_email ON customers (tenant_id, email) WHERE email IS NOT NULL;
CREATE INDEX ix_customers_tenant_phone ON customers (tenant_id, phone_normalized) WHERE phone_normalized IS NOT NULL;
CREATE INDEX ix_customers_name_trgm ON customers
    USING gin ((first_name || ' ' || coalesce(last_name, '')) gin_trgm_ops);
