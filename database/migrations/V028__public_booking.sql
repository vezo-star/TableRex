-- Public booking settings per venue (Phase 8). Existing venues stay bookable online with a 2-hour cancellation cutoff.
ALTER TABLE venues
    ADD COLUMN public_booking_enabled            boolean NOT NULL DEFAULT true,
    ADD COLUMN public_cancellation_cutoff_minutes integer NOT NULL DEFAULT 120,
    ADD CONSTRAINT ck_venues_public_cancellation_cutoff CHECK (public_cancellation_cutoff_minutes BETWEEN 0 AND 10080);

-- Public slug lookups resolve the tenant first (uq_tenants_slug) and then the venue (uq_venues_tenant_slug); both are unique
-- indexes already. Guest manage links look bookings up by token hash (uq_bookings_public_token_hash).
