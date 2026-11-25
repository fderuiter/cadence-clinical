import { getActivePinia } from "pinia";
import { resolveActiveWorkspaceScope } from "../canvas/workspaceScopeResolver";
import { ROLE_ALIASES } from "../stores/auth";

export const STAFF_HUB_ROLES = Object.freeze([
  "sponsor_admin",
  "sponsor_designer",
  "data_manager",
  "unblinded_statistician",
  "pharmacist",
  "safety_reviewer",
  "safety_officer",
  "safety_physician",
  "sponsor_mm",
  "cra",
  "monitor",
  "principal_investigator",
  "site_investigator",
  "crc",
  "trial_coordinator",
  "auditor",
  "tmf_auditor",
]);

export function hasRequiredRole(userRoles, requiredRoles) {
  if (!requiredRoles || requiredRoles.length === 0) return true;
  return (userRoles || []).some((uRole) => {
    if (requiredRoles.includes(uRole)) return true;
    const aliases = ROLE_ALIASES[uRole] || [];
    return aliases.some((alias) => requiredRoles.includes(alias));
  });
}

export function redirectToCanvasSection(stage, section) {
  return (to) => {
    const canvasState = getActivePinia()?.state.value?.studyCanvas;
    const scope = resolveActiveWorkspaceScope(to, canvasState);
    const rawCode =
      (typeof to.query?.studyCode === "string" && to.query.studyCode.trim()) ||
      (typeof to.query?.studyId === "string" && to.query.studyId.trim()) ||
      scope.studyCode ||
      "";
    const rawSite =
      (typeof to.query?.site === "string" && to.query.site.trim()) ||
      (typeof to.query?.siteId === "string" && to.query.siteId.trim()) ||
      scope.siteId ||
      "";
    const nextQuery = { ...to.query, stage, section };
    if (
      !nextQuery.participant &&
      typeof nextQuery.subjectId === "string" &&
      nextQuery.subjectId.trim()
    ) {
      nextQuery.participant = nextQuery.subjectId.trim();
    }
    delete nextQuery.studyCode;
    delete nextQuery.studyId;
    delete nextQuery.siteId;
    delete nextQuery.subjectId;
    if (rawSite) {
      nextQuery.site = rawSite;
    }
    return rawCode
      ? { name: "study", params: { studyCode: rawCode }, query: nextQuery }
      : { name: "study", query: nextQuery };
  };
}

export function createCecAdjudicationRoute({ order, path, name }) {
  return {
    order,
    availability: "demo",
    contextLabel: "Endpoint adjudication",
    sopEscalation: {
      prefixes: [path],
      category: "SAFETY_ADVERSE_EVENT",
    },
    catalog: {
      title: "CEC adjudication",
      family: "Vigil",
      minimumTier: "Complete",
      isBaselineCapability: false,
      roadmapStage: "Pre-release Vigil module roadmap",
      summary:
        "Blinded clinical events committee dossier assembly and independent endpoint adjudication.",
    },
    route: {
      path,
      name,
      component: () => import("../views/CecAdjudicationView.vue"),
      meta: {
        requiresAuth: true,
        requiresRole: [
          "site_investigator",
          "crc",
          "data_manager",
          "sponsor_admin",
          "cra",
          "monitor",
          "sponsor_designer",
          "auditor",
          "tmf_auditor",
        ],
      },
    },
  };
}
