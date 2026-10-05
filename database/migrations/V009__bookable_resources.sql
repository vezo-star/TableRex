-- The reservable unit (table, booth, room, court...). Composite FKs keep the venue, section and type in one tenant,
-- and the section in the same venue. A NULL section_id skips the section FK (MATCH SIMPLE).
CREATE TABLE bookable_resources (
    id                 uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id          uuid        NOT NULL,
    venue_id           uuid        NOT NULL,
    section_id         uuid        NULL,
    resource_type_id   uuid        NOT NULL,
    name               text        NOT NULL,
    code               text        NOT NULL,
    min_capacity       integer     NOT NULL DEFAULT 1,
    capacity           integer     NOT NULL,
    is_active          boolean     NOT NULL DEFAULT true,
    is_bookable_online boolean     NOT NULL DEFAULT true,
    sort_order         integer     NOT NULL DEFAULT 0,
    attributes         jsonb       NOT NULL DEFAULT '{}'::jsonb,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_bookable_resources PRIMARY KEY (id),
    CONSTRAINT uq_bookable_resources_venue_code UNIQUE (venue_id, code),
    CONSTRAINT uq_bookable_resources_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT uq_bookable_resources_tenant_venue_id UNIQUE (tenant_id, venue_id, id),
    CONSTRAINT fk_bookable_resources_venue FOREIGN KEY (tenant_id, venue_id) REFERENCES venues (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_bookable_resources_section FOREIGN KEY (tenant_id, venue_id, section_id)
        REFERENCES venue_sections (tenant_id, venue_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_bookable_resources_type FOREIGN KEY (tenant_id, resource_type_id)
        REFERENCES resource_types (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_bookable_resources_capacity CHECK (min_capacity > 0 AND min_capacity <= capacity AND capacity <= 1000),
    CONSTRAINT ck_bookable_resources_name CHECK (length(btrim(name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_bookable_resources_code CHECK (length(btrim(code)) BETWEEN 1 AND 40),
    CONSTRAINT ck_bookable_resources_attributes CHECK (jsonb_typeof(attributes) = 'object')
);

CREATE INDEX ix_bookable_resources_venue_active ON bookable_resources (tenant_id, venue_id) WHERE is_active;
CREATE INDEX ix_bookable_resources_section_id ON bookable_resources (section_id);
CREATE INDEX ix_bookable_resources_type_id ON bookable_resources (resource_type_id);
