/**
 * Caller study and site scope helpers for Keycloak OIDC token claims and 403 FORBIDDEN_SCOPE responses.
 *
 * Backend scope guards (ExecutionScopeGuard.cs and CtmsScopeGuard.cs) return RFC 7807 ProblemDetails with
 * HTTP 403 and `code: "FORBIDDEN_SCOPE"` whenever a caller queries or mutates a study or site outside their
 * assigned token scope. Surfacing clear scope-assignment guidance (UX-9) avoids misleading users into thinking
 * eTMF, CTMS, or RTSM services failed.
 */

const SCOPE_DETAIL_PATTERN =
  /not authorized for (?:study|site|unscoped)|not assigned to (?:study|site)|no assigned (?:study|site|studies|sites)|caller scope/i;

export function parseClaimStringList(raw: unknown): string[] {
  if (raw == null) return [];
  const items: string[] = [];
  if (Array.isArray(raw)) {
    for (const entry of raw) {
      if (typeof entry === "string") {
        items.push(entry);
      }
    }
  } else if (typeof raw === "string") {
    const trimmed = raw.trim();
    if (!trimmed) return [];
    if (trimmed.startsWith("[")) {
      try {
        const parsed = JSON.parse(trimmed);
        if (Array.isArray(parsed)) {
          return parseClaimStringList(parsed);
        }
      } catch {
        // Treat as delimited string below
      }
    }
    items.push(...trimmed.split(/[\s,]+/));
  }

  const seen = new Set<string>();
  const result: string[] = [];
  for (const item of items) {
    const clean = item.trim();
    if (!clean || seen.has(clean)) continue;
    seen.add(clean);
    result.push(clean);
  }
  return result;
}

export function extractAssignedStudies(claims: Record<string, unknown> = {}): {
  assignedStudies: string[];
  hasAllStudiesScope: boolean;
} {
  const raw = parseClaimStringList(
    claims.study_ids ?? claims.assigned_studies ?? claims.study_id
  );
  const hasAllStudiesScope = raw.includes("*");
  const assignedStudies = raw.filter((study) => study !== "*");
  return { assignedStudies, hasAllStudiesScope };
}

export function extractAssignedSites(
  claims: Record<string, unknown> = {}
): string[] {
  const combined = [
    ...parseClaimStringList(claims.site_id),
    ...parseClaimStringList(claims.site_ids ?? claims.assigned_sites),
  ];
  const seen = new Set<string>();
  const result: string[] = [];
  for (const site of combined) {
    if (seen.has(site)) continue;
    seen.add(site);
    result.push(site);
  }
  return result;
}

export function isForbiddenScopeError(err: unknown): boolean {
  if (!err || typeof err !== "object") return false;
  const record = err as Record<string, unknown>;
  if (record.status !== 403) return false;

  const data =
    record.data && typeof record.data === "object"
      ? (record.data as Record<string, unknown>)
      : null;
  if (data?.code === "FORBIDDEN_SCOPE") {
    return true;
  }

  const detail = String(data?.detail ?? record.message ?? "");
  return SCOPE_DETAIL_PATTERN.test(detail);
}

export interface CallerScopeGuidanceContext {
  studyId?: string;
  siteId?: string;
  assignedStudies?: readonly string[];
  assignedSites?: readonly string[];
  hasAllStudiesScope?: boolean;
}

export function formatScopeForbiddenGuidance(
  err: unknown,
  context: CallerScopeGuidanceContext = {}
): string {
  const record =
    err && typeof err === "object" ? (err as Record<string, unknown>) : null;
  const data =
    record?.data && typeof record.data === "object"
      ? (record.data as Record<string, unknown>)
      : null;
  const serverDetail =
    typeof data?.detail === "string" && data.detail.trim()
      ? data.detail.trim()
      : "";

  const targetParts: string[] = [];
  if (context.studyId) targetParts.push(`study ${context.studyId}`);
  if (context.siteId) targetParts.push(`site ${context.siteId}`);
  const targetText =
    targetParts.length > 0 ? targetParts.join(" / ") : "this study or site";

  const scopeParts: string[] = [];
  if (context.hasAllStudiesScope) {
    scopeParts.push("all studies");
  } else if (context.assignedStudies?.length) {
    scopeParts.push(`studies: ${context.assignedStudies.join(", ")}`);
  }
  if (context.assignedSites?.length) {
    scopeParts.push(`sites: ${context.assignedSites.join(", ")}`);
  }

  const assignedSummary =
    scopeParts.length > 0
      ? `Your account is currently assigned to ${scopeParts.join("; ")}.`
      : "Your account does not currently hold study or site assignment claims for this scope.";

  const prefix = serverDetail
    ? `Scope access restricted (${serverDetail})`
    : `Your account isn't assigned to ${targetText}.`;

  return `${prefix} ${assignedSummary} Select an assigned study and site or ask a study administrator to update your study/site assignment.`;
}
