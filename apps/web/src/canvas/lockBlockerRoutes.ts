import type { RouteLocationRaw } from "vue-router";
import type { KernelLockBlocker } from "../api/kernelApi";
import {
  collectionInstanceRef,
  formatNodeRef,
  observationNodeRef,
  parseNodeRef,
} from "./nodeRef";
import {
  buildScopedStudyLocation,
  type WorkspaceScopeSnapshot,
} from "./palette";
import {
  formatLockBlockerFallbackTitle,
  normalizeLockBlockerKind,
} from "../composables/useSignedKernelLock";

export interface LockBlockerChipDescriptor {
  key: string;
  ref: string;
  fallback?: string;
}

export function getLockBlockerChips(
  blocker: Partial<KernelLockBlocker>
): LockBlockerChipDescriptor[] {
  const chips: LockBlockerChipDescriptor[] = [];
  if (blocker.target_ref) {
    chips.push({
      key: `t-${blocker.target_ref}`,
      ref: blocker.target_ref,
      fallback: formatLockBlockerFallbackTitle(blocker),
    });
  }
  if (blocker.query_ref && blocker.query_ref !== blocker.target_ref) {
    chips.push({ key: `q-${blocker.query_ref}`, ref: blocker.query_ref });
  }
  if (blocker.finding_ref && blocker.finding_ref !== blocker.target_ref) {
    chips.push({
      key: `f-${blocker.finding_ref}`,
      ref: blocker.finding_ref,
      fallback: blocker.rule_id ?? undefined,
    });
  }
  return chips;
}

export type LockBlockerRoleFilter = "all" | "monitor" | "medical" | "site";

export interface LockBlockerRoleFilterOption {
  id: LockBlockerRoleFilter;
  label: string;
}

export const LOCK_BLOCKER_ROLE_FILTERS: readonly LockBlockerRoleFilterOption[] =
  [
    { id: "all", label: "All blockers" },
    { id: "monitor", label: "Monitor SDV" },
    { id: "medical", label: "Medical review" },
    { id: "site", label: "Site coordinator / PI" },
  ];

export function countLockBlockersForRoleFilter(
  blockers: ReadonlyArray<Partial<KernelLockBlocker>>,
  filter: LockBlockerRoleFilter
): number {
  if (filter === "all") return blockers.length;
  return blockers.filter((b) => matchesLockBlockerRoleFilter(b, filter)).length;
}

export function matchesLockBlockerRoleFilter(
  blocker: Partial<KernelLockBlocker>,
  filter: LockBlockerRoleFilter
): boolean {
  if (filter === "all") return true;
  const kind = normalizeLockBlockerKind(blocker.kind);
  const owner = (blocker.owner ?? "").toLowerCase();

  if (filter === "monitor") {
    return (
      kind === "UnverifiedValue" ||
      owner.includes("clinical research associate") ||
      (owner.includes("monitor") && !owner.includes("medical monitor"))
    );
  }

  if (filter === "medical") {
    return kind === "UnreviewedValue" || owner.includes("medical monitor");
  }

  // "site" -> Site CRC / Principal Investigator owned items
  return (
    kind === "MissingRequiredItem" ||
    kind === "UnsignedForm" ||
    kind === "UnopenedVisit" ||
    owner.includes("investigator") ||
    owner.includes("coordinator") ||
    owner.includes("site")
  );
}

function resolveBlockerEnrollmentRef(
  blocker: Partial<KernelLockBlocker>
): string | null {
  const candidates = [
    blocker.enrollment_ref,
    blocker.target_kind === "enrollment" ? blocker.target_ref : null,
  ];
  for (const raw of candidates) {
    if (!raw) continue;
    const parsed = parseNodeRef(raw);
    if (parsed?.kind === "enrollment") {
      return formatNodeRef({ kind: "enrollment", id: parsed.id });
    }
  }
  return null;
}

function resolveBlockerFormRef(
  blocker: Partial<KernelLockBlocker>
): string | null {
  if (blocker.form_ref) {
    const resolved = collectionInstanceRef(blocker.form_ref);
    if (resolved) return resolved;
  }
  if (blocker.target_kind === "collection_instance" && blocker.target_ref) {
    const resolved = collectionInstanceRef(blocker.target_ref);
    if (resolved) return resolved;
  }
  if (blocker.target_ref?.startsWith("collection_instance:")) {
    return collectionInstanceRef(blocker.target_ref);
  }
  return null;
}

function resolveBlockerObservationRef(
  blocker: Partial<KernelLockBlocker>
): string | null {
  if (blocker.target_kind === "observation" && blocker.target_ref) {
    return observationNodeRef(blocker.target_ref);
  }
  if (blocker.target_ref?.startsWith("observation:")) {
    return observationNodeRef(blocker.target_ref);
  }
  return null;
}

/**
 * Pure, router-safe route builder mapping an unmasked closeout readiness blocker
 * to its actionable workspace inside `StudyCanvasView`.
 */
