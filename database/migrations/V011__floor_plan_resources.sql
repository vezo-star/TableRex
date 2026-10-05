-- Placement of a resource on a floor plan. Both FKs include venue_id, so a plan can only show resources of its venue.
CREATE TABLE floor_plan_resources (
    tenant_id     uuid        NOT NULL,
    venue_id      uuid        NOT NULL,
    floor_plan_id uuid        NOT NULL,
    resource_id   uuid        NOT NULL,
    x             numeric(8,2) NOT NULL,
    y             numeric(8,2) NOT NULL,
    width         numeric(8,2) NOT NULL,
    height        numeric(8,2) NOT NULL,
    rotation      numeric(6,2) NOT NULL DEFAULT 0,
    shape         text        NOT NULL DEFAULT 'RECT',
    visual        jsonb       NOT NULL DEFAULT '{}'::jsonb,
    updated_at    timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_floor_plan_resources PRIMARY KEY (floor_plan_id, resource_id),
    CONSTRAINT fk_floor_plan_resources_plan FOREIGN KEY (tenant_id, venue_id, floor_plan_id)
        REFERENCES floor_plans (tenant_id, venue_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_floor_plan_resources_resource FOREIGN KEY (tenant_id, venue_id, resource_id)
        REFERENCES bookable_resources (tenant_id, venue_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_floor_plan_resources_size CHECK (width > 0 AND height > 0),
    CONSTRAINT ck_floor_plan_resources_position CHECK (x >= 0 AND y >= 0),
    CONSTRAINT ck_floor_plan_resources_rotation CHECK (rotation >= 0 AND rotation < 360),
    CONSTRAINT ck_floor_plan_resources_shape CHECK (shape IN ('RECT', 'ROUND')),
    CONSTRAINT ck_floor_plan_resources_visual CHECK (jsonb_typeof(visual) = 'object')
);

CREATE INDEX ix_floor_plan_resources_resource_id ON floor_plan_resources (resource_id);
