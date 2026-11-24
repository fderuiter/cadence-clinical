import { useAuthStore } from "../stores/auth.js";
import {
  normalizeBackendRole,
  PERMISSION_ROLES,
} from "../canvas/authority/roles";

const PUBLIC_API_PATHS = new Set([
  "/health",
  "/api/v1/kernel/health",
  "/api/v1/execution/health",
  "/api/v1/auth/session",
  "/api/v1/telemetry/web-vitals",
]);

const WRITE_HTTP_METHODS = new Set(["POST", "PUT", "PATCH", "DELETE"]);

const AUDITOR_ONLY_ROLES = new Set([
  "auditor",
  "tmf_auditor",
  "inspector",
  "regulatory_inspector",
]);

const SIGNATURE_DISALLOWED_ROLES = new Set([
  ...AUDITOR_ONLY_ROLES,
  "subject",
  "participant",
]);

const PARTICIPANT_MANAGEMENT_ROLES = new Set([
  "sysadmin",
  "principal_investigator",
  "investigator",
  "lead_investigator",
  "crc",
  "trial_coordinator",
]);

const UNBLIND_AUTHORITY_ROLES = new Set([
  "sysadmin",
  "principal_investigator",
  "authorized_er_physician",
]);

const BLINDING_EXEMPT_ROLES = new Set([
  "sysadmin",
  "unblinded_statistician",
  "idmc",
  "pharmacist",
]);

const FORM_WRITE_ROLES = new Set([
  "sysadmin",
  "principal_investigator",
  "investigator",
  "lead_investigator",
  "crc",
  "trial_coordinator",
]);

const FORM_SIGN_ROLES = new Set([
  "sysadmin",
  "principal_investigator",
  "investigator",
  "lead_investigator",
  "authorized_er_physician",
]);

const OBSERVATION_CORRECTION_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "principal_investigator",
  "investigator",
  "lead_investigator",
  "crc",
  "trial_coordinator",
]);

const SDV_AUTHORITY_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
  "monitor",
  "external_monitor",
]);

const QUERY_MANAGE_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "sponsor_mm",
  "cra",
  "monitor",
  "external_monitor",
]);

const QUERY_ANSWER_ROLES = new Set([
  "sysadmin",
  "principal_investigator",
  "investigator",
  "lead_investigator",
  "crc",
  "trial_coordinator",
]);

const DESIGN_AUTHORITY_ROLES = new Set([
  "sysadmin",
  "sponsor_designer",
  "sponsor_dm",
]);

const RULES_AUTHORITY_ROLES = new Set([
  "sysadmin",
  "sponsor_designer",
  "sponsor_dm",
]);

const LOCK_AUTHORITY_ROLES = new Set(["sysadmin", "sponsor_dm"]);

const SITE_ACTIVATION_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
  "monitor",
]);

const CTMS_WRITE_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
  "monitor",
  "finance_officer",
]);

export const DEMO_PERSONA_DEFAULT_SCOPES = Object.freeze({
  super_admin: {
    rawRoles: [
      "Sponsor Admin",
      "Sponsor Designer",
      "Data Manager",
      "CRA",
      "Monitor",
      "Principal Investigator",
      "Site Investigator",
      "CRC",
      "Safety Reviewer",
      "Auditor",
      "TMF Auditor",
    ],
    siteIds: [],
  },
  sponsor_designer: {
    rawRoles: ["Sponsor Designer"],
    siteIds: [],
  },
  data_manager: {
    rawRoles: ["Data Manager"],
    siteIds: [],
  },
  site_crc: {
    rawRoles: ["Site Investigator", "CRC"],
    siteIds: ["SITE-101"],
  },
  site_investigator: {
    rawRoles: ["Principal Investigator", "Site Investigator"],
    siteIds: ["SITE-101"],
  },
  cra_monitor: {
    rawRoles: ["CRA", "Monitor"],
    siteIds: [],
  },
  safety_officer: {
    rawRoles: ["Safety Reviewer"],
    siteIds: [],
  },
  auditor: {
    rawRoles: ["Auditor"],
    siteIds: [],
  },
});

function splitCsvHeader(raw) {
  if (!raw || typeof raw !== "string") return [];
  return raw
    .split(",")
    .map((item) => item.trim())
    .filter((item) => item.length > 0 && item !== "*");
}

function buildNormalizedBackendSet(rawRoles) {
  const set = new Set();
  for (const raw of rawRoles || []) {
    const trimmed = String(raw || "").trim();
    if (!trimmed) continue;
    const normalized = normalizeBackendRole(trimmed);
    if (normalized) {
      set.add(normalized);
    }
    const underscored = trimmed.toLowerCase().replace(/[\s-]+/g, "_");
    if (AUDITOR_ONLY_ROLES.has(underscored)) {
      set.add(underscored);
    }
  }
  return set;
}

