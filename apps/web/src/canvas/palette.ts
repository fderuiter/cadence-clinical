import type { LocationQuery, Router } from "vue-router";
import type { SubjectSummary } from "../api/executionApi";
import { formatNodeRef, parseNodeRef, tryNodeRef } from "./nodeRef";
import { isScreenAvailable } from "../router/availability.js";
import { hasRequiredRole } from "../router/index.js";

export type PaletteWorkspaceAction =
  | { kind: "open-help" }
  | { kind: "open-checklist" }
  | { kind: "start-tour" }
  | { kind: "switch-persona"; personaKey: string };

/** Where a ⌘K palette choice takes the unified Clinical Canvas shell. */
export interface PaletteTarget {
  studyCode?: string;
  stage?: string;
  section?: string;
  participant?: SubjectSummary;
  /** A question for the inspector's Assist pane (ADR-2635). */
  ask?: string;
  /** Authorized workflow or orientation route path. */
  routePath?: string;
  /** Workspace shell action (help drawer, setup checklist, guided tour, or persona view). */
  action?: PaletteWorkspaceAction;
}

export interface WorkspacePaletteOption {
  key: string;
  group: string;
  label: string;
  detail?: string;
  target: PaletteTarget;
}

const DATA_COLLECTION_ROLES = [
  "site_investigator",
  "crc",
  "data_manager",
  "sponsor_admin",
  "cra",
  "monitor",
];

const SITE_READINESS_SPONSOR_ROLES = [
  "cra",
  "monitor",
  "data_manager",
  "sponsor_admin",
];
const SITE_READINESS_SITE_ROLES = ["site_investigator", "crc"];

export function resolveSiteReadinessPath(
  normalizedRoles: string[]
): string | null {
  if (
    isScreenAvailable("ctms") &&
    hasRequiredRole(normalizedRoles, SITE_READINESS_SPONSOR_ROLES)
  ) {
    return "/ctms";
  }
  if (
    isScreenAvailable("ssu") &&
    hasRequiredRole(normalizedRoles, SITE_READINESS_SITE_ROLES)
  ) {
    return "/ssu";
  }
  return null;
}

export function canAccessDataCollection(normalizedRoles: string[]): boolean {
  return (
    isScreenAvailable("ecrf") &&
    hasRequiredRole(normalizedRoles, DATA_COLLECTION_ROLES)
  );
}

/**
 * Resolves the active participant and kernel enrollment ID from Clinical Canvas state
 * when the current study matches the canvas store's loaded study.
 */
export function resolveCanvasParticipantContext(
  activeStudyCode: string,
  routeQuery: LocationQuery | Record<string, unknown> | undefined,
  canvasStore: {
    studyCode?: string;
    knownParticipants?: Record<string, SubjectSummary>;
    record?: { subjectCode?: string; enrollmentId?: string | null };
  }
): { subjectCode: string; enrollmentId: string } {
  const study = activeStudyCode.trim();
  if (!study || canvasStore.studyCode !== study) {
    return { subjectCode: "", enrollmentId: "" };
  }
  const participantRef =
    parseNodeRef(routeQuery?.participant) ?? parseNodeRef(routeQuery?.node);
  const participantNode =
    participantRef?.kind === "participant" ? participantRef : null;
  const known = participantNode
    ? canvasStore.knownParticipants?.[participantNode.id]
    : undefined;
  const subjectCode =
    known?.subject_id || canvasStore.record?.subjectCode || "";
  const enrollmentId =
    subjectCode &&
    canvasStore.record?.subjectCode === subjectCode &&
    canvasStore.record?.enrollmentId
      ? canvasStore.record.enrollmentId
      : "";
  return { subjectCode, enrollmentId };
}

/**
 * Builds the `/ecrf` path preserving active study, participant, and resolved kernel enrollment context.
 */
export function buildDataCollectionPath(context: {
  studyCode?: string;
  subjectCode?: string;
  enrollmentId?: string | null;
}): string {
  const studyCode = context.studyCode?.trim() || "";
  if (!studyCode) return "/ecrf";
  const params = new URLSearchParams();
  params.set("studyId", studyCode);
  const subjectCode = context.subjectCode?.trim() || "";
  if (subjectCode) {
    params.set("subjectId", subjectCode);
  }
  const enrollmentId = context.enrollmentId?.trim() || "";
  if (enrollmentId) {
    params.set("enrollmentId", enrollmentId);
  }
  return `/ecrf?${params.toString()}`;
}

