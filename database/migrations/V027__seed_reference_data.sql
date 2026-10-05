-- Reference data only: system roles, permissions and the role -> permission matrix (SECURITY.md §3).
-- Demo tenants/users are created by `Migrations seed-dev`, never by migrations (D-016).

INSERT INTO permissions (code, description) VALUES
    ('booking.view',     'View bookings and availability'),
    ('booking.create',   'Create bookings and holds'),
    ('booking.update',   'Edit, confirm and mark bookings as no-show'),
    ('booking.cancel',   'Cancel bookings'),
    ('booking.move',     'Move bookings to another time or resource'),
    ('booking.checkin',  'Check in guests'),
    ('booking.seat',     'Seat guests and complete bookings'),
    ('resource.view',    'View resources and combinations'),
    ('resource.create',  'Create resources and combinations'),
    ('resource.update',  'Edit resources, combinations and blocks'),
    ('resource.delete',  'Delete or deactivate resources'),
    ('venue.view',       'View venues, sections and hours'),
    ('venue.manage',     'Manage venues, sections and hours'),
    ('floorplan.view',   'View floor plans'),
    ('floorplan.manage', 'Edit floor plans'),
    ('customer.view',    'View customers'),
    ('customer.manage',  'Create, edit and anonymise customers'),
    ('staff.view',       'View staff and roles'),
    ('staff.manage',     'Manage staff, roles and venue assignments'),
    ('reports.view',     'View reports'),
    ('settings.manage',  'Manage tenant settings and view audit logs');

INSERT INTO roles (code, name, is_system) VALUES
    ('TENANT_OWNER', 'Owner',     true),
    ('TENANT_ADMIN', 'Admin',     true),
    ('MANAGER',      'Manager',   true),
    ('HOST',         'Host',      true),
    ('STAFF',        'Staff',     true),
    ('VIEW_ONLY',    'View only', true);

INSERT INTO role_permissions (role_id, permission_code)
SELECT r.id, m.permission_code
FROM (VALUES
    ('TENANT_OWNER', 'booking.view'), ('TENANT_OWNER', 'booking.create'), ('TENANT_OWNER', 'booking.update'),
    ('TENANT_OWNER', 'booking.cancel'), ('TENANT_OWNER', 'booking.move'), ('TENANT_OWNER', 'booking.checkin'),
    ('TENANT_OWNER', 'booking.seat'), ('TENANT_OWNER', 'resource.view'), ('TENANT_OWNER', 'resource.create'),
    ('TENANT_OWNER', 'resource.update'), ('TENANT_OWNER', 'resource.delete'), ('TENANT_OWNER', 'venue.view'),
    ('TENANT_OWNER', 'venue.manage'), ('TENANT_OWNER', 'floorplan.view'), ('TENANT_OWNER', 'floorplan.manage'),
    ('TENANT_OWNER', 'customer.view'), ('TENANT_OWNER', 'customer.manage'), ('TENANT_OWNER', 'staff.view'),
    ('TENANT_OWNER', 'staff.manage'), ('TENANT_OWNER', 'reports.view'), ('TENANT_OWNER', 'settings.manage'),

    ('TENANT_ADMIN', 'booking.view'), ('TENANT_ADMIN', 'booking.create'), ('TENANT_ADMIN', 'booking.update'),
    ('TENANT_ADMIN', 'booking.cancel'), ('TENANT_ADMIN', 'booking.move'), ('TENANT_ADMIN', 'booking.checkin'),
    ('TENANT_ADMIN', 'booking.seat'), ('TENANT_ADMIN', 'resource.view'), ('TENANT_ADMIN', 'resource.create'),
    ('TENANT_ADMIN', 'resource.update'), ('TENANT_ADMIN', 'resource.delete'), ('TENANT_ADMIN', 'venue.view'),
    ('TENANT_ADMIN', 'venue.manage'), ('TENANT_ADMIN', 'floorplan.view'), ('TENANT_ADMIN', 'floorplan.manage'),
    ('TENANT_ADMIN', 'customer.view'), ('TENANT_ADMIN', 'customer.manage'), ('TENANT_ADMIN', 'staff.view'),
    ('TENANT_ADMIN', 'staff.manage'), ('TENANT_ADMIN', 'reports.view'), ('TENANT_ADMIN', 'settings.manage'),

    ('MANAGER', 'booking.view'), ('MANAGER', 'booking.create'), ('MANAGER', 'booking.update'),
    ('MANAGER', 'booking.cancel'), ('MANAGER', 'booking.move'), ('MANAGER', 'booking.checkin'),
    ('MANAGER', 'booking.seat'), ('MANAGER', 'resource.view'), ('MANAGER', 'resource.create'),
    ('MANAGER', 'resource.update'), ('MANAGER', 'resource.delete'), ('MANAGER', 'venue.view'),
    ('MANAGER', 'floorplan.view'), ('MANAGER', 'floorplan.manage'), ('MANAGER', 'customer.view'),
    ('MANAGER', 'customer.manage'), ('MANAGER', 'staff.view'), ('MANAGER', 'reports.view'),

    ('HOST', 'booking.view'), ('HOST', 'booking.create'), ('HOST', 'booking.update'),
    ('HOST', 'booking.cancel'), ('HOST', 'booking.move'), ('HOST', 'booking.checkin'),
    ('HOST', 'booking.seat'), ('HOST', 'resource.view'), ('HOST', 'venue.view'),
    ('HOST', 'floorplan.view'), ('HOST', 'customer.view'), ('HOST', 'customer.manage'),

    ('STAFF', 'booking.view'), ('STAFF', 'booking.checkin'), ('STAFF', 'booking.seat'),
    ('STAFF', 'resource.view'), ('STAFF', 'venue.view'), ('STAFF', 'floorplan.view'),
    ('STAFF', 'customer.view'),

    ('VIEW_ONLY', 'booking.view'), ('VIEW_ONLY', 'resource.view'), ('VIEW_ONLY', 'venue.view'),
    ('VIEW_ONLY', 'floorplan.view')
) AS m (role_code, permission_code)
JOIN roles r ON r.code = m.role_code AND r.tenant_id IS NULL;
