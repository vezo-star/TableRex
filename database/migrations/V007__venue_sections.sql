-- Areas inside a venue (Main, VIP, Rooftop...). UNIQUE (tenant_id, venue_id, id) lets resources reference a section
-- of the same venue only.
CREATE TABLE venue_sections (
    id          uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id   uuid        NOT NULL,
    venue_id    uuid        NOT NULL,
    name        text        NOT NULL,
    description text        NULL,
    sort_order  integer     NOT NULL DEFAULT 0,
    is_active   boolean     NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_venue_sections PRIMARY KEY (id),
    CONSTRAINT uq_venue_sections_venue_name UNIQUE (venue_id, name),
    CONSTRAINT uq_venue_sections_tenant_venue_id UNIQUE (tenant_id, venue_id, id),
    CONSTRAINT fk_venue_sections_venue FOREIGN KEY (tenant_id, venue_id) REFERENCES venues (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_venue_sections_name CHECK (length(btrim(name)) BETWEEN 1 AND 100)
);
