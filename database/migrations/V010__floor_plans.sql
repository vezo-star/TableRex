-- Visual layouts of a venue. Presentation only; never consulted for availability.
CREATE TABLE floor_plans (
    id         uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id  uuid        NOT NULL,
    venue_id   uuid        NOT NULL,
    name       text        NOT NULL,
    width      integer     NOT NULL DEFAULT 1200,
    height     integer     NOT NULL DEFAULT 800,
    background jsonb       NOT NULL DEFAULT '{}'::jsonb,
    sort_order integer     NOT NULL DEFAULT 0,
    is_active  boolean     NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_floor_plans PRIMARY KEY (id),
    CONSTRAINT uq_floor_plans_tenant_venue_id UNIQUE (tenant_id, venue_id, id),
    CONSTRAINT fk_floor_plans_venue FOREIGN KEY (tenant_id, venue_id) REFERENCES venues (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_floor_plans_name CHECK (length(btrim(name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_floor_plans_size CHECK (width BETWEEN 200 AND 10000 AND height BETWEEN 200 AND 10000),
    CONSTRAINT ck_floor_plans_background CHECK (jsonb_typeof(background) = 'object')
);

CREATE INDEX ix_floor_plans_venue ON floor_plans (tenant_id, venue_id);
