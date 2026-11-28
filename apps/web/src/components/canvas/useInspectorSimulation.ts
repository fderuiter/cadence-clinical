import { ref, type ComputedRef, type Ref } from "vue";
import {
  isMasked,
  kernelApi,
  type KernelPlannedVisit,
  type KernelScheduleChange,
  type KernelSimulationResult,
} from "../../api/kernelApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";
import {
  ensureNodesResolved,
  formatCommandTitle,
  getCachedNodeEntry,
  seedNodeSummary,
} from "../../canvas/nodeResolver";

const SIMULATION_PART_LABELS: Record<string, string> = {
  structural_diff: "Protocol element changes",
  schedule_changes: "Visit schedule changes",
  dependents: "Downstream impact",
  matrix_gaps: "Schedule findings",
  operational_impact: "Operational site & participant impact",
  projection_changes: "Dataset projection changes",
  validation_delta: "Validation rule delta",
};

export type SimulationCandidateKind =
  "ProtocolVersion" | "MappingSpecification" | "RulePack";

export interface CandidateSimulationOverride {
  kind?: SimulationCandidateKind;
  studyRef?: string;
  storedVersionRef?: string;
  baseVersionRef?: string;
  candidateContent?: string;
  libraryItemRef?: string;
  baseLibraryItemRef?: string;
  standardVersionRef?: string;
}

export interface InspectorSimulationContext {
  studyCode?: () => string;
  studyRef?: () => string | null | undefined;
}

