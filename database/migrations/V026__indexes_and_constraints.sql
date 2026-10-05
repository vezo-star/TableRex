-- Secondary indexes for the booking engine's read paths and Worker housekeeping.

-- Booking search by reference fragment and listing by status within a business date.
CREATE INDEX ix_bookings_reference_trgm ON bookings USING gin (reference gin_trgm_ops);
CREATE INDEX ix_bookings_venue_date_status ON bookings (tenant_id, venue_id, business_date, status);

-- Bookings that use a resource (board filters, resource history).
CREATE INDEX ix_booking_resources_tenant_resource ON booking_resources (tenant_id, resource_id, booking_id);

-- Active blocks for a venue in a time range.
CREATE INDEX ix_resource_blocks_active ON resource_blocks (tenant_id, venue_id, starts_at) WHERE status = 'ACTIVE';

-- Holds owned by one caller (release / convert lookups).
CREATE INDEX ix_resource_holds_owner ON resource_holds (tenant_id, owner_type, owner_reference) WHERE status = 'ACTIVE';

-- Booking timeline joins for the audit viewer and customer history.
CREATE INDEX ix_bookings_tenant_customer_starts ON bookings (tenant_id, customer_id, starts_at DESC) WHERE customer_id IS NOT NULL;
