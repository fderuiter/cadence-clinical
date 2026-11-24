import { getActivePinia } from "pinia";
import { getCurrentInstance } from "vue";
import * as VueRouter from "vue-router";
import type {
  LocationQuery,
  RouteLocationNormalizedLoaded,
  Router,
} from "vue-router";

const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function normalizeSubjectCodeValue(value: unknown): string {
  const raw = Array.isArray(value) ? value[0] : value;
  if (typeof raw !== "string") return "";
  const trimmed = raw.trim();
  if (!trimmed) return "";

  let participantId = "";
  let enrollmentId = "";
  if (trimmed.startsWith("participant:")) {
    participantId =
      trimmed.slice("participant:".length).split("@")[0]?.trim() || "";
  } else if (trimmed.startsWith("enrollment:")) {
    enrollmentId =
      trimmed.slice("enrollment:".length).split("@")[0]?.trim() || "";
  } else if (trimmed.includes(":")) {
    return "";
  } else if (UUID_PATTERN.test(trimmed)) {
    participantId = trimmed;
  }

  if (enrollmentId || participantId) {
    const stateRoot = getActivePinia()?.state.value as
      | {
          studyCanvas?: {
            knownParticipants?: Record<string, { subject_id?: string }>;
            knownParticipantsByEnrollmentId?: Record<
              string,
              { subject_id?: string }
            >;
          };
        }
      | undefined;
    const resolved = enrollmentId
      ? (stateRoot?.studyCanvas?.knownParticipantsByEnrollmentId?.[enrollmentId]
          ?.subject_id ??
        stateRoot?.studyCanvas?.knownParticipants?.[enrollmentId]?.subject_id)
      : (stateRoot?.studyCanvas?.knownParticipants?.[participantId]
          ?.subject_id ??
        stateRoot?.studyCanvas?.knownParticipantsByEnrollmentId?.[participantId]
          ?.subject_id);
    return typeof resolved === "string" ? resolved.trim() : "";
  }

  return trimmed;
}

export interface RouteScopeQuery {
  studyId: string;
  siteId: string;
  subjectId: string;
  visitId: string;
}

function readSingleQueryString(
  query: LocationQuery | Record<string, unknown> | undefined | null,
  keys: readonly string[]
): string {
  if (!query) return "";
  for (const key of keys) {
    const raw = query[key];
    const candidate = Array.isArray(raw) ? raw[0] : raw;
    if (typeof candidate === "string" && candidate.trim().length > 0) {
      return candidate.trim();
    }
  }
  return "";
}

export function resolveRouteAndRouter(): {
  route: RouteLocationNormalizedLoaded | null;
  router: Router | null;
} {
  const instance = getCurrentInstance();
  let route: RouteLocationNormalizedLoaded | null = null;
  let router: Router | null = null;

  if (typeof VueRouter.useRoute === "function") {
    try {
      route = VueRouter.useRoute() ?? null;
    } catch {
      route = null;
    }
  }
  if (!route) {
    route =
      (instance?.proxy?.$route as RouteLocationNormalizedLoaded | undefined) ??
      null;
  }

  if (typeof VueRouter.useRouter === "function") {
    try {
      router = VueRouter.useRouter() ?? null;
    } catch {
      router = null;
    }
  }
  if (!router) {
    router = (instance?.proxy?.$router as Router | undefined) ?? null;
  }

  return { route, router };
}

export function readRouteScopeQuery(
  targetRoute: Pick<RouteLocationNormalizedLoaded, "query"> | null | undefined
): RouteScopeQuery {
  const query = targetRoute?.query;
  return {
    studyId: readSingleQueryString(query, ["studyId", "study_id", "study"]),
    siteId: readSingleQueryString(query, ["siteId", "site_id", "site"]),
    subjectId: normalizeSubjectCodeValue(
      readSingleQueryString(query, [
        "subjectId",
        "subject_id",
        "subject",
        "participant",
      ])
    ),
    visitId: readSingleQueryString(query, ["visitId", "visit_id"]),
  };
}

const ALIAS_KEYS_BY_CANONICAL: Record<string, readonly string[]> = {
  studyId: ["study_id", "study"],
  siteId: ["site_id", "site"],
  subjectId: ["subject_id", "subject", "participant"],
  visitId: ["visit_id"],
};

function shallowEqualQuery(
  left: Record<string, unknown>,
  right: Record<string, unknown>
): boolean {
  const leftKeys = Object.keys(left).filter(
    (k) => left[k] !== undefined && left[k] !== null && left[k] !== ""
  );
  const rightKeys = Object.keys(right).filter(
    (k) => right[k] !== undefined && right[k] !== null && right[k] !== ""
  );
  if (leftKeys.length !== rightKeys.length) return false;
  for (const key of leftKeys) {
    const lVal = Array.isArray(left[key]) ? left[key][0] : left[key];
    const rVal = Array.isArray(right[key]) ? right[key][0] : right[key];
    if (String(lVal ?? "") !== String(rVal ?? "")) {
      return false;
    }
  }
  return true;
}

export async function replaceRouteQueryParams(
  router: Pick<Router, "replace"> | null | undefined,
  route: Pick<RouteLocationNormalizedLoaded, "query"> | null | undefined,
  updates: Record<string, string | null | undefined>
): Promise<boolean> {
  if (!route || typeof router?.replace !== "function") {
    return false;
  }

  const currentQuery = (route.query || {}) as Record<string, unknown>;
  const nextQuery: Record<string, string> = {};

  for (const [key, raw] of Object.entries(currentQuery)) {
    const val = Array.isArray(raw) ? raw[0] : raw;
    if (typeof val === "string" && val.length > 0) {
      if (key === "subjectId") {
        const normalized = normalizeSubjectCodeValue(val);
        if (normalized) {
          nextQuery[key] = normalized;
        }
      } else {
        nextQuery[key] = val;
      }
    }
  }

  for (const [key, raw] of Object.entries(updates)) {
    const aliases = ALIAS_KEYS_BY_CANONICAL[key] || [];
    for (const alias of aliases) {
      delete nextQuery[alias];
    }
    const trimmed =
      key === "subjectId"
        ? normalizeSubjectCodeValue(raw)
        : typeof raw === "string"
          ? raw.trim()
          : "";
    if (trimmed) {
      nextQuery[key] = trimmed;
    } else {
      delete nextQuery[key];
    }
  }

  if (shallowEqualQuery(currentQuery, nextQuery)) {
    return false;
  }

  try {
    await router.replace({ query: nextQuery });
    return true;
  } catch {
    return false;
  }
}

export function shouldSuppressUnsavedNavigationGuard(
  authStore: {
    isAuthenticated?: boolean;
    sessionExpired?: boolean;
    isLoggingOut?: boolean;
  },
  to?: { name?: unknown; path?: string } | null
): boolean {
  if (
    !authStore.isAuthenticated ||
    authStore.sessionExpired ||
    authStore.isLoggingOut
  ) {
    return true;
  }
  return Boolean(
    to &&
    (to.name === "forbidden" ||
      to.path === "/forbidden" ||
      to.name === "login" ||
      to.path === "/login")
  );
}
