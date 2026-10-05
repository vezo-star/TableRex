-- Roles and permissions. System roles have tenant_id NULL; tenant custom roles are deferred (Phase 11).
CREATE TABLE roles (
    id         uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id  uuid        NULL,
    code       text        NOT NULL,
    name       text        NOT NULL,
    is_system  boolean     NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_roles PRIMARY KEY (id),
    CONSTRAINT fk_roles_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT,
    CONSTRAINT ck_roles_code CHECK (code ~ '^[A-Z][A-Z0-9_]{1,49}$'),
    CONSTRAINT ck_roles_system_scope CHECK (NOT is_system OR tenant_id IS NULL)
);

CREATE UNIQUE INDEX uq_roles_system_code ON roles (code) WHERE tenant_id IS NULL;
CREATE UNIQUE INDEX uq_roles_tenant_code ON roles (tenant_id, code) WHERE tenant_id IS NOT NULL;

CREATE TABLE permissions (
    code        text NOT NULL,
    description text NOT NULL,

    CONSTRAINT pk_permissions PRIMARY KEY (code),
    CONSTRAINT ck_permissions_code CHECK (code ~ '^[a-z]+\.[a-z]+$')
);

CREATE TABLE role_permissions (
    role_id         uuid NOT NULL,
    permission_code text NOT NULL,

    CONSTRAINT pk_role_permissions PRIMARY KEY (role_id, permission_code),
    CONSTRAINT fk_role_permissions_role FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE CASCADE,
    CONSTRAINT fk_role_permissions_permission FOREIGN KEY (permission_code) REFERENCES permissions (code) ON DELETE CASCADE
);

CREATE TABLE tenant_user_roles (
    tenant_id      uuid        NOT NULL,
    tenant_user_id uuid        NOT NULL,
    role_id        uuid        NOT NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_tenant_user_roles PRIMARY KEY (tenant_user_id, role_id),
    CONSTRAINT fk_tenant_user_roles_tenant_user FOREIGN KEY (tenant_id, tenant_user_id)
        REFERENCES tenant_users (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_tenant_user_roles_role FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE RESTRICT
);

CREATE INDEX ix_tenant_user_roles_role_id ON tenant_user_roles (role_id);
