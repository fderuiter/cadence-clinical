import { ref, type ComputedRef, type Ref } from "vue";
import {
  isMasked,
  kernelApi,
  type KernelPlannedVisit,
  type KernelScheduleChange,
  type KernelSimulationResult,
} from "../../api/kernelApi";
import {
  ensureNodesResolved,
  formatCommandTitle,
} from "../../canvas/nodeResolver";

const SIMULATION_PART_LABELS: Record<string, string> = {
  structural_diff: "Protocol element changes",
  schedule_changes: "Visit schedule changes",
  dependents: "Downstream impact",
  matrix_gaps: "Schedule findings",
};

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

export function useInspectorSimulation(
  canSimulate: ComputedRef<boolean>,
  kernelRef: ComputedRef<string>
): {
  simulationResult: Ref<KernelSimulationResult | null>;
  simulationLoading: Ref<boolean>;
  simulationError: Ref<string>;
  runSimulation: () => Promise<void>;
  resetSimulation: () => void;
} {
  const simulationResult = ref<KernelSimulationResult | null>(null);
  const simulationLoading = ref(false);
  const simulationError = ref("");
  let simulationRequest = 0;

  async function runSimulation() {
    if (!canSimulate.value || !kernelRef.value) return;
    const request = ++simulationRequest;
    simulationLoading.value = true;
    simulationError.value = "";
    try {
      const result = await kernelApi.simulate({
        storedVersionRef: kernelRef.value,
      });
      if (request !== simulationRequest) return;
      simulationResult.value = result;
      if (result.dependents?.groups?.length) {
        ensureNodesResolved(
          result.dependents.groups.flatMap((group) =>
            group.objects.filter((obj) => !isMasked(obj)).map((obj) => obj.ref)
          )
        );
      }
    } catch (e: unknown) {
      if (request !== simulationRequest) return;
      simulationError.value = e instanceof Error ? e.message : String(e);
    } finally {
      if (request === simulationRequest) {
        simulationLoading.value = false;
      }
    }
  }

  function resetSimulation() {
    simulationRequest++;
    simulationResult.value = null;
    simulationLoading.value = false;
    simulationError.value = "";
  }

  return {
    simulationResult,
    simulationLoading,
    simulationError,
    runSimulation,
    resetSimulation,
  };
}
