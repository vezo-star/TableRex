-- Tenant-defined kinds of bookable resource (TABLE, BOOTH, ROOM, COURT...).
CREATE TABLE resource_types (
    id         uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id  uuid        NOT NULL,
    code       text        NOT NULL,
    name       text        NOT NULL,
    is_active  boolean     NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_resource_types PRIMARY KEY (id),
    CONSTRAINT uq_resource_types_tenant_code UNIQUE (tenant_id, code),
    CONSTRAINT uq_resource_types_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_resource_types_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT,
    CONSTRAINT ck_resource_types_code CHECK (code ~ '^[A-Z][A-Z0-9_]{0,39}$'),
    CONSTRAINT ck_resource_types_name CHECK (length(btrim(name)) BETWEEN 1 AND 100)
);
