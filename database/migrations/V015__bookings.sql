-- Customer-facing reservations. Never deleted. Time claims live in booking_allocations (V017).
CREATE SEQUENCE booking_reference_seq AS bigint START WITH 1 INCREMENT BY 1;

CREATE TABLE bookings (
    id                  uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id           uuid        NOT NULL,
    venue_id            uuid        NOT NULL,
    customer_id         uuid        NULL,
    reference           text        NOT NULL,
    status              text        NOT NULL,
    source              text        NOT NULL,
    party_size          integer     NOT NULL,
    starts_at           timestamptz NOT NULL,
    ends_at             timestamptz NOT NULL,
    business_date       date        NOT NULL,
    special_requests    text        NULL,
    internal_notes      text        NULL,
    public_token_hash   bytea       NULL,
    expires_at          timestamptz NULL,
    created_by_user_id  uuid        NULL,
    version             integer     NOT NULL DEFAULT 1,
    confirmed_at        timestamptz NULL,
    checked_in_at       timestamptz NULL,
    seated_at           timestamptz NULL,
    completed_at        timestamptz NULL,
    cancelled_at        timestamptz NULL,
    no_show_at          timestamptz NULL,
    cancellation_reason text        NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_bookings PRIMARY KEY (id),
    CONSTRAINT uq_bookings_reference UNIQUE (reference),
    CONSTRAINT uq_bookings_public_token_hash UNIQUE (public_token_hash),
    CONSTRAINT uq_bookings_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT uq_bookings_tenant_venue_id UNIQUE (tenant_id, venue_id, id),
    CONSTRAINT fk_bookings_venue FOREIGN KEY (tenant_id, venue_id) REFERENCES venues (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_bookings_customer FOREIGN KEY (tenant_id, customer_id) REFERENCES customers (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_bookings_created_by FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_bookings_status CHECK (status IN
        ('PENDING', 'HELD', 'CONFIRMED', 'CHECKED_IN', 'SEATED', 'COMPLETED', 'CANCELLED', 'NO_SHOW')),
    CONSTRAINT ck_bookings_source CHECK (source IN ('STAFF', 'PHONE', 'WALK_IN', 'PUBLIC', 'API')),
    CONSTRAINT ck_bookings_party_size CHECK (party_size BETWEEN 1 AND 1000),
    CONSTRAINT ck_bookings_period CHECK (ends_at > starts_at),
    CONSTRAINT ck_bookings_reference CHECK (reference ~ '^BK-[0-9]{8}-[0-9]{6,}$'),
    CONSTRAINT ck_bookings_token_hash CHECK (public_token_hash IS NULL OR octet_length(public_token_hash) = 32),
    CONSTRAINT ck_bookings_held_expiry CHECK (status <> 'HELD' OR expires_at IS NOT NULL),
    CONSTRAINT ck_bookings_cancelled CHECK ((status = 'CANCELLED') = (cancelled_at IS NOT NULL)),
    CONSTRAINT ck_bookings_version CHECK (version >= 1),
    CONSTRAINT ck_bookings_special_requests CHECK (special_requests IS NULL OR length(special_requests) <= 2000),
    CONSTRAINT ck_bookings_internal_notes CHECK (internal_notes IS NULL OR length(internal_notes) <= 4000)
);

CREATE INDEX ix_bookings_venue_business_date ON bookings (tenant_id, venue_id, business_date);
CREATE INDEX ix_bookings_venue_starts_at ON bookings (tenant_id, venue_id, starts_at);
CREATE INDEX ix_bookings_customer ON bookings (tenant_id, customer_id) WHERE customer_id IS NOT NULL;
CREATE INDEX ix_bookings_active_status ON bookings (tenant_id, status)
    WHERE status IN ('PENDING', 'HELD', 'CONFIRMED', 'CHECKED_IN', 'SEATED');
CREATE INDEX ix_bookings_held_expiry ON bookings (expires_at) WHERE status = 'HELD';