export function resolveLockBlockerLocation(
  studyCode: string | null | undefined,
  blocker: Partial<KernelLockBlocker>,
  currentScope?: Partial<WorkspaceScopeSnapshot> | null
): RouteLocationRaw | null {
  const code = studyCode?.trim() ?? "";
  if (!code || blocker.masked) return null;

  const kind = normalizeLockBlockerKind(blocker.kind);
  const site = blocker.site_code?.trim() || undefined;
  const participant = resolveBlockerEnrollmentRef(blocker) ?? undefined;
  const formRef = resolveBlockerFormRef(blocker);
  const obsRef = resolveBlockerObservationRef(blocker);
  const itemId = blocker.item_id?.trim() || undefined;

  if (kind === "ErrorFinding") {
    return buildScopedStudyLocation(
      code,
      {
        stage: "data",
        section: "validation",
        site,
        participant,
        node: "",
        ...(formRef ? { form: formRef } : {}),
        ...(formRef && itemId ? { item: itemId } : {}),
        ...(!formRef && obsRef ? { observation: obsRef } : {}),
      },
      currentScope
    );
  }

  if (kind === "UnopenedVisit") {
    if (participant) {
      const spec = blocker.collection_spec_id?.trim() || undefined;
      const visit = blocker.planned_activity_id?.trim() || undefined;
      const built = buildScopedStudyLocation(
        code,
        {
          stage: "conduct",
          site,
          participant,
          node: "",
          ...(spec || visit ? { form: "new" } : {}),
        },
        currentScope
      );
      if (spec) built.query.spec = spec;
      if (visit) built.query.visit = visit;
      return built;
    }
    return buildScopedStudyLocation(
      code,
      {
        stage: "conduct",
        section: "participants",
        site,
        node: "",
      },
      currentScope
    );
  }

  if (formRef) {
    return buildScopedStudyLocation(
      code,
      {
        stage: "conduct",
        site,
        participant,
        node: "",
        form: formRef,
        ...(itemId ? { item: itemId } : {}),
      },
      currentScope
    );
  }

  if (obsRef) {
    return buildScopedStudyLocation(
      code,
      {
        stage: "conduct",
        site,
        participant,
        node: "",
        observation: obsRef,
      },
      currentScope
    );
  }

  if (participant) {
    return buildScopedStudyLocation(
      code,
      {
        stage: "conduct",
        site,
        participant,
        node: "",
      },
      currentScope
    );
  }

  if (kind === "OpenQuery") {
    return buildScopedStudyLocation(
      code,
      {
        stage: "data",
        section: "queries",
        site,
        node: "",
      },
      currentScope
    );
  }

  return null;
}

export function resolveLockBlockerActionLabel(
  blocker: Partial<KernelLockBlocker>
): string {
  const kind = normalizeLockBlockerKind(blocker.kind);
  if (kind === "ErrorFinding") return "Open validation";
  if (kind === "UnopenedVisit") return "Open visit";
  if (resolveBlockerFormRef(blocker)) {
    if (kind === "OpenQuery") return "Open query in form";
    if (kind === "UnverifiedValue") return "Open form to verify";
    if (kind === "UnreviewedValue") return "Open form to review";
    if (kind === "UnsignedForm") return "Open form to sign";
    if (kind === "MissingRequiredItem") return "Open item in form";
    return blocker.item_id?.trim() ? "Open item" : "Open form";
  }
  if (resolveBlockerObservationRef(blocker)) {
    if (kind === "UnverifiedValue") return "Verify observation";
    if (kind === "UnreviewedValue") return "Review observation";
    return "Open observation";
  }
  if (kind === "OpenQuery") return "Open queries";
  return "Open workspace";
}

export function resolveInspectorNodeWorkspaceLocation(
  studyCode: string | null | undefined,
  nodeRef: string | null | undefined,
  currentScope?: Partial<WorkspaceScopeSnapshot> | null
): { to: RouteLocationRaw; label: string } | null {
  const code = studyCode?.trim() ?? "";
  const parsed = parseNodeRef(nodeRef ?? "");
  if (!code || !parsed) return null;

  switch (parsed.kind) {
    case "collection_instance":
      return {
        to: buildScopedStudyLocation(
          code,
          {
            stage: "conduct",
            node: "",
            form: formatNodeRef({
              kind: "collection_instance",
              id: parsed.id,
            }),
          },
          currentScope
        ),
        label: "Open form workspace",
      };
    case "observation":
      return {
        to: buildScopedStudyLocation(
          code,
          {
            stage: "conduct",
            node: "",
            observation: formatNodeRef({ kind: "observation", id: parsed.id }),
          },
          currentScope
        ),
        label: "Open observation",
      };
    case "enrollment":
      return {
        to: buildScopedStudyLocation(
          code,
          {
            stage: "conduct",
            node: "",
            participant: formatNodeRef({ kind: "enrollment", id: parsed.id }),
          },
          currentScope
        ),
        label: "Open participant's data",
      };
    case "validation_finding":
      return {
        to: buildScopedStudyLocation(
          code,
          { stage: "data", section: "validation", node: "" },
          currentScope
        ),
        label: "Open validation workspace",
      };
    case "study_site":
      return {
        to: buildScopedStudyLocation(
          code,
          { stage: "conduct", section: "sites", node: "" },
          currentScope
        ),
        label: "Open study sites",
      };
    case "assertion":
    case "attestation":
      return {
        to: buildScopedStudyLocation(
          code,
          { stage: "data", section: "locks", node: "" },
          currentScope
        ),
        label: "Open closeout & locks",
      };
    case "proposal":
      return {
        to: buildScopedStudyLocation(
          code,
          {
            node: "",
            proposal: formatNodeRef({ kind: "proposal", id: parsed.id }),
          },
          currentScope
        ),
        label: "Open proposal review",
      };
    default:
      return null;
  }
}