function buildCallerObject({
  isAuthenticated,
  persona = null,
  rawRoles = [],
  siteIds = [],
  userId = null,
  isTestBearerFallback = false,
}) {
  const normalizedSites = (siteIds || [])
    .map((site) =>
      String(site || "")
        .trim()
        .toUpperCase()
    )
    .filter((site) => site.length > 0 && site !== "*");
  const backendRoles = buildNormalizedBackendSet(rawRoles);
  if (persona === "super_admin" || isTestBearerFallback) {
    backendRoles.add("sysadmin");
    backendRoles.add("unblinded_statistician");
  }
  return {
    isAuthenticated: Boolean(isAuthenticated),
    persona: persona || null,
    rawRoles: [...rawRoles],
    backendRoles,
    siteIds: normalizedSites,
    userId: userId || (isAuthenticated ? "demo-user" : null),
    isTestBearerFallback: Boolean(isTestBearerFallback),
  };
}

/**
 * Resolves the active demo caller from gateway headers (`X-Demo-Persona`,
 * `X-User-Roles`, `X-User-Id`, `X-Site-Id`), explicit `Authorization` bearer
 * headers in test harnesses, or active Pinia `useAuthStore()` state.
 *
 * @param {Request | null | undefined} request
 */
export function resolveDemoCaller(request) {
  const headers = request?.headers;
  const headerPersona =
    headers?.get("X-Demo-Persona") || headers?.get("x-demo-persona") || "";
  const headerRolesRaw =
    headers?.get("X-User-Roles") || headers?.get("x-user-roles") || "";
  const headerUserId =
    headers?.get("X-User-Id") || headers?.get("x-user-id") || "";
  const headerSiteRaw =
    headers?.get("X-Site-Id") || headers?.get("x-site-id") || null;
  const authHeader =
    headers?.get("Authorization") || headers?.get("authorization") || "";

  if (headerPersona.trim() || headerRolesRaw.trim()) {
    const persona = headerPersona.trim() || null;
    const presetScope = persona ? DEMO_PERSONA_DEFAULT_SCOPES[persona] : null;
    const parsedRoles = splitCsvHeader(headerRolesRaw);
    const rawRoles =
      parsedRoles.length > 0 ? parsedRoles : presetScope?.rawRoles || [];
    const siteIds =
      headerSiteRaw !== null
        ? splitCsvHeader(headerSiteRaw)
        : presetScope?.siteIds || [];
    return buildCallerObject({
      isAuthenticated: true,
      persona,
      rawRoles,
      siteIds,
      userId: headerUserId.trim() || "demo-user",
    });
  }

  let activeAuthStore;
  try {
    activeAuthStore = useAuthStore();
  } catch {
    activeAuthStore = null;
  }

  if (/^Bearer\s+\S+/i.test(authHeader.trim())) {
    if (
      activeAuthStore?.isAuthenticated &&
      activeAuthStore.isDemoMode &&
      !activeAuthStore.accessToken
    ) {
      return buildCallerObject({
        isAuthenticated: true,
        persona: activeAuthStore.currentPersona,
        rawRoles: activeAuthStore.rawRoles || [],
        siteIds:
          activeAuthStore.assignedSiteIds || activeAuthStore.siteIds || [],
        userId:
          activeAuthStore.user?.id ||
          activeAuthStore.user?.username ||
          "demo-user",
      });
    }
    return buildCallerObject({
      isAuthenticated: true,
      persona: "super_admin",
      rawRoles: DEMO_PERSONA_DEFAULT_SCOPES.super_admin.rawRoles,
      siteIds: [],
      userId: "demo-bearer-operator",
      isTestBearerFallback: true,
    });
  }

  if (activeAuthStore?.isAuthenticated) {
    return buildCallerObject({
      isAuthenticated: true,
      persona: activeAuthStore.currentPersona,
      rawRoles: activeAuthStore.rawRoles || [],
      siteIds: activeAuthStore.assignedSiteIds || activeAuthStore.siteIds || [],
      userId:
        activeAuthStore.user?.id ||
        activeAuthStore.user?.username ||
        "demo-user",
    });
  }

  return buildCallerObject({ isAuthenticated: false });
}

export function isPublicDemoEndpoint(pathname) {
  const clean = String(pathname || "").replace(/\/+$/, "") || "/";
  return PUBLIC_API_PATHS.has(clean);
}

export function buildProblemDetails(status, code, title, detail) {
  return {
    type: `https://cadence.clinical/errors/${code}`,
    title,
    status,
    code,
    detail,
  };
}

