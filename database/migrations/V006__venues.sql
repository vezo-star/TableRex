-- Venues (physical locations) with typed booking-rule columns, and venue-scoped staff access (D-018).
CREATE TABLE venues (
    id                           uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id                    uuid        NOT NULL,
    name                         text        NOT NULL,
    slug                         text        NOT NULL,
    time_zone                    text        NOT NULL,
    status                       text        NOT NULL DEFAULT 'ACTIVE',
    description                  text        NULL,
    address_line1                text        NULL,
    address_line2                text        NULL,
    city                         text        NULL,
    region                       text        NULL,
    postal_code                  text        NULL,
    country_code                 text        NULL,
    phone                        text        NULL,
    email                        text        NULL,
    slot_interval_minutes        integer     NOT NULL DEFAULT 15,
    default_duration_minutes     integer     NOT NULL DEFAULT 120,
    min_duration_minutes         integer     NOT NULL DEFAULT 30,
    max_duration_minutes         integer     NOT NULL DEFAULT 480,
    turnover_buffer_minutes      integer     NOT NULL DEFAULT 0,
    min_party_size               integer     NOT NULL DEFAULT 1,
    max_party_size               integer     NOT NULL DEFAULT 12,
    min_lead_time_minutes        integer     NOT NULL DEFAULT 60,
    max_advance_days             integer     NOT NULL DEFAULT 90,
    hold_ttl_seconds             integer     NOT NULL DEFAULT 300,
    auto_confirm_public_bookings boolean     NOT NULL DEFAULT true,
    created_at                   timestamptz NOT NULL DEFAULT now(),
    updated_at                   timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_venues PRIMARY KEY (id),
    CONSTRAINT uq_venues_tenant_slug UNIQUE (tenant_id, slug),
    CONSTRAINT uq_venues_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_venues_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT,
    CONSTRAINT ck_venues_status CHECK (status IN ('ACTIVE', 'INACTIVE')),
    CONSTRAINT ck_venues_slug CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(slug) <= 63),
    CONSTRAINT ck_venues_time_zone CHECK (length(btrim(time_zone)) > 0),
    CONSTRAINT ck_venues_slot_interval CHECK (slot_interval_minutes IN (5, 10, 15, 20, 30, 60)),
    CONSTRAINT ck_venues_durations CHECK (
        min_duration_minutes >= 5
        AND min_duration_minutes <= default_duration_minutes
        AND default_duration_minutes <= max_duration_minutes
        AND max_duration_minutes <= 1440),
    CONSTRAINT ck_venues_turnover CHECK (turnover_buffer_minutes BETWEEN 0 AND 240),
    CONSTRAINT ck_venues_party_size CHECK (min_party_size >= 1 AND min_party_size <= max_party_size AND max_party_size <= 1000),
    CONSTRAINT ck_venues_lead_time CHECK (min_lead_time_minutes BETWEEN 0 AND 43200),
    CONSTRAINT ck_venues_advance CHECK (max_advance_days BETWEEN 1 AND 730),
    CONSTRAINT ck_venues_hold_ttl CHECK (hold_ttl_seconds BETWEEN 60 AND 3600)
);

-- Venues assigned to members who do not have all-venue access (MANAGER, HOST, STAFF, VIEW_ONLY).
CREATE TABLE tenant_user_venues (
    tenant_id      uuid        NOT NULL,
    tenant_user_id uuid        NOT NULL,
    venue_id       uuid        NOT NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_tenant_user_venues PRIMARY KEY (tenant_user_id, venue_id),
    CONSTRAINT fk_tenant_user_venues_member FOREIGN KEY (tenant_id, tenant_user_id)
        REFERENCES tenant_users (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_tenant_user_venues_venue FOREIGN KEY (tenant_id, venue_id)
        REFERENCES venues (tenant_id, id) ON DELETE CASCADE
);

CREATE INDEX ix_tenant_user_venues_venue_id ON tenant_user_venues (venue_id);
