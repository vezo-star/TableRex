-- The time claim on a resource for bookings, holds and blocks. The exclusion constraint is the double-booking
-- guarantee (DATABASE.md §4): no two ACTIVE allocations of one resource may overlap. Ranges are half-open [start, end).
-- hold_id / block_id FKs are added by V018 / V020.
CREATE TABLE booking_allocations (
    id              uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id       uuid        NOT NULL,
    venue_id        uuid        NOT NULL,
    resource_id     uuid        NOT NULL,
    allocation_type text        NOT NULL,
    booking_id      uuid        NULL,
    hold_id         uuid        NULL,
    block_id        uuid        NULL,
    starts_at       timestamptz NOT NULL,
    ends_at         timestamptz NOT NULL,
    period          tstzrange   GENERATED ALWAYS AS (tstzrange(starts_at, ends_at, '[)')) STORED,
    expires_at      timestamptz NULL,
    is_active       boolean     NOT NULL DEFAULT true,
    released_at     timestamptz NULL,
    release_reason  text        NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_booking_allocations PRIMARY KEY (id),
    CONSTRAINT fk_booking_allocations_resource FOREIGN KEY (tenant_id, venue_id, resource_id)
        REFERENCES bookable_resources (tenant_id, venue_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_booking_allocations_booking FOREIGN KEY (tenant_id, venue_id, booking_id)
        REFERENCES bookings (tenant_id, venue_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_booking_allocations_type CHECK (allocation_type IN ('BOOKING', 'HOLD', 'BLOCK')),
    CONSTRAINT ck_booking_allocations_owner CHECK (
           (allocation_type = 'BOOKING' AND booking_id IS NOT NULL AND hold_id IS NULL AND block_id IS NULL)
        OR (allocation_type = 'HOLD' AND hold_id IS NOT NULL AND booking_id IS NULL AND block_id IS NULL AND expires_at IS NOT NULL)
        OR (allocation_type = 'BLOCK' AND block_id IS NOT NULL AND booking_id IS NULL AND hold_id IS NULL)),
    CONSTRAINT ck_booking_allocations_period CHECK (ends_at > starts_at),
    CONSTRAINT ck_booking_allocations_released CHECK (is_active = (released_at IS NULL)),
    CONSTRAINT ck_booking_allocations_release_reason CHECK (release_reason IS NULL OR release_reason IN
        ('CANCELLED', 'NO_SHOW', 'MOVED', 'HOLD_EXPIRED', 'HOLD_RELEASED', 'HOLD_CONVERTED', 'BLOCK_CANCELLED', 'EXPIRED')),
    CONSTRAINT ex_booking_allocations_no_overlap
        EXCLUDE USING gist (resource_id WITH =, period WITH &&) WHERE (is_active)
);

CREATE INDEX ix_booking_allocations_venue_period ON booking_allocations USING gist (venue_id, period) WHERE is_active;
CREATE INDEX ix_booking_allocations_booking ON booking_allocations (booking_id) WHERE booking_id IS NOT NULL;
CREATE INDEX ix_booking_allocations_hold ON booking_allocations (hold_id) WHERE hold_id IS NOT NULL;
CREATE INDEX ix_booking_allocations_block ON booking_allocations (block_id) WHERE block_id IS NOT NULL;
CREATE INDEX ix_booking_allocations_hold_expiry ON booking_allocations (expires_at)
    WHERE allocation_type = 'HOLD' AND is_active;
