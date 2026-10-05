-- Short-lived provisional claims (default 5 minutes). Each hold produces HOLD allocations.
CREATE TABLE resource_holds (
    id                   uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id            uuid        NOT NULL,
    venue_id             uuid        NOT NULL,
    owner_type           text        NOT NULL,
    owner_reference      text        NOT NULL,
    token_hash           bytea       NOT NULL,
    party_size           integer     NOT NULL,
    starts_at            timestamptz NOT NULL,
    ends_at              timestamptz NOT NULL,
    expires_at           timestamptz NOT NULL,
    status               text        NOT NULL DEFAULT 'ACTIVE',
    converted_booking_id uuid        NULL,
    created_by_user_id   uuid        NULL,
    released_at          timestamptz NULL,
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_resource_holds PRIMARY KEY (id),
    CONSTRAINT uq_resource_holds_token_hash UNIQUE (token_hash),
    CONSTRAINT uq_resource_holds_tenant_venue_id UNIQUE (tenant_id, venue_id, id),
    CONSTRAINT fk_resource_holds_venue FOREIGN KEY (tenant_id, venue_id) REFERENCES venues (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_resource_holds_booking FOREIGN KEY (tenant_id, venue_id, converted_booking_id)
        REFERENCES bookings (tenant_id, venue_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_resource_holds_created_by FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_resource_holds_owner_type CHECK (owner_type IN ('USER', 'PUBLIC')),
    CONSTRAINT ck_resource_holds_owner_reference CHECK (length(owner_reference) BETWEEN 1 AND 200),
    CONSTRAINT ck_resource_holds_token_hash CHECK (octet_length(token_hash) = 32),
    CONSTRAINT ck_resource_holds_party_size CHECK (party_size BETWEEN 1 AND 1000),
    CONSTRAINT ck_resource_holds_period CHECK (ends_at > starts_at),
    CONSTRAINT ck_resource_holds_status CHECK (status IN ('ACTIVE', 'CONVERTED', 'RELEASED', 'EXPIRED')),
    CONSTRAINT ck_resource_holds_converted CHECK ((status = 'CONVERTED') = (converted_booking_id IS NOT NULL))
);

CREATE INDEX ix_resource_holds_active_expiry ON resource_holds (expires_at) WHERE status = 'ACTIVE';

ALTER TABLE booking_allocations
    ADD CONSTRAINT fk_booking_allocations_hold FOREIGN KEY (tenant_id, venue_id, hold_id)
        REFERENCES resource_holds (tenant_id, venue_id, id) ON DELETE RESTRICT;
