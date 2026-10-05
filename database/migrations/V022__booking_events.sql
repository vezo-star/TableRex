-- Business timeline of a booking (created, updated, moved, status changes, ...). Append-only.
CREATE TABLE booking_events (
    id            uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id     uuid        NOT NULL,
    venue_id      uuid        NOT NULL,
    booking_id    uuid        NOT NULL,
    event_type    text        NOT NULL,
    data          jsonb       NOT NULL DEFAULT '{}'::jsonb,
    actor_user_id uuid        NULL,
    occurred_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_booking_events PRIMARY KEY (id),
    CONSTRAINT fk_booking_events_booking FOREIGN KEY (tenant_id, venue_id, booking_id)
        REFERENCES bookings (tenant_id, venue_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_booking_events_actor FOREIGN KEY (actor_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_booking_events_type CHECK (event_type IN
        ('CREATED', 'UPDATED', 'MOVED', 'STATUS_CHANGED', 'CANCELLED', 'NOTE_ADDED')),
    CONSTRAINT ck_booking_events_data CHECK (jsonb_typeof(data) = 'object')
);

CREATE INDEX ix_booking_events_booking ON booking_events (booking_id, occurred_at);
