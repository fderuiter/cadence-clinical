/**
 * Shared caller study and site scope helpers for Keycloak OIDC token claims, JWT payloads,
 * and 403 FORBIDDEN_SCOPE responses across apps/web and apps/subject-portal.
 */

const SCOPE_DETAIL_PATTERN =
  /not authorized for (?:study|site|unscoped)|not assigned to (?:study|site)|no assigned (?:study|site|studies|sites)|caller scope/i;

export function parseClaimStringList(raw) {
  if (raw == null) return [];
  const items = [];
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

  const seen = new Set();
  const result = [];
  for (const item of items) {
    const clean = item.trim();
    if (!clean || seen.has(clean)) continue;
    seen.add(clean);
    result.push(clean);
  }
  return result;
}

export function decodeJwtPayloadClaims(token) {
  if (typeof token !== "string") return null;
  const trimmed = token.trim();
  if (!trimmed) return null;
  const parts = trimmed.split(".");
  if (parts.length < 2 || !parts[1]) return null;
  try {
    const base64 = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, "=");
    let jsonText = "";
    if (typeof atob === "function") {
      const binary = atob(padded);
      const bytes = Uint8Array.from(binary, (ch) => ch.charCodeAt(0));
      jsonText = new TextDecoder("utf-8").decode(bytes);
    } else if (typeof Buffer !== "undefined") {
      jsonText = Buffer.from(padded, "base64").toString("utf8");
    } else {
      return null;
    }
    const parsed = JSON.parse(jsonText);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? parsed
      : null;
  } catch {
    return null;
  }
}

function resolveClaimsRecord(claimsOrToken) {
  if (typeof claimsOrToken === "string") {
    return decodeJwtPayloadClaims(claimsOrToken) ?? {};
  }
  if (claimsOrToken && typeof claimsOrToken === "object") {
    return claimsOrToken;
  }
  return {};
}

export function extractAssignedStudies(claimsOrToken = {}) {
  const claims = resolveClaimsRecord(claimsOrToken);
  const raw = parseClaimStringList(
    claims.study_ids ??
      claims.assigned_studies ??
      claims.assignedStudies ??
      claims.studies ??
      claims.study_id ??
      claims.studyId
  );
  const hasAllStudiesScope = raw.includes("*");
  const assignedStudies = raw.filter((study) => study !== "*");
  return { assignedStudies, hasAllStudiesScope };
}

export function extractAssignedSites(claimsOrToken = {}) {
  const claims = resolveClaimsRecord(claimsOrToken);
  const combined = [
    ...parseClaimStringList(claims.site_id ?? claims.siteId),
    ...parseClaimStringList(
      claims.site_ids ??
        claims.assigned_sites ??
        claims.assignedSites ??
        claims.sites
    ),
  ];
  const seen = new Set();
  const result = [];
  for (const site of combined) {
    if (seen.has(site)) continue;
    seen.add(site);
    result.push(site);
  }
  return result;
}

export function extractPrimaryStudyId(claimsOrToken = {}) {
  const { assignedStudies } = extractAssignedStudies(claimsOrToken);
  return assignedStudies[0] ?? null;
}

export function extractPrimarySiteId(claimsOrToken = {}) {
  const sites = extractAssignedSites(claimsOrToken);
  return sites[0] ?? null;
}

export function isForbiddenScopeError(err) {
  if (!err || typeof err !== "object") return false;
  const record = err;
  if (record.status !== 403) return false;

  const data =
    record.data && typeof record.data === "object" ? record.data : null;
  if (data?.code === "FORBIDDEN_SCOPE") {
    return true;
  }

  const detail = String(data?.detail ?? record.message ?? "");
  return SCOPE_DETAIL_PATTERN.test(detail);
}

export function formatScopeForbiddenGuidance(err, context = {}) {
  const record = err && typeof err === "object" ? err : null;
  const data =
    record?.data && typeof record.data === "object" ? record.data : null;
  const serverDetail =
    typeof data?.detail === "string" && data.detail.trim()
      ? data.detail.trim()
      : "";

  const targetParts = [];
  if (context.studyId) targetParts.push(`study ${context.studyId}`);
  if (context.siteId) targetParts.push(`site ${context.siteId}`);
  const targetText =
    targetParts.length > 0 ? targetParts.join(" / ") : "this study or site";

  const scopeParts = [];
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