function hasSuperAdminAccess(caller) {
  return Boolean(
    caller?.isAuthenticated &&
    (caller.persona === "super_admin" ||
      caller.isTestBearerFallback ||
      caller.backendRoles.has("sysadmin"))
  );
}

function hasAnyRole(caller, allowedSet) {
  if (!caller?.isAuthenticated) return false;
  if (hasSuperAdminAccess(caller)) return true;
  for (const role of caller.backendRoles) {
    if (allowedSet.has(role)) return true;
  }
  return false;
}

function holdsMatrixPermission(caller, permission) {
  if (!caller?.isAuthenticated) return false;
  if (hasSuperAdminAccess(caller)) return true;
  const allowed =
    PERMISSION_ROLES[String(permission || "").toLowerCase()] || [];
  for (const role of allowed) {
    if (caller.backendRoles.has(role)) return true;
  }
  return false;
}

/**
 * True when the caller is blinded to treatment arm allocations (`K-AUTH-4`, `PX-20`).
 * Only `super_admin` (demo all-access lens) and unblinded roles (`unblinded_statistician`,
 * `idmc`, `pharmacist`, `sysadmin`) see unblinded treatment arms by default.
 */
export function isCallerBlinded(caller) {
  if (!caller) return false;
  if (!caller.isAuthenticated) return true;
  if (hasSuperAdminAccess(caller)) return false;
  for (const role of caller.backendRoles) {
    if (BLINDING_EXEMPT_ROLES.has(role)) {
      return false;
    }
  }
  return true;
}

/**
 * True when the caller holds study-wide scope (unrestricted across sites).
 */
export function hasStudyWideScope(caller) {
  if (!caller) return true;
  if (hasSuperAdminAccess(caller)) return true;
  return !Array.isArray(caller.siteIds) || caller.siteIds.length === 0;
}

/**
 * True when the caller is permitted to read/mutate clinical entities scoped to `siteCode`.
 * Empty `caller.siteIds` means study-wide / unrestricted site scope.
 */
export function isSitePermitted(caller, siteCode) {
  if (!caller) return true;
  if (hasSuperAdminAccess(caller)) return true;
  if (!Array.isArray(caller.siteIds) || caller.siteIds.length === 0) {
    return true;
  }
  const normalizedTarget = String(siteCode || "")
    .trim()
    .toUpperCase();
  if (!normalizedTarget || normalizedTarget === "*") {
    return true;
  }
  return caller.siteIds.includes(normalizedTarget);
}

/**
 * Enforces `AuditorWriteGuardMiddleware.cs`: any HTTP write verb from an
 * auditor/inspector-only caller fails closed with `403`.
 */
export function evaluateAuditorWriteGuard(caller, method) {
  const upperMethod = String(method || "GET").toUpperCase();
  if (!WRITE_HTTP_METHODS.has(upperMethod)) {
    return null;
  }
  if (hasSuperAdminAccess(caller)) {
    return null;
  }
  const roles = [...(caller?.backendRoles || [])];
  const hasAuditor =
    caller?.persona === "auditor" ||
    roles.some((role) => AUDITOR_ONLY_ROLES.has(role));
  const hasNonAuditor =
    caller?.persona !== "auditor" &&
    roles.some((role) => !AUDITOR_ONLY_ROLES.has(role));

  if (hasAuditor && !hasNonAuditor) {
    return buildProblemDetails(
      403,
      "Auth.AuditorWriteForbidden",
      "Auditor role is strictly read-only",
      "GxP and 21 CFR Part 11 auditor sessions cannot perform state-mutating operations."
    );
  }
  return null;
}

/**
 * Enforces `SignatureTokens.MayObtainSignatureToken`: auditors, inspectors,
 * and unentitled callers cannot obtain a Part 11 `sig_token`.
 */
export function evaluateSignatureTokenEligibility(caller) {
  if (!caller?.isAuthenticated) {
    return buildProblemDetails(
      401,
      "Auth.Unauthenticated",
      "Authentication required",
      "Sign in before requesting an electronic signature token."
    );
  }
  if (hasSuperAdminAccess(caller)) {
    return null;
  }
  const roles = [...(caller.backendRoles || [])];
  if (roles.length === 0) {
    return buildProblemDetails(
      403,
      "Auth.SignatureForbidden",
      "Electronic signature not permitted",
      "No clinical signing role is assigned to the active session."
    );
  }
  const hasEligibleRole =
    caller.persona !== "auditor" &&
    roles.some((role) => !SIGNATURE_DISALLOWED_ROLES.has(role));
  if (!hasEligibleRole) {
    return buildProblemDetails(
      403,
      "Auth.SignatureForbidden",
      "Auditor role cannot obtain signature tokens",
      "Read-only auditor and inspector roles are not eligible to execute 21 CFR Part 11 electronic signatures."
    );
  }
  return null;
}