/**
 * Builds UX-1 compliant, backend-available, role-authorized workflow destinations
 * and workspace controls for the unified global command palette.
 */
export function buildWorkspacePaletteItems(options: {
  isAuthenticated: boolean;
  normalizedRoles: string[];
  availablePersonas?: Array<{ key: string; label: string }>;
  activeStudyCode?: string;
  activeSubjectCode?: string;
  activeEnrollmentId?: string | null;
}): WorkspacePaletteOption[] {
  if (!options.isAuthenticated) return [];
  const roles = options.normalizedRoles || [];
  const items: WorkspacePaletteOption[] = [];

  if (isScreenAvailable("study")) {
    const studyPath = options.activeStudyCode
      ? `/studies/${encodeURIComponent(options.activeStudyCode)}`
      : "/studies";
    items.push({
      key: "workspace:studies",
      group: "Workspace",
      label: "Studies",
      detail: "Open a study and navigate its lifecycle",
      target: { routePath: studyPath },
    });
  }

  if (canAccessDataCollection(roles)) {
    items.push({
      key: "workspace:data-collection",
      group: "Workspace",
      label: "Data collection",
      detail: "Clinical case report forms and discrepancy review",
      target: {
        routePath: buildDataCollectionPath({
          studyCode: options.activeStudyCode,
          subjectCode: options.activeSubjectCode,
          enrollmentId: options.activeEnrollmentId,
        }),
      },
    });
  }

  const siteReadinessPath = resolveSiteReadinessPath(roles);
  if (siteReadinessPath) {
    items.push({
      key: "workspace:site-readiness",
      group: "Workspace",
      label: "Site readiness",
      detail: "Site activation, milestones, and readiness workflows",
      target: { routePath: siteReadinessPath },
    });
  }

  if (isScreenAvailable("overview")) {
    items.push({
      key: "workspace:orientation",
      group: "Workspace",
      label: "Orientation",
      detail: "Role-tailored workspace orientation and setup",
      target: { routePath: "/overview" },
    });
  }

  items.push(
    {
      key: "action:open-help",
      group: "Action",
      label: "Open contextual help",
      detail: "View regulatory and standard operating procedure guidance",
      target: { action: { kind: "open-help" } },
    },
    {
      key: "action:open-checklist",
      group: "Action",
      label: "Open setup checklist",
      detail: "Review opt-in workspace setup tasks",
      target: { action: { kind: "open-checklist" } },
    },
    {
      key: "action:start-tour",
      group: "Action",
      label: "Start guided tour",
      detail: "Launch opt-in interactive workspace tour",
      target: { action: { kind: "start-tour" } },
    }
  );

  for (const persona of options.availablePersonas || []) {
    const cleanLabel = persona.label.replace(/^[^\w\s]+\s*/, "").trim();
    items.push({
      key: `action:persona:${persona.key}`,
      group: "Action",
      label: `Switch view: ${cleanLabel}`,
      detail: "Adjust workspace orientation and help emphasis",
      target: {
        action: { kind: "switch-persona", personaKey: persona.key },
      },
    });
  }

  return items;
}

export function executePaletteTarget(
  target: PaletteTarget,
  context: {
    router: Router;
    currentRouteName?: string | symbol | null;
    currentQuery?: LocationQuery;
    activeStudyCode?: string;
    onAction?: (action: PaletteWorkspaceAction) => void;
  }
): void {
  if (target.action) {
    context.onAction?.(target.action);
    return;
  }
  if (target.routePath) {
    context.router.push(target.routePath);
    return;
  }
  const studyCode = target.studyCode || context.activeStudyCode || "";
  if (!studyCode) {
    context.router.push({ name: "study" });
    return;
  }
  if (target.ask) {
    const baseQuery =
      context.currentRouteName === "study" && context.currentQuery
        ? { ...context.currentQuery }
        : {};
    context.router.push({
      name: "study",
      params: { studyCode },
      query: { ...baseQuery, ask: target.ask },
    });
    return;
  }
  const query: Record<string, string> = {};
  if (target.stage) query.stage = target.stage;
  if (target.section) query.section = target.section;
  if (target.participant) {
    const node = tryNodeRef("participant", target.participant.id);
    if (node) query.node = formatNodeRef(node);
  }
  context.router.push({
    name: "study",
    params: { studyCode },
    query,
  });
}
