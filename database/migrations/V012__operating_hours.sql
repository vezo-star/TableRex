-- Weekly schedule. Several periods per day are allowed (lunch + dinner). close_time <= open_time means the period
-- ends on the next day (overnight); the period belongs to the business date on which it opens.
CREATE TABLE operating_hours (
    id          uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id   uuid        NOT NULL,
    venue_id    uuid        NOT NULL,
    day_of_week smallint    NOT NULL,
    open_time   time        NOT NULL,
    close_time  time        NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_operating_hours PRIMARY KEY (id),
    CONSTRAINT fk_operating_hours_venue FOREIGN KEY (tenant_id, venue_id) REFERENCES venues (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_operating_hours_day CHECK (day_of_week BETWEEN 1 AND 7),
    CONSTRAINT ck_operating_hours_times CHECK (open_time <> close_time)
);

CREATE INDEX ix_operating_hours_venue_day ON operating_hours (venue_id, day_of_week);
