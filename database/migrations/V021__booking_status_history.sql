-- Every booking status change (DOMAIN_MODEL §4). Append-only; from_status is NULL for the initial status.
CREATE TABLE booking_status_history (
    id                 uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id          uuid        NOT NULL,
    venue_id           uuid        NOT NULL,
    booking_id         uuid        NOT NULL,
    from_status        text        NULL,
    to_status          text        NOT NULL,
    source             text        NOT NULL,
    reason             text        NULL,
    changed_by_user_id uuid        NULL,
    changed_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_booking_status_history PRIMARY KEY (id),
    CONSTRAINT fk_booking_status_history_booking FOREIGN KEY (tenant_id, venue_id, booking_id)
        REFERENCES bookings (tenant_id, venue_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_booking_status_history_user FOREIGN KEY (changed_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_booking_status_history_from CHECK (from_status IS NULL OR from_status IN
        ('PENDING', 'HELD', 'CONFIRMED', 'CHECKED_IN', 'SEATED', 'COMPLETED', 'CANCELLED', 'NO_SHOW')),
    CONSTRAINT ck_booking_status_history_to CHECK (to_status IN
        ('PENDING', 'HELD', 'CONFIRMED', 'CHECKED_IN', 'SEATED', 'COMPLETED', 'CANCELLED', 'NO_SHOW')),
    CONSTRAINT ck_booking_status_history_source CHECK (source IN ('STAFF', 'PUBLIC', 'SYSTEM')),
    CONSTRAINT ck_booking_status_history_reason CHECK (reason IS NULL OR length(reason) <= 500)
);

CREATE INDEX ix_booking_status_history_booking ON booking_status_history (booking_id, changed_at);
