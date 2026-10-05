-- Predefined groups of resources booked together (e.g. "Table 5 + 6" for 8 guests).
CREATE TABLE resource_combinations (
    id                 uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id          uuid        NOT NULL,
    venue_id           uuid        NOT NULL,
    name               text        NOT NULL,
    min_capacity       integer     NOT NULL DEFAULT 1,
    capacity           integer     NOT NULL,
    is_active          boolean     NOT NULL DEFAULT true,
    is_bookable_online boolean     NOT NULL DEFAULT true,
    sort_order         integer     NOT NULL DEFAULT 0,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_resource_combinations PRIMARY KEY (id),
    CONSTRAINT uq_resource_combinations_venue_name UNIQUE (venue_id, name),
    CONSTRAINT uq_resource_combinations_tenant_venue_id UNIQUE (tenant_id, venue_id, id),
    CONSTRAINT fk_resource_combinations_venue FOREIGN KEY (tenant_id, venue_id) REFERENCES venues (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_resource_combinations_name CHECK (length(btrim(name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_resource_combinations_capacity CHECK (min_capacity > 0 AND min_capacity <= capacity AND capacity <= 1000)
);

CREATE TABLE resource_combination_members (
    tenant_id      uuid    NOT NULL,
    venue_id       uuid    NOT NULL,
    combination_id uuid    NOT NULL,
    resource_id    uuid    NOT NULL,
    is_required    boolean NOT NULL DEFAULT true,
    CONSTRAINT pk_resource_combination_members PRIMARY KEY (combination_id, resource_id),
    CONSTRAINT fk_resource_combination_members_combination FOREIGN KEY (tenant_id, venue_id, combination_id)
        REFERENCES resource_combinations (tenant_id, venue_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_resource_combination_members_resource FOREIGN KEY (tenant_id, venue_id, resource_id)
        REFERENCES bookable_resources (tenant_id, venue_id, id) ON DELETE RESTRICT
);

CREATE INDEX ix_resource_combination_members_resource ON resource_combination_members (resource_id);

ALTER TABLE booking_resources
    ADD CONSTRAINT fk_booking_resources_combination FOREIGN KEY (tenant_id, venue_id, combination_id)
        REFERENCES resource_combinations (tenant_id, venue_id, id) ON DELETE RESTRICT;