/**
 * Enforces `SubjectAuthority.CanManageParticipants` and `SubjectAuthority.CanUnblind`
 * along with site-level scope boundaries (`SITE-101` vs `SITE-102`).
 */
export function evaluateSubjectAuthority(
  caller,
  action = "manage",
  siteCode = null
) {
  if (action === "unblind") {
    if (!hasAnyRole(caller, UNBLIND_AUTHORITY_ROLES)) {
      return buildProblemDetails(
        403,
        "Execution.Subject.UnblindForbidden",
        "Emergency unblinding not permitted",
        "Only a Principal Investigator or Authorized Emergency Physician may unblind a participant."
      );
    }
  } else if (!hasAnyRole(caller, PARTICIPANT_MANAGEMENT_ROLES)) {
    return buildProblemDetails(
      403,
      "Execution.Subject.Forbidden",
      "Participant mutation not permitted",
      "Only Site Investigators and Clinical Research Coordinators may register or transition participants."
    );
  }

  if (siteCode && !isSitePermitted(caller, siteCode)) {
    return buildProblemDetails(
      403,
      "Auth.SiteScopeForbidden",
      "Site scope violation",
      `Active persona is not authorized to mutate clinical records at site '${siteCode}'.`
    );
  }
  return null;
}

function resolveEnrollmentSiteCode(store, enrollmentId) {
  if (!store?.state?.subjects || !enrollmentId) return null;
  const cleanId = String(enrollmentId)
    .replace(/^enrollment:/, "")
    .split("@")[0];
  const subj = store.state.subjects.find(
    (s) =>
      s.id === cleanId ||
      String(s.subject_id || "").toUpperCase() === cleanId.toUpperCase()
  );
  return subj?.site_id || null;
}

function resolveFormSiteCode(store, formId) {
  if (!store?.state?.forms || !formId) return null;
  const cleanId = String(formId)
    .replace(/^collection_instance:/, "")
    .split("@")[0];
  const form = store.state.forms.get(cleanId);
  if (!form) return null;
  return resolveEnrollmentSiteCode(store, form.enrollment_id);
}

function resolveScopeSiteCode(store, scopeRef) {
  const ref = String(scopeRef || "").trim();
  if (!ref) return null;
  if (ref.startsWith("study_site:")) {
    const unpinned = ref.split("@")[0];
    if (store?.state?.studySites) {
      for (const links of store.state.studySites.values()) {
        const linked = links.find((item) => item.study_site_ref === unpinned);
        if (linked?.site_code) return linked.site_code;
      }
    }
    const match = store?.state?.sites?.find((s) => ref.includes(s.siteUuid));
    return match?.code || null;
  }
  if (ref.startsWith("enrollment:")) {
    return resolveEnrollmentSiteCode(store, ref);
  }
  if (ref.startsWith("collection_instance:")) {
    return resolveFormSiteCode(store, ref);
  }
  if (/^SITE-\d+/i.test(ref)) {
    return ref.toUpperCase();
  }
  return null;
}

/**
 * Evaluates a `can-i` capability check against the caller's scoped roles and site grants.
 */
