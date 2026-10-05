-- Tenants: the businesses using the platform. The slug is used by the public booking API.
CREATE TABLE tenants (
    id                uuid        NOT NULL DEFAULT uuidv7(),
    name              text        NOT NULL,
    slug              text        NOT NULL,
    status            text        NOT NULL DEFAULT 'ACTIVE',
    default_time_zone text        NOT NULL DEFAULT 'Africa/Johannesburg',
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_tenants PRIMARY KEY (id),
    CONSTRAINT uq_tenants_slug UNIQUE (slug),
    CONSTRAINT ck_tenants_status CHECK (status IN ('ACTIVE', 'SUSPENDED', 'ARCHIVED')),
    CONSTRAINT ck_tenants_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(slug) <= 63),
    CONSTRAINT ck_tenants_name CHECK (length(btrim(name)) BETWEEN 1 AND 200),
    CONSTRAINT ck_tenants_time_zone CHECK (length(btrim(default_time_zone)) > 0)
);
