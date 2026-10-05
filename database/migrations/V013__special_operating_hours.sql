-- Date overrides (public holidays, private events, extended hours). Rows for a date replace the weekly schedule for
-- that business date: one closed row, or one or more custom periods.
CREATE TABLE special_operating_hours (
    id         uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id  uuid        NOT NULL,
    venue_id   uuid        NOT NULL,
    date       date        NOT NULL,
    name       text        NULL,
    is_closed  boolean     NOT NULL DEFAULT false,
    open_time  time        NULL,
    close_time time        NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_special_operating_hours PRIMARY KEY (id),
    CONSTRAINT fk_special_operating_hours_venue FOREIGN KEY (tenant_id, venue_id) REFERENCES venues (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_special_operating_hours_closed CHECK (
        (is_closed AND open_time IS NULL AND close_time IS NULL)
        OR (NOT is_closed AND open_time IS NOT NULL AND close_time IS NOT NULL AND open_time <> close_time)),
    CONSTRAINT ck_special_operating_hours_name CHECK (name IS NULL OR length(name) <= 100)
);

CREATE INDEX ix_special_operating_hours_venue_date ON special_operating_hours (venue_id, date);
