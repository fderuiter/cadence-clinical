import { PERMISSION_ROLES } from "../generated/permissions";
import { BACKEND_ROLE_ALIASES } from "../generated/roleAliases";

export { BACKEND_ROLE_ALIASES, PERMISSION_ROLES };

export interface RoleHolder {
  hasRole: (role: string) => boolean;
}

/** The realm roles exactly as the gateway signs them to downstream .NET services. */
export interface RawRoleHolder {
  rawRoles?: readonly string[] | null;
}

export type AuthorityAuth = Partial<RoleHolder> &
  RawRoleHolder & {
    isAuthenticated?: boolean;
  };

/**
 * Web-only workspace/persona role aliases from `src/stores/auth.js` (lines 72-127, 192-194)
 * that do not exist in `Cadence.Security.Rbac.RoleNormalization.ROLE_ALIASES`:
 * - `super_admin` is a web workspace persona requiring `sponsor_admin` (`auth.js` normalizes
 *   `super_admin` -> `sponsor_admin`, which `RoleNormalization.cs` maps to `sponsor_dm`, never `sysadmin`).
 * - `tmf_auditor` / `"tmf auditor"` is a web auditor persona alias (`auth.js` maps `tmf_auditor` -> `auditor`).
 */
export const WEB_PERSONA_ROLE_ALIASES: Readonly<Record<string, string>> =
  Object.freeze({
    super_admin: BACKEND_ROLE_ALIASES.sponsor_admin,
    "super admin": BACKEND_ROLE_ALIASES.sponsor_admin,
    "super-admin": BACKEND_ROLE_ALIASES.sponsor_admin,
    tmf_auditor: BACKEND_ROLE_ALIASES.auditor,
    "tmf auditor": BACKEND_ROLE_ALIASES.auditor,
    "tmf-auditor": BACKEND_ROLE_ALIASES.auditor,
  });

/**
 * Normalizes a raw realm or web persona role string using generated `RoleNormalization.ROLE_ALIASES`
 * plus reconciled web-only persona aliases from `stores/auth.js`.
 */
export function normalizeBackendRole(role: string): string {
  const norm = (role || "").trim().toLowerCase();
  if (!norm) return "";
  return BACKEND_ROLE_ALIASES[norm] ?? WEB_PERSONA_ROLE_ALIASES[norm] ?? norm;
}

export function canonicalBackendRoles(
  auth: AuthorityAuth | null | undefined
): string[] {
  if (!auth) return [];
  const raw = auth.rawRoles ?? [];
  if (raw.length > 0) {
    return raw.map((r) => normalizeBackendRole(r)).filter(Boolean);
  }
  if (typeof auth.hasRole === "function") {
    const inferred: string[] = [];
    if (auth.hasRole("sponsor_admin") || auth.hasRole("data_manager")) {
      inferred.push("sponsor_dm");
    }
    return inferred;
  }
  return [];
}

export function holdsBackendRole(
  auth: AuthorityAuth | null | undefined,
  allowed: ReadonlySet<string> | readonly string[]
): boolean {
  const set = allowed instanceof Set ? allowed : new Set(allowed);
  return canonicalBackendRoles(auth).some((role) => set.has(role));
}

const PERMISSION_SET_CACHE = new Map<string, ReadonlySet<string>>();

export function rolesForPermission(permission: string): ReadonlySet<string> {
  const key = permission.toLowerCase();
  let cached = PERMISSION_SET_CACHE.get(key);
  if (!cached) {
    cached = new Set(PERMISSION_ROLES[key] ?? []);
    PERMISSION_SET_CACHE.set(key, cached);
  }
  return cached;
}

export function holdsBackendPermission(
  auth: AuthorityAuth | null | undefined,
  permission: string
): boolean {
  return holdsBackendRole(auth, rolesForPermission(permission));
}

/**
 * Unauthenticated Pinia store instances in isolated component mounts default to permissive presentation
 * when no `rawRoles` are supplied (`auth.isAuthenticated === false`), while explicit `{ rawRoles }`
 * callers always evaluate role checks strictly.
 */
export function isUnauthenticatedFallback(
  auth: AuthorityAuth | null | undefined
): boolean {
  return (
    Boolean(auth) &&
    auth?.isAuthenticated === false &&
    (auth.rawRoles?.length ?? 0) === 0
  );
}
