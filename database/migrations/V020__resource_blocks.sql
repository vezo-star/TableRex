-- Staff-created unavailability (maintenance, private events, ...). Each block produces BLOCK allocations.
CREATE TABLE resource_blocks (
    id                   uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id            uuid        NOT NULL,
    venue_id             uuid        NOT NULL,
    reason               text        NOT NULL,
    notes                text        NULL,
    starts_at            timestamptz NOT NULL,
    ends_at              timestamptz NOT NULL,
    status               text        NOT NULL DEFAULT 'ACTIVE',
    created_by_user_id   uuid        NULL,
    cancelled_at         timestamptz NULL,
    cancelled_by_user_id uuid        NULL,
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_resource_blocks PRIMARY KEY (id),
    CONSTRAINT uq_resource_blocks_tenant_venue_id UNIQUE (tenant_id, venue_id, id),
    CONSTRAINT fk_resource_blocks_venue FOREIGN KEY (tenant_id, venue_id) REFERENCES venues (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_resource_blocks_created_by FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_resource_blocks_cancelled_by FOREIGN KEY (cancelled_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_resource_blocks_reason CHECK (reason IN
        ('MAINTENANCE', 'PRIVATE_EVENT', 'CLEANING', 'MANAGEMENT', 'UNAVAILABLE', 'STAFF_USE')),
    CONSTRAINT ck_resource_blocks_notes CHECK (notes IS NULL OR length(notes) <= 1000),
    CONSTRAINT ck_resource_blocks_period CHECK (ends_at > starts_at),
    CONSTRAINT ck_resource_blocks_status CHECK (status IN ('ACTIVE', 'CANCELLED')),
    CONSTRAINT ck_resource_blocks_cancelled CHECK ((status = 'CANCELLED') = (cancelled_at IS NOT NULL))
);

CREATE INDEX ix_resource_blocks_venue_starts_at ON resource_blocks (tenant_id, venue_id, starts_at);

ALTER TABLE booking_allocations
    ADD CONSTRAINT fk_booking_allocations_block FOREIGN KEY (tenant_id, venue_id, block_id)
        REFERENCES resource_blocks (tenant_id, venue_id, id) ON DELETE RESTRICT;
