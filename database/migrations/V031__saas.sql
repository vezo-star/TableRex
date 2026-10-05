-- Phase 11: subscription plans, per-tenant limits and branding. Plans are capability reference data only: no prices,
-- invoices or billing state live here, and nothing in the booking tables refers to a plan (DECISIONS.md D-088).

CREATE TABLE plans (
    code                     text        NOT NULL,
    name                     text        NOT NULL,
    description              text        NULL,
    -- NULL limit = unlimited.
    max_venues               integer     NULL,
    max_active_staff         integer     NULL,
    max_resources_per_venue  integer     NULL,
    custom_branding          boolean     NOT NULL DEFAULT false,
    is_default               boolean     NOT NULL DEFAULT false,
    sort_order               integer     NOT NULL DEFAULT 0,
    CONSTRAINT pk_plans PRIMARY KEY (code),
    CONSTRAINT ck_plans_code CHECK (code ~ '^[A-Z][A-Z0-9_]{1,39}$'),
    CONSTRAINT ck_plans_name CHECK (length(name) BETWEEN 1 AND 100),
    CONSTRAINT ck_plans_max_venues CHECK (max_venues IS NULL OR max_venues >= 1),
    CONSTRAINT ck_plans_max_active_staff CHECK (max_active_staff IS NULL OR max_active_staff >= 1),
    CONSTRAINT ck_plans_max_resources CHECK (max_resources_per_venue IS NULL OR max_resources_per_venue >= 1)
);

-- Exactly one plan applies to tenants that have no subscription row.
CREATE UNIQUE INDEX uq_plans_default ON plans (is_default) WHERE is_default;

INSERT INTO plans (code, name, description, max_venues, max_active_staff, max_resources_per_venue, custom_branding, is_default, sort_order) VALUES
    ('STARTER',      'Starter',      'One venue and a small team.',                    1,    5,    30,   false, true,  10),
    ('PROFESSIONAL', 'Professional', 'Several locations with custom branding.',        5,    25,   100,  true,  false, 20),
    ('ENTERPRISE',   'Enterprise',   'Unlimited venues, staff and resources.',         NULL, NULL, NULL, true,  false, 30);

-- The tenant's plan plus optional per-tenant overrides (a negotiated limit). NULL override = use the plan's limit.
CREATE TABLE tenant_subscriptions (
    tenant_id                         uuid        NOT NULL,
    plan_code                         text        NOT NULL,
    max_venues_override               integer     NULL,
    max_active_staff_override         integer     NULL,
    max_resources_per_venue_override  integer     NULL,
    created_at                        timestamptz NOT NULL DEFAULT now(),
    updated_at                        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_tenant_subscriptions PRIMARY KEY (tenant_id),
    CONSTRAINT fk_tenant_subscriptions_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT fk_tenant_subscriptions_plan FOREIGN KEY (plan_code) REFERENCES plans (code) ON DELETE RESTRICT,
    CONSTRAINT ck_tenant_subscriptions_max_venues CHECK (max_venues_override IS NULL OR max_venues_override BETWEEN 1 AND 100000),
    CONSTRAINT ck_tenant_subscriptions_max_staff CHECK (max_active_staff_override IS NULL OR max_active_staff_override BETWEEN 1 AND 100000),
    CONSTRAINT ck_tenant_subscriptions_max_resources CHECK (max_resources_per_venue_override IS NULL OR max_resources_per_venue_override BETWEEN 1 AND 100000)
);

CREATE INDEX ix_tenant_subscriptions_plan ON tenant_subscriptions (plan_code);

-- Tenants that exist before plans were introduced keep working as they did: Professional covers multi-location use.
INSERT INTO tenant_subscriptions (tenant_id, plan_code)
SELECT id, 'PROFESSIONAL' FROM tenants
ON CONFLICT (tenant_id) DO NOTHING;

-- Booking-page branding. Only applied when the tenant's plan includes custom branding. Values are rendered as text,
-- a validated colour and an https image URL; never as HTML.
CREATE TABLE tenant_branding (
    tenant_id           uuid        NOT NULL,
    primary_color       text        NULL,
    logo_url            text        NULL,
    booking_page_intro  text        NULL,
    booking_terms       text        NULL,
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_tenant_branding PRIMARY KEY (tenant_id),
    CONSTRAINT fk_tenant_branding_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT ck_tenant_branding_color CHECK (primary_color IS NULL OR primary_color ~ '^#[0-9a-f]{6}$'),
    CONSTRAINT ck_tenant_branding_logo CHECK (logo_url IS NULL OR (logo_url ~ '^https://' AND length(logo_url) <= 500)),
    CONSTRAINT ck_tenant_branding_intro CHECK (booking_page_intro IS NULL OR length(booking_page_intro) <= 1000),
    CONSTRAINT ck_tenant_branding_terms CHECK (booking_terms IS NULL OR length(booking_terms) <= 2000)
);
