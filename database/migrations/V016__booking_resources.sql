-- Which resources a booking is assigned to (survives cancellation for history).
-- combination_id FK is added by V019 once resource_combinations exists.
CREATE TABLE booking_resources (
    tenant_id      uuid        NOT NULL,
    venue_id       uuid        NOT NULL,
    booking_id     uuid        NOT NULL,
    resource_id    uuid        NOT NULL,
    combination_id uuid        NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_booking_resources PRIMARY KEY (booking_id, resource_id),
    CONSTRAINT fk_booking_resources_booking FOREIGN KEY (tenant_id, venue_id, booking_id)
        REFERENCES bookings (tenant_id, venue_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_booking_resources_resource FOREIGN KEY (tenant_id, venue_id, resource_id)
        REFERENCES bookable_resources (tenant_id, venue_id, id) ON DELETE RESTRICT
);

CREATE INDEX ix_booking_resources_resource ON booking_resources (resource_id);
