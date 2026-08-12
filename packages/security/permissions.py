"""Granular Permission Matrix and Role-Based Authorization Definitions.

Provides fine-grained permission enums (STUDY_READ, FORM_WRITE, DATA_LOCK, SDV_VERIFY,
AUDIT_VIEW, etc.) and role-to-permission mapping for Cadence Clinical eClinical platform.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""


class DynamicStrEnumMeta(type):
    def __getattr__(cls, name):
        if name in cls._members:
            return cls._members[name]
        raise AttributeError(f"'{cls.__name__}' object has no attribute '{name}'")

    def __iter__(cls):
        return iter(cls._members.values())

    def __len__(cls):
        return len(cls._members)

    def __contains__(cls, item):
        if isinstance(item, cls):
            return item._value_ in cls._members
        return item in cls._members or item in [
            m._value_ for m in cls._members.values()
        ]

    def __getitem__(cls, name):
        if name in cls._members:
            return cls._members[name]
        raise KeyError(name)

    @property
    def __members__(cls):
        return cls._members


class DynamicStrEnum(str, metaclass=DynamicStrEnumMeta):
    _members = {}

    def __init_subclass__(cls, **kwargs):
        super().__init_subclass__(**kwargs)
        cls._members = {}
        # Parse initial class attributes
        for k, v in list(cls.__dict__.items()):
            if not k.startswith("_") and isinstance(v, str):
                cls._add_member(k, v)

    def __new__(cls, value):
        obj = str.__new__(cls, value)
        obj._value_ = value
        obj._name_ = value
        return obj

    @property
    def value(self):
        return self._value_

    @property
    def name(self):
        return self._name_

    @classmethod
    def __get_pydantic_core_schema__(cls, _source_type, _handler):
        from pydantic_core import core_schema

        return core_schema.str_schema()

    @classmethod
    def _add_member(cls, name, value):
        inst = cls(value)
        inst._name_ = name
        setattr(cls, name, inst)
        cls._members[name] = inst


class PermissionEnum(DynamicStrEnum):
    """Granular permission definitions. Starts empty/generic, clinical trial values are dynamically registered."""

    _members = {}


class RoleEnum(DynamicStrEnum):
    """Canonical system roles. Starts empty/generic, clinical trial values are dynamically registered."""

    _members = {}


# Canonical Role to Permission Matrix Mapping - Dynamically populated
ROLE_PERMISSIONS_MAP: dict[str, set[PermissionEnum]] = {}

# Role aliases normalization mapping - Dynamically populated
_ROLE_ALIASES_MAP: dict[str, str] = {}


def normalize_role_name(role: str) -> str:
    """Normalize arbitrary role string or alias to canonical RoleEnum string value.

    Args:
        role: Raw role string or alias (e.g. 'pi', 'crc', 'auditor').

    Returns:
        Canonical role string value (e.g. 'PrincipalInvestigator').
    """
    cleaned = role.strip()
    if cleaned in ROLE_PERMISSIONS_MAP:
        return cleaned

    lowered = cleaned.lower()
    if lowered in _ROLE_ALIASES_MAP:
        return _ROLE_ALIASES_MAP[lowered]

    return cleaned


def get_permissions_for_role(role: str) -> set[PermissionEnum]:
    """Retrieve the set of granular permissions assigned to a given role.

    Args:
        role: Canonical role name or alias string.

    Returns:
        Set of PermissionEnum members granted to the role.
    """
    canonical_role = normalize_role_name(role)
    return ROLE_PERMISSIONS_MAP.get(canonical_role, set())


def get_permissions_for_roles(roles: list[str]) -> set[PermissionEnum]:
    """Retrieve the aggregated set of permissions across multiple assigned roles.

    Args:
        roles: List of role strings or aliases.

    Returns:
        Aggregated set of PermissionEnum members granted across all input roles.
    """
    aggregated: set[PermissionEnum] = set()
    for r in roles:
        aggregated.update(get_permissions_for_role(r))
    return aggregated


def has_permission(roles: str | list[str], required_permission: PermissionEnum) -> bool:
    """Check if any of the provided roles possess the required permission.

    Args:
        roles: A single role string/alias or list of role strings/aliases.
        required_permission: The target PermissionEnum to verify.

    Returns:
        True if the required permission is granted, False otherwise.
    """
    role_list = [roles] if isinstance(roles, str) else roles

    user_permissions = get_permissions_for_roles(role_list)
    return required_permission in user_permissions


# Dynamic registration API for consumer applications to register clinical configurations
def register_role_and_permissions(
    role_name: str, permissions: set[str], aliases: list[str] = None
):
    """Dynamically register a role, its associated permissions, and aliases on startup."""
    role_key = role_name.upper().replace(" ", "_")
    if role_key not in RoleEnum.__members__:
        RoleEnum._add_member(role_key, role_name)

    for perm in permissions:
        perm_key = perm.upper().replace(":", "_").replace(" ", "_").replace("-", "_")
        if perm_key not in PermissionEnum.__members__:
            PermissionEnum._add_member(perm_key, perm)

    canonical_role = RoleEnum[role_key].value
    if canonical_role not in ROLE_PERMISSIONS_MAP:
        ROLE_PERMISSIONS_MAP[canonical_role] = set()

    for perm in permissions:
        perm_key = perm.upper().replace(":", "_").replace(" ", "_").replace("-", "_")
        ROLE_PERMISSIONS_MAP[canonical_role].add(PermissionEnum[perm_key])

    if aliases:
        for alias in aliases:
            _ROLE_ALIASES_MAP[alias.lower()] = canonical_role