export function evaluateCapabilityDecision(
  caller,
  capability,
  resource,
  store
) {
  const cap = String(capability || "kernel.read")
    .trim()
    .toLowerCase();
  const res = String(resource || "study:demo");
  const targetSite = resolveScopeSiteCode(store, res);

  if (!caller?.isAuthenticated) {
    return store.buildDecision(
      capability,
      res,
      false,
      "Authentication required",
      "Auth.Unauthenticated"
    );
  }

  if (targetSite && !isSitePermitted(caller, targetSite)) {
    return store.buildDecision(
      capability,
      res,
      false,
      `Caller is not assigned to ${targetSite}`,
      "Auth.SiteScopeForbidden"
    );
  }

  if (
    cap !== "kernel.read" &&
    !cap.endsWith(".read") &&
    !cap.endsWith(":read")
  ) {
    const auditorBlock = evaluateAuditorWriteGuard(caller, "POST");
    if (auditorBlock) {
      return store.buildDecision(
        capability,
        res,
        false,
        auditorBlock.detail,
        auditorBlock.code
      );
    }
  }

  if (cap === "kernel.read" || cap.endsWith(".read") || cap.endsWith(":read")) {
    return store.buildDecision(capability, res, true);
  }
  if (cap.includes(":")) {
    const allowed = holdsMatrixPermission(caller, cap);
    return store.buildDecision(
      capability,
      res,
      allowed,
      allowed ? undefined : `Role lacks '${capability}' permission`,
      allowed ? undefined : "Auth.Forbidden"
    );
  }
  if (
    cap.includes("verify") ||
    cap.includes("sdv") ||
    cap.includes("retract")
  ) {
    const allowed = hasAnyRole(caller, SDV_AUTHORITY_ROLES);
    return store.buildDecision(
      capability,
      res,
      allowed,
      allowed
        ? undefined
        : "Source data verification requires CRA or Data Manager role",
      allowed ? undefined : "Auth.Forbidden"
    );
  }
  if (cap.includes("correct") || cap.includes("observation")) {
    const allowed = hasAnyRole(caller, OBSERVATION_CORRECTION_ROLES);
    return store.buildDecision(
      capability,
      res,
      allowed,
      allowed
        ? undefined
        : "Observation correction requires Site or Data Manager role",
      allowed ? undefined : "Auth.Forbidden"
    );
  }
  if (cap.includes("site.activate") || cap.includes("greenlight")) {
    const allowed = hasAnyRole(caller, SITE_ACTIVATION_ROLES);
    return store.buildDecision(
      capability,
      res,
      allowed,
      allowed ? undefined : "Site activation requires CRA or Data Manager role",
      allowed ? undefined : "Auth.Forbidden"
    );
  }
  if (cap.includes("sign")) {
    const allowed = hasAnyRole(caller, FORM_SIGN_ROLES);
    return store.buildDecision(
      capability,
      res,
      allowed,
      allowed
        ? undefined
        : "Electronic form sign-off requires Investigator authority",
      allowed ? undefined : "Auth.Forbidden"
    );
  }
  if (cap.includes("unblind")) {
    const allowed = hasAnyRole(caller, UNBLIND_AUTHORITY_ROLES);
    return store.buildDecision(
      capability,
      res,
      allowed,
      allowed
        ? undefined
        : "Emergency unblinding requires Principal Investigator authority",
      allowed ? undefined : "Auth.Forbidden"
    );
  }
  if (
    cap.includes("subject") ||
    cap.includes("participant") ||
    cap.includes("enroll")
  ) {
    const allowed = hasAnyRole(caller, PARTICIPANT_MANAGEMENT_ROLES);
    return store.buildDecision(
      capability,
      res,
      allowed,
      allowed
        ? undefined
        : "Participant management requires Site Investigator or CRC role",
      allowed ? undefined : "Auth.Forbidden"
    );
  }
  if (cap.includes("lock")) {
    const allowed = hasAnyRole(caller, LOCK_AUTHORITY_ROLES);
    return store.buildDecision(
      capability,
      res,
      allowed,
      allowed ? undefined : "Data lock requires Data Manager authority",
      allowed ? undefined : "Auth.Forbidden"
    );
  }
  if (cap.includes("query")) {
    const isAnswer = cap.endsWith(".answer") || cap === "query.answer";
    const allowed = hasAnyRole(
      caller,
      isAnswer ? QUERY_ANSWER_ROLES : QUERY_MANAGE_ROLES
    );
    return store.buildDecision(
      capability,
      res,
      allowed,
      allowed
        ? undefined
        : isAnswer
          ? "Query response requires Site Investigator or CRC role"
          : "Query management requires CRA or Data Manager role",
      allowed ? undefined : "Auth.Forbidden"
    );
  }
  if (
    cap.includes("protocol") ||
    cap.includes("study") ||
    cap.includes("design")
  ) {
    const allowed = hasAnyRole(caller, DESIGN_AUTHORITY_ROLES);
    return store.buildDecision(
      capability,
      res,
      allowed,
      allowed
        ? undefined
        : "Protocol design requires Sponsor Designer or Data Manager role",
      allowed ? undefined : "Auth.Forbidden"
    );
  }

  return store.buildDecision(capability, res, hasSuperAdminAccess(caller));
}

/**
 * Evaluates `/api/v1/kernel/nodes/:nodeRef/commands` for the active caller.
 */
export function evaluateNodeCommands(caller, nodeRef, store) {
  const commands = [
    "collection.correct",
    "collection.verify",
    "execution.site.activate",
  ];
  return commands.map((command) => ({
    command,
    decision: evaluateCapabilityDecision(caller, command, nodeRef, store),
  }));
}

/**
 * Evaluates authentication, `AuditorWriteGuardMiddleware`, `SignatureTokens`,
 * `SubjectAuthority`, and `PermissionMatrix` rules for any `/api/*` request.
 * Returns `{ status, problem }` when denied, or `null` when permitted.
 */
