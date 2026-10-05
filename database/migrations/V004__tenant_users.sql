-- Tenant memberships. UNIQUE (tenant_id, id) lets child tables use composite FKs that keep rows in one tenant.
CREATE TABLE tenant_users (
    id         uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id  uuid        NOT NULL,
    user_id    uuid        NOT NULL,
    status     text        NOT NULL DEFAULT 'ACTIVE',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_tenant_users PRIMARY KEY (id),
    CONSTRAINT uq_tenant_users_tenant_user UNIQUE (tenant_id, user_id),
    CONSTRAINT uq_tenant_users_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_tenant_users_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT,
    CONSTRAINT fk_tenant_users_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_tenant_users_status CHECK (status IN ('ACTIVE', 'INVITED', 'DISABLED'))
);

CREATE INDEX ix_tenant_users_user_id ON tenant_users (user_id);
