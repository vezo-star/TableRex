-- Phase 10: provider-independent payments (deposits), refunds and the webhook event log.
-- No provider-specific columns: a provider is a name plus opaque references (DECISIONS.md D-082).

-- Deposit policy per venue, kept apart from the booking rules so billing settings never leak into the booking model.
CREATE TABLE venue_payment_settings (
    tenant_id               uuid        NOT NULL,
    venue_id                uuid        NOT NULL,
    deposit_policy          text        NOT NULL DEFAULT 'NONE',
    deposit_amount_minor    bigint      NULL,
    currency                char(3)     NOT NULL DEFAULT 'ZAR',
    payment_window_minutes  integer     NOT NULL DEFAULT 30,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_venue_payment_settings PRIMARY KEY (tenant_id, venue_id),
    CONSTRAINT fk_venue_payment_settings_venue FOREIGN KEY (tenant_id, venue_id) REFERENCES venues (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_venue_payment_settings_policy CHECK (deposit_policy IN ('NONE', 'PER_BOOKING', 'PER_GUEST')),
    CONSTRAINT ck_venue_payment_settings_amount CHECK (
           (deposit_policy = 'NONE' AND deposit_amount_minor IS NULL)
        OR (deposit_policy <> 'NONE' AND deposit_amount_minor BETWEEN 100 AND 10000000)),
    CONSTRAINT ck_venue_payment_settings_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_venue_payment_settings_window CHECK (payment_window_minutes BETWEEN 5 AND 1440)
);

-- A payment owed for a booking. Amounts are integer minor units (cents). The provider is identified by name only;
-- provider_reference is the provider's id for the latest checkout and is informational: webhooks are matched by our id.
CREATE TABLE payments (
    id                     uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id              uuid        NOT NULL,
    venue_id               uuid        NOT NULL,
    booking_id             uuid        NOT NULL,
    purpose                text        NOT NULL DEFAULT 'DEPOSIT',
    provider               text        NOT NULL,
    provider_reference     text        NULL,
    amount_minor           bigint      NOT NULL,
    currency               char(3)     NOT NULL,
    status                 text        NOT NULL DEFAULT 'CREATED',
    refunded_amount_minor  bigint      NOT NULL DEFAULT 0,
    failure_reason         text        NULL,
    requires_attention     boolean     NOT NULL DEFAULT false,
    expires_at             timestamptz NOT NULL,
    succeeded_at           timestamptz NULL,
    version                integer     NOT NULL DEFAULT 1,
    created_at             timestamptz NOT NULL DEFAULT now(),
    updated_at             timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_payments PRIMARY KEY (id),
    CONSTRAINT uq_payments_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_payments_booking FOREIGN KEY (tenant_id, venue_id, booking_id) REFERENCES bookings (tenant_id, venue_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_payments_purpose CHECK (purpose IN ('DEPOSIT')),
    CONSTRAINT ck_payments_provider CHECK (provider ~ '^[A-Za-z][A-Za-z0-9_-]{0,39}$'),
    CONSTRAINT ck_payments_provider_reference CHECK (provider_reference IS NULL OR length(provider_reference) BETWEEN 1 AND 200),
    CONSTRAINT ck_payments_amount CHECK (amount_minor > 0),
    CONSTRAINT ck_payments_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_payments_status CHECK (status IN ('CREATED', 'PENDING', 'SUCCEEDED', 'FAILED', 'CANCELLED', 'EXPIRED')),
    CONSTRAINT ck_payments_refunded CHECK (refunded_amount_minor BETWEEN 0 AND amount_minor),
    CONSTRAINT ck_payments_refund_requires_success CHECK (refunded_amount_minor = 0 OR status = 'SUCCEEDED'),
    CONSTRAINT ck_payments_succeeded CHECK ((status = 'SUCCEEDED') = (succeeded_at IS NOT NULL)),
    CONSTRAINT ck_payments_failure_reason CHECK (failure_reason IS NULL OR length(failure_reason) <= 500)
);

CREATE INDEX ix_payments_booking ON payments (tenant_id, booking_id, created_at);
CREATE INDEX ix_payments_attention ON payments (tenant_id, created_at) WHERE requires_attention;
CREATE INDEX ix_payments_provider_reference ON payments (provider, provider_reference) WHERE provider_reference IS NOT NULL;
-- At most one live deposit per booking, so a retried checkout can never create a second charge record.
CREATE UNIQUE INDEX uq_payments_open_deposit ON payments (booking_id)
    WHERE purpose = 'DEPOSIT' AND status IN ('CREATED', 'PENDING', 'SUCCEEDED');

CREATE TABLE payment_refunds (
    id                  uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id           uuid        NOT NULL,
    payment_id          uuid        NOT NULL,
    amount_minor        bigint      NOT NULL,
    status              text        NOT NULL DEFAULT 'PENDING',
    provider_reference  text        NULL,
    reason              text        NULL,
    failure_reason      text        NULL,
    requested_by_user_id uuid       NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    completed_at        timestamptz NULL,
    CONSTRAINT pk_payment_refunds PRIMARY KEY (id),
    CONSTRAINT fk_payment_refunds_payment FOREIGN KEY (tenant_id, payment_id) REFERENCES payments (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_payment_refunds_user FOREIGN KEY (requested_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_payment_refunds_amount CHECK (amount_minor > 0),
    CONSTRAINT ck_payment_refunds_status CHECK (status IN ('PENDING', 'SUCCEEDED', 'FAILED')),
    CONSTRAINT ck_payment_refunds_completed CHECK ((status = 'PENDING') = (completed_at IS NULL)),
    CONSTRAINT ck_payment_refunds_provider_reference CHECK (provider_reference IS NULL OR length(provider_reference) BETWEEN 1 AND 200),
    CONSTRAINT ck_payment_refunds_reason CHECK (reason IS NULL OR length(reason) <= 500),
    CONSTRAINT ck_payment_refunds_failure_reason CHECK (failure_reason IS NULL OR length(failure_reason) <= 500)
);

CREATE INDEX ix_payment_refunds_payment ON payment_refunds (tenant_id, payment_id, created_at);

-- Every authenticated webhook event, once: the unique key makes redelivery a no-op. Only a hash of the body is kept,
-- never card or account data.
CREATE TABLE payment_webhook_events (
    id                 uuid        NOT NULL DEFAULT uuidv7(),
    provider           text        NOT NULL,
    provider_event_id  text        NOT NULL,
    event_type         text        NOT NULL,
    tenant_id          uuid        NULL,
    payment_id         uuid        NULL,
    outcome            text        NOT NULL,
    detail             text        NULL,
    payload_sha256     bytea       NOT NULL,
    received_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_payment_webhook_events PRIMARY KEY (id),
    CONSTRAINT uq_payment_webhook_events_event UNIQUE (provider, provider_event_id),
    CONSTRAINT fk_payment_webhook_events_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT ck_payment_webhook_events_event_id CHECK (length(provider_event_id) BETWEEN 1 AND 200),
    CONSTRAINT ck_payment_webhook_events_type CHECK (length(event_type) BETWEEN 1 AND 60),
    CONSTRAINT ck_payment_webhook_events_outcome CHECK (outcome IN ('APPLIED', 'IGNORED', 'UNMATCHED')),
    CONSTRAINT ck_payment_webhook_events_detail CHECK (detail IS NULL OR length(detail) <= 500),
    CONSTRAINT ck_payment_webhook_events_hash CHECK (octet_length(payload_sha256) = 32)
);

CREATE INDEX ix_payment_webhook_events_payment ON payment_webhook_events (payment_id, received_at) WHERE payment_id IS NOT NULL;
CREATE INDEX ix_payment_webhook_events_received_at ON payment_webhook_events (received_at);

INSERT INTO permissions (code, description) VALUES
    ('payment.view',   'View booking payments and deposit settings'),
    ('payment.refund', 'Refund payments');

INSERT INTO role_permissions (role_id, permission_code)
SELECT r.id, m.permission_code
FROM (VALUES
    ('TENANT_OWNER', 'payment.view'), ('TENANT_OWNER', 'payment.refund'),
    ('TENANT_ADMIN', 'payment.view'), ('TENANT_ADMIN', 'payment.refund'),
    ('MANAGER', 'payment.view'), ('MANAGER', 'payment.refund'),
    ('HOST', 'payment.view')
) AS m (role_code, permission_code)
JOIN roles r ON r.code = m.role_code AND r.tenant_id IS NULL;