export async function evaluateDemoRequestPolicy(request, store) {
  const url = new URL(request.url, "http://localhost");
  const pathname = url.pathname;
  const method = String(request.method || "GET").toUpperCase();

  if (isPublicDemoEndpoint(pathname)) {
    return null;
  }

  const caller = resolveDemoCaller(request);
  if (!caller.isAuthenticated) {
    return {
      status: 401,
      problem: buildProblemDetails(
        401,
        "Auth.Unauthenticated",
        "Authentication required",
        "Sign in to access Cadence Clinical APIs."
      ),
    };
  }

  if (
    method === "POST" &&
    pathname.endsWith("/api/v1/auth/signature-verification")
  ) {
    const sigDenied = evaluateSignatureTokenEligibility(caller);
    return sigDenied ? { status: sigDenied.status, problem: sigDenied } : null;
  }

  const auditorDenied = evaluateAuditorWriteGuard(caller, method);
  if (auditorDenied) {
    return { status: 403, problem: auditorDenied };
  }

  if (!WRITE_HTTP_METHODS.has(method)) {
    return null;
  }

  if (hasSuperAdminAccess(caller)) {
    return null;
  }

  let body = null;
  const readClonedBody = async () => {
    if (body !== null) return body;
    try {
      body = await request.clone().json();
    } catch {
      body = {};
    }
    return body;
  };

  // 1. Participant registration, state transitions, consent, and emergency unblinding
  if (
    method === "POST" &&
    /\/api\/v1\/execution\/subjects\/?$/.test(pathname)
  ) {
    const payload = await readClonedBody();
    const denied = evaluateSubjectAuthority(
      caller,
      "manage",
      payload?.site_id || "SITE-101"
    );
    return denied ? { status: 403, problem: denied } : null;
  }

  const subjectStateMatch = pathname.match(
    /\/api\/v1\/execution\/subjects\/([^/]+)\/state$/
  );
  if (subjectStateMatch) {
    const subjectId = decodeURIComponent(subjectStateMatch[1]);
    const payload = await readClonedBody();
    const targetState = String(
      payload?.status || payload?.state || payload?.target || ""
    ).toUpperCase();
    const siteCode = resolveEnrollmentSiteCode(store, subjectId);
    const denied = evaluateSubjectAuthority(
      caller,
      targetState === "UNBLINDED" ? "unblind" : "manage",
      siteCode
    );
    return denied ? { status: 403, problem: denied } : null;
  }

  const rtsmUnblindMatch = pathname.match(
    /\/api\/v1\/execution\/rtsm\/unblind\/([^/]+)$/
  );
  if (rtsmUnblindMatch) {
    const subjectId = decodeURIComponent(rtsmUnblindMatch[1]);
    const siteCode = resolveEnrollmentSiteCode(store, subjectId);
    const denied = evaluateSubjectAuthority(caller, "unblind", siteCode);
    return denied ? { status: 403, problem: denied } : null;
  }

  if (
    /\/api\/v1\/econsent\/consents/.test(pathname) ||
    /\/api\/v1\/execution\/amendments\/(bulk-)?reconsent$/.test(pathname)
  ) {
    const payload = await readClonedBody();
    const siteCode =
      payload?.site_id ||
      resolveEnrollmentSiteCode(
        store,
        payload?.subject_id || payload?.enrollment_id
      );
    const denied = evaluateSubjectAuthority(caller, "manage", siteCode);
    return denied ? { status: 403, problem: denied } : null;
  }

  // 2. eCRF form open, values save, and Part 11 form sign-off
  if (method === "POST" && /\/api\/v1\/execution\/forms\/?$/.test(pathname)) {
    const payload = await readClonedBody();
    const siteCode = resolveEnrollmentSiteCode(store, payload?.enrollment_id);
    if (!hasAnyRole(caller, FORM_WRITE_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Execution.Form.WriteForbidden",
          "eCRF data entry not permitted",
          "Only Site Investigators and Clinical Research Coordinators may open or edit case report forms."
        ),
      };
    }
    if (siteCode && !isSitePermitted(caller, siteCode)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Auth.SiteScopeForbidden",
          "Site scope violation",
          `Active persona is not authorized to open eCRF forms for site '${siteCode}'.`
        ),
      };
    }
    return null;
  }

  const formValuesMatch = pathname.match(
    /\/api\/v1\/execution\/forms\/([^/]+)\/values$/
  );
  if (formValuesMatch) {
    const formId = decodeURIComponent(formValuesMatch[1]);
    const siteCode = resolveFormSiteCode(store, formId);
    if (!hasAnyRole(caller, FORM_WRITE_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Execution.Form.WriteForbidden",
          "eCRF data entry not permitted",
          "Only Site Investigators and Clinical Research Coordinators may record or update eCRF values."
        ),
      };
    }
    if (siteCode && !isSitePermitted(caller, siteCode)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Auth.SiteScopeForbidden",
          "Site scope violation",
          `Active persona is not authorized to edit eCRF values for site '${siteCode}'.`
        ),
      };
    }
    return null;
  }

  const formSignMatch = pathname.match(
    /\/api\/v1\/execution\/forms\/([^/]+)\/signatures$/
  );
  if (formSignMatch) {
    const formId = decodeURIComponent(formSignMatch[1]);
    const siteCode = resolveFormSiteCode(store, formId);
    if (!hasAnyRole(caller, FORM_SIGN_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Execution.Form.SignForbidden",
          "Investigator signature authority required",
          "Only an Investigator may apply a 21 CFR Part 11 sign-off to an eCRF form."
        ),
      };
    }
    if (siteCode && !isSitePermitted(caller, siteCode)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Auth.SiteScopeForbidden",
          "Site scope violation",
          `Active persona is not authorized to sign eCRF forms for site '${siteCode}'.`
        ),
      };
    }
    return null;
  }

  // 3. Discrepancy queries: raise, SLA escalate, answer, reopen/close/cancel/reassign
  if (
    method === "POST" &&
    /\/api\/v1\/execution\/forms\/queries\/escalate\/?$/.test(pathname)
  ) {
    if (!hasAnyRole(caller, QUERY_MANAGE_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Execution.Query.EscalateForbidden",
          "Query SLA escalation not permitted",
          "Only Clinical Monitors (CRA) and Data Managers may run manual 14-day query SLA escalations."
        ),
      };
    }
    return null;
  }

  const queryRaiseMatch = pathname.match(
    /\/api\/v1\/execution\/forms\/([^/]+)\/queries$/
  );
  if (method === "POST" && queryRaiseMatch) {
    if (!hasAnyRole(caller, QUERY_MANAGE_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Execution.Query.RaiseForbidden",
          "Query creation not permitted",
          "Only Clinical Monitors (CRA) and Data Managers may raise discrepancy queries."
        ),
      };
    }
    return null;
  }

  const queryActionMatch = pathname.match(
    /\/api\/v1\/execution\/forms\/([^/]+)\/queries\/([^/]+)\/([^/]+)$/
  );
  if (method === "POST" && queryActionMatch) {
    const formId = decodeURIComponent(queryActionMatch[1]);
    const action = decodeURIComponent(queryActionMatch[3]).toLowerCase();
    if (action === "answer") {
      const siteCode = resolveFormSiteCode(store, formId);
      if (!hasAnyRole(caller, QUERY_ANSWER_ROLES)) {
        return {
          status: 403,
          problem: buildProblemDetails(
            403,
            "Execution.Query.AnswerForbidden",
            "Query response not permitted",
            "Only Site Investigators and Clinical Research Coordinators may answer discrepancy queries."
          ),
        };
      }
      if (siteCode && !isSitePermitted(caller, siteCode)) {
        return {
          status: 403,
          problem: buildProblemDetails(
            403,
            "Auth.SiteScopeForbidden",
            "Site scope violation",
            `Active persona is not authorized to answer queries at site '${siteCode}'.`
          ),
        };
      }
      return null;
    }
    if (!hasAnyRole(caller, QUERY_MANAGE_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Execution.Query.TransitionForbidden",
          "Query transition not permitted",
          `Only Clinical Monitors (CRA) and Data Managers may ${action} discrepancy queries.`
        ),
      };
    }
    return null;
  }

  // 4. Observation corrections & SDV verifications / retractions
  if (/\/api\/v1\/kernel\/observations\/[^/]+\/corrections$/.test(pathname)) {
    if (!hasAnyRole(caller, OBSERVATION_CORRECTION_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Kernel.Observation.CorrectionForbidden",
          "Observation correction not permitted",
          "Only Site Investigators, CRCs, or Data Managers may record observation corrections."
        ),
      };
    }
    return null;
  }

  if (
    /\/api\/v1\/kernel\/observations\/[^/]+\/verifications$/.test(pathname) ||
    /\/api\/v1\/kernel\/assertions\/[^/]+\/retraction$/.test(pathname)
  ) {
    if (!hasAnyRole(caller, SDV_AUTHORITY_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Kernel.Sdv.Forbidden",
          "Source Data Verification not permitted",
          "Only Clinical Monitors (CRA) and Data Managers hold 'sdv:update' authority."
        ),
      };
    }
    return null;
  }

  // 5. Data locks
  if (
    /\/api\/v1\/kernel\/locks/.test(pathname) ||
    /\/api\/v1\/execution\/locks/.test(pathname)
  ) {
    if (!hasAnyRole(caller, LOCK_AUTHORITY_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Kernel.Lock.Forbidden",
          "Data lock authority required",
          "Only Data Managers may apply or release clinical data locks."
        ),
      };
    }
    return null;
  }

  // 6. Site activation & CTMS mutations
  if (/\/api\/v1\/execution\/sites\/[^/]+\/activate$/.test(pathname)) {
    if (!hasAnyRole(caller, SITE_ACTIVATION_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Execution.Site.ActivateForbidden",
          "Site activation not permitted",
          "Only Clinical Monitors (CRA) and Data Managers may certify greenlight and activate sites."
        ),
      };
    }
    return null;
  }

  if (/\/api\/v1\/ctms\//.test(pathname)) {
    const isDoaSign =
      /\/api\/v1\/ctms\/doa\/sign(-off)?$/.test(pathname) ||
      /\/api\/v1\/ctms\/doa\/[^/]+\/sign(-off)?$/.test(pathname);
    const allowed = isDoaSign
      ? holdsMatrixPermission(caller, "ctms_doa:sign")
      : hasAnyRole(caller, CTMS_WRITE_ROLES);
    if (!allowed) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Ctms.WriteForbidden",
          "CTMS write authority required",
          "Active persona does not hold clinical operations write permissions."
        ),
      };
    }
    return null;
  }

  // 7. Validation rules, protocol design, study creation, and amendments
  if (/\/api\/v1\/studies\/[^/]+\/rules/.test(pathname)) {
    if (!hasAnyRole(caller, RULES_AUTHORITY_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Design.Rules.Forbidden",
          "Validation rule authoring not permitted",
          "Only Sponsor Designers and Data Managers may modify study validation rules."
        ),
      };
    }
    return null;
  }

  if (
    /\/api\/v1\/kernel\/(studies|organizations|sites|proposals|simulation|protocols)/.test(
      pathname
    ) ||
    /\/api\/v1\/designer\//.test(pathname) ||
    /\/api\/v1\/studies\/[^/]+\/versions\//.test(pathname) ||
    /\/api\/v1\/execution\/amendments\/publish$/.test(pathname)
  ) {
    if (!hasAnyRole(caller, DESIGN_AUTHORITY_ROLES)) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Kernel.Design.Forbidden",
          "Study design authority required",
          "Only Sponsor Designers and Data Managers may modify study definitions or protocol versions."
        ),
      };
    }
    return null;
  }

  // 8. RTSM kit seeding / allocation / dispensing
  if (/\/api\/v1\/execution\/(rtsm|randomization)\//.test(pathname)) {
    const isSiteDispense =
      /\/api\/v1\/execution\/rtsm\/(dispense|allocate|cgt)\//.test(pathname);
    const allowed = isSiteDispense
      ? hasAnyRole(caller, PARTICIPANT_MANAGEMENT_ROLES) ||
        caller.backendRoles.has("pharmacist")
      : hasAnyRole(caller, DESIGN_AUTHORITY_ROLES) ||
        caller.backendRoles.has("pharmacist");
    if (!allowed) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Execution.Rtsm.Forbidden",
          "RTSM operation not permitted",
          "Active persona is not authorized to perform this supply or randomization action."
        ),
      };
    }
    return null;
  }

  // 9. eTMF document staging, QC transitions, and sign-off
  if (/\/api\/v1\/etmf\//.test(pathname)) {
    const isQcReview = /\/api\/v1\/etmf\/intelligence\/qc-review\//.test(
      pathname
    );
    const isSignOff = /\/api\/v1\/etmf\/documents\/[^/]+\/sign-off$/.test(
      pathname
    );
    const allowed = isQcReview
      ? holdsMatrixPermission(caller, "etmf_document:transition_clinical_qc")
      : isSignOff
        ? holdsMatrixPermission(caller, "etmf_document:sign") ||
          hasAnyRole(caller, FORM_SIGN_ROLES)
        : holdsMatrixPermission(caller, "etmf_document:create");
    if (!allowed) {
      return {
        status: 403,
        problem: buildProblemDetails(
          403,
          "Etmf.WriteForbidden",
          "eTMF write authority required",
          "Active persona is not authorized to stage, review, or sign eTMF artifacts."
        ),
      };
    }
    return null;
  }

  // 10. Knowledge authoring (super_admin only; super_admin already returned null above)
  if (/\/api\/v1\/knowledge\/(categories|articles)/.test(pathname)) {
    return {
      status: 403,
      problem: buildProblemDetails(
        403,
        "Knowledge.WriteForbidden",
        "Knowledge authoring requires administrator role",
        "Only Sponsor Administrators may create or modify knowledge base articles."
      ),
    };
  }

  return null;
}