export function plainSimulationWords(name: string): string {
  return name
    .replace(/_/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .toLowerCase();
}

export function formatSimulationPart(part: string): string {
  return SIMULATION_PART_LABELS[part] ?? formatCommandTitle(part);
}

function formatPlannedVisit(visit: KernelPlannedVisit): string {
  const window =
    visit.window_before || visit.window_after
      ? ` (-${visit.window_before}/+${visit.window_after}d)`
      : "";
  return `Day ${visit.study_day}${window} in ${visit.epoch_id}`;
}

export function formatScheduleChange(change: KernelScheduleChange): string {
  if (change.before && change.after) {
    return `${formatPlannedVisit(change.before)} -> ${formatPlannedVisit(change.after)}`;
  }
  if (change.after) {
    return formatPlannedVisit(change.after);
  }
  if (change.before) {
    return `was ${formatPlannedVisit(change.before)}`;
  }
  return "";
}

function inferLibraryCandidateKind(nodeRef: string): SimulationCandidateKind {
  const cached = getCachedNodeEntry(nodeRef)?.node;
  const hay = `${cached?.title ?? ""} ${cached?.code ?? ""}`.toLowerCase();
  if (hay.includes("mapping")) {
    return "MappingSpecification";
  }
  return "RulePack";
}

function warmSimulationNodeRefs(result: KernelSimulationResult): void {
  const refsToResolve: string[] = [];

  if (result.dependents?.groups?.length) {
    for (const group of result.dependents.groups) {
      for (const obj of group.objects) {
        if (!isMasked(obj) && obj.ref) {
          refsToResolve.push(obj.ref);
        }
      }
    }
  }

  for (const diff of result.structural_diff ?? []) {
    if (diff.protocol_element && parseNodeRef(diff.protocol_element)) {
      seedNodeSummary(diff.protocol_element, {
        kind: "protocol_element",
        title: diff.element_id,
      });
      refsToResolve.push(diff.protocol_element);
    }
  }

  for (const sc of result.schedule_changes ?? []) {
    if (sc.protocol_element && parseNodeRef(sc.protocol_element)) {
      seedNodeSummary(sc.protocol_element, {
        kind: "protocol_element",
        title: sc.activity_id,
      });
      refsToResolve.push(sc.protocol_element);
    }
  }

  for (const gap of result.matrix_gaps ?? []) {
    if (gap.protocol_element && parseNodeRef(gap.protocol_element)) {
      if (gap.element_id) {
        seedNodeSummary(gap.protocol_element, {
          kind: "protocol_element",
          title: gap.element_id,
        });
      }
      refsToResolve.push(gap.protocol_element);
    }
  }

  for (const impact of result.operational_impact?.participant_impacts ?? []) {
    if (impact.enrollment && parseNodeRef(impact.enrollment)) {
      if (impact.subject_code) {
        seedNodeSummary(impact.enrollment, {
          kind: "enrollment",
          code: impact.subject_code,
        });
      }
      refsToResolve.push(impact.enrollment);
    }
    for (const shift of impact.schedule_shifts ?? []) {
      if (shift.protocol_element && parseNodeRef(shift.protocol_element)) {
        seedNodeSummary(shift.protocol_element, {
          kind: "protocol_element",
          title: shift.activity_id,
        });
        refsToResolve.push(shift.protocol_element);
      }
    }
  }

  const findings = [
    ...(result.validation_delta?.opening ?? []),
    ...(result.validation_delta?.resolving ?? []),
  ];
  for (const finding of findings) {
    if (finding.subject && parseNodeRef(finding.subject)) {
      refsToResolve.push(finding.subject);
    }
  }

  if (refsToResolve.length > 0) {
    ensureNodesResolved(refsToResolve);
  }
}

export function useInspectorSimulation(
  canSimulate: ComputedRef<boolean>,
  kernelRef: ComputedRef<string>,
  context?: InspectorSimulationContext
): {
  simulationResult: Ref<KernelSimulationResult | null>;
  simulationLoading: Ref<boolean>;
  simulationError: Ref<string>;
  runSimulation: () => Promise<void>;
  runCandidateSimulation: (
    override: CandidateSimulationOverride
  ) => Promise<void>;
  resetSimulation: () => void;
} {
  const simulationResult = ref<KernelSimulationResult | null>(null);
  const simulationLoading = ref(false);
  const simulationError = ref("");
  let lastOverride: CandidateSimulationOverride | null = null;
  let simulationRequest = 0;

  async function resolveContextStudyRef(
    explicitStudyRef?: string
  ): Promise<string | undefined> {
    const direct = explicitStudyRef?.trim() || context?.studyRef?.()?.trim();
    if (direct) return direct;
    const code = context?.studyCode?.()?.trim();
    if (!code) return undefined;
    const lookedUp = await findKernelStudyRef(code);
    return lookedUp?.trim() || undefined;
  }

  async function executeRequest(
    override: CandidateSimulationOverride | null
  ): Promise<void> {
    if (!canSimulate.value && !override) return;
    if (!kernelRef.value && !override) return;
    const request = ++simulationRequest;
    simulationLoading.value = true;
    simulationError.value = "";
    try {
      let result: KernelSimulationResult;
      const activeParsed = parseNodeRef(kernelRef.value);
      if (!override && activeParsed?.kind === "protocol_version") {
        result = await kernelApi.simulate({
          storedVersionRef: kernelRef.value,
        });
      } else if (!override && activeParsed?.kind === "library_item") {
        const studyRef = await resolveContextStudyRef();
        const kind = inferLibraryCandidateKind(kernelRef.value);
        result = await kernelApi.simulate({
          kind,
          studyRef,
          libraryItemRef: kernelRef.value,
        });
      } else {
        const kind = override?.kind ?? "ProtocolVersion";
        const studyRef = await resolveContextStudyRef(override?.studyRef);
        const candidateContent =
          override?.candidateContent?.trim() || undefined;
        const baseVersionRef = override?.baseVersionRef?.trim() || undefined;
        const storedVersionRef =
          override?.storedVersionRef?.trim() ||
          (kind === "ProtocolVersion" &&
          !candidateContent &&
          activeParsed?.kind === "protocol_version"
            ? kernelRef.value
            : undefined);
        const libraryItemRef =
          override?.libraryItemRef?.trim() ||
          (kind !== "ProtocolVersion" && activeParsed?.kind === "library_item"
            ? kernelRef.value
            : undefined);
        const baseLibraryItemRef =
          override?.baseLibraryItemRef?.trim() || undefined;
        const standardVersionRef =
          override?.standardVersionRef?.trim() || undefined;

        result = await kernelApi.simulate({
          kind,
          ...(studyRef ? { studyRef } : {}),
          ...(storedVersionRef ? { storedVersionRef } : {}),
          ...(baseVersionRef ? { baseVersionRef } : {}),
          ...(candidateContent ? { candidateContent } : {}),
          ...(libraryItemRef ? { libraryItemRef } : {}),
          ...(baseLibraryItemRef ? { baseLibraryItemRef } : {}),
          ...(standardVersionRef ? { standardVersionRef } : {}),
        });
      }
      if (request !== simulationRequest) return;
      simulationResult.value = result;
      warmSimulationNodeRefs(result);
    } catch (e: unknown) {
      if (request !== simulationRequest) return;
      simulationError.value = e instanceof Error ? e.message : String(e);
    } finally {
      if (request === simulationRequest) {
        simulationLoading.value = false;
      }
    }
  }

  async function runSimulation() {
    await executeRequest(lastOverride);
  }

  async function runCandidateSimulation(override: CandidateSimulationOverride) {
    lastOverride = { ...override };
    await executeRequest(lastOverride);
  }

  function resetSimulation() {
    simulationRequest++;
    lastOverride = null;
    simulationResult.value = null;
    simulationLoading.value = false;
    simulationError.value = "";
  }

  return {
    simulationResult,
    simulationLoading,
    simulationError,
    runSimulation,
    runCandidateSimulation,
    resetSimulation,
  };
}
