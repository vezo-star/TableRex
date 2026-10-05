-- Phase 9: notification delivery log and password reset requests.

-- One row per outbox message and channel that the notification dispatcher settled (sent or deliberately skipped).
-- Recipients are not stored: the booking/user ids identify them, and anonymising a customer leaves nothing behind here.
CREATE TABLE notification_deliveries (
    id                  uuid        NOT NULL DEFAULT uuidv7(),
    outbox_message_id   uuid        NOT NULL,
    tenant_id           uuid        NULL,
    channel             text        NOT NULL DEFAULT 'EMAIL',
    type                text        NOT NULL,
    template            text        NULL,
    booking_id          uuid        NULL,
    user_id             uuid        NULL,
    status              text        NOT NULL,
    provider            text        NOT NULL,
    provider_message_id text        NULL,
    detail              text        NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_notification_deliveries PRIMARY KEY (id),
    CONSTRAINT uq_notification_deliveries_message UNIQUE (outbox_message_id, channel),
    CONSTRAINT fk_notification_deliveries_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT fk_notification_deliveries_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_notification_deliveries_channel CHECK (channel IN ('EMAIL')),
    CONSTRAINT ck_notification_deliveries_status CHECK (status IN ('SENT', 'SKIPPED')),
    CONSTRAINT ck_notification_deliveries_detail CHECK (detail IS NULL OR length(detail) <= 500)
);

-- Deduplicates booking emails ("already sent the confirmed email for this booking") and serves the booking history view.
CREATE INDEX ix_notification_deliveries_booking ON notification_deliveries (booking_id, template) WHERE booking_id IS NOT NULL;
CREATE INDEX ix_notification_deliveries_created_at ON notification_deliveries (created_at);

-- Password reset: the Api records the request, the Worker generates the token when it sends the email and stores only
-- its SHA-256, so the raw token exists nowhere but in the email.
CREATE TABLE password_reset_requests (
    id             uuid        NOT NULL DEFAULT uuidv7(),
    user_id        uuid        NOT NULL,
    token_hash     bytea       NULL,
    expires_at     timestamptz NOT NULL,
    used_at        timestamptz NULL,
    invalidated_at timestamptz NULL,
    requested_ip   text        NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_password_reset_requests PRIMARY KEY (id),
    CONSTRAINT fk_password_reset_requests_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT uq_password_reset_requests_token_hash UNIQUE (token_hash),
    CONSTRAINT ck_password_reset_requests_token_hash CHECK (token_hash IS NULL OR octet_length(token_hash) = 32),
    CONSTRAINT ck_password_reset_requests_expiry CHECK (expires_at > created_at)
);

CREATE INDEX ix_password_reset_requests_user ON password_reset_requests (user_id, created_at DESC);
CREATE INDEX ix_password_reset_requests_expires_at ON password_reset_requests (expires_at);
