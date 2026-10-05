-- Users are global identities that may belong to several tenants (tenant_users, V004).
CREATE TABLE users (
    id                  uuid        NOT NULL DEFAULT uuidv7(),
    email               citext      NOT NULL,
    password_hash       text        NOT NULL,
    display_name        text        NOT NULL,
    status              text        NOT NULL DEFAULT 'ACTIVE',
    is_platform_admin   boolean     NOT NULL DEFAULT false,
    failed_login_count  integer     NOT NULL DEFAULT 0,
    lockout_until       timestamptz NULL,
    last_login_at       timestamptz NULL,
    password_changed_at timestamptz NOT NULL DEFAULT now(),
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT uq_users_email UNIQUE (email),
    CONSTRAINT ck_users_status CHECK (status IN ('ACTIVE', 'LOCKED', 'DISABLED')),
    CONSTRAINT ck_users_email CHECK (length(email) BETWEEN 3 AND 254 AND position('@' IN email) > 1),
    CONSTRAINT ck_users_display_name CHECK (length(btrim(display_name)) BETWEEN 1 AND 200),
    CONSTRAINT ck_users_failed_login_count CHECK (failed_login_count >= 0)
);

-- Opaque refresh tokens, stored only as SHA-256 hashes. Rotated on every use; reuse of a rotated token
-- revokes the whole family (theft detection). Bound to a user and, once selected, a tenant.
CREATE TABLE refresh_tokens (
    id                uuid        NOT NULL DEFAULT uuidv7(),
    user_id           uuid        NOT NULL,
    tenant_id         uuid        NULL,
    token_hash        bytea       NOT NULL,
    family_id         uuid        NOT NULL,
    expires_at        timestamptz NOT NULL,
    family_expires_at timestamptz NOT NULL,
    revoked_at        timestamptz NULL,
    revoked_reason    text        NULL,
    replaced_by_id    uuid        NULL,
    created_ip        text        NULL,
    user_agent        text        NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
    CONSTRAINT uq_refresh_tokens_token_hash UNIQUE (token_hash),
    CONSTRAINT fk_refresh_tokens_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT,
    CONSTRAINT fk_refresh_tokens_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT,
    CONSTRAINT fk_refresh_tokens_replaced_by FOREIGN KEY (replaced_by_id) REFERENCES refresh_tokens (id) ON DELETE SET NULL,
    CONSTRAINT ck_refresh_tokens_hash_length CHECK (length(token_hash) = 32),
    CONSTRAINT ck_refresh_tokens_expiry CHECK (expires_at > created_at AND family_expires_at >= expires_at),
    CONSTRAINT ck_refresh_tokens_revocation CHECK ((revoked_at IS NULL) = (revoked_reason IS NULL))
);

CREATE INDEX ix_refresh_tokens_user_id ON refresh_tokens (user_id);
CREATE INDEX ix_refresh_tokens_family_id ON refresh_tokens (family_id);
CREATE INDEX ix_refresh_tokens_expires_at ON refresh_tokens (expires_at);
