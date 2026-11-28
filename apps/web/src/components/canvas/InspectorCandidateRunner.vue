<template>
  <details id="kernel-simulate-candidate-runner" class="candidate-runner">
    <summary class="candidate-runner-summary">
      Evaluate candidate draft, mapping, or rule pack
    </summary>
    <form
      id="kernel-simulate-candidate-form"
      class="candidate-form"
      @submit.prevent="submitCandidate"
    >
      <div class="candidate-field">
        <label for="simulate-candidate-kind">Candidate kind</label>
        <select
          id="simulate-candidate-kind"
          v-model="candidateKind"
          class="candidate-control"
          :disabled="disabled"
        >
          <option value="ProtocolVersion">Protocol version or draft</option>
          <option value="MappingSpecification">Mapping specification</option>
          <option value="RulePack">Validation rule pack</option>
        </select>
      </div>

      <template v-if="candidateKind === 'ProtocolVersion'">
        <div class="candidate-field">
          <label for="simulate-base-version-ref">
            Base version reference (optional)
          </label>
          <input
            id="simulate-base-version-ref"
            v-model="baseVersionRef"
            type="text"
            class="candidate-control candidate-mono"
            :disabled="disabled"
            placeholder="protocol_version:..."
          />
        </div>
        <div class="candidate-field">
          <label for="simulate-candidate-content">
            Candidate USDM JSON (optional override)
          </label>
          <textarea
            id="simulate-candidate-content"
            v-model="candidateContent"
            rows="3"
            class="candidate-control candidate-mono"
            :disabled="disabled"
            :placeholder="'{&quot;schema&quot;:&quot;cadence.usdm.design/v1&quot;}'"
          />
        </div>
      </template>

      <template v-else>
        <div class="candidate-field">
          <label for="simulate-library-item-ref">
            Library item reference
          </label>
          <input
            id="simulate-library-item-ref"
            v-model="libraryItemRef"
            type="text"
            class="candidate-control candidate-mono"
            :disabled="disabled"
            placeholder="library_item:..."
          />
        </div>
        <div class="candidate-field">
          <label for="simulate-base-library-item-ref">
            Base library item reference (optional)
          </label>
          <input
            id="simulate-base-library-item-ref"
            v-model="baseLibraryItemRef"
            type="text"
            class="candidate-control candidate-mono"
            :disabled="disabled"
            placeholder="library_item:..."
          />
        </div>
        <div
          v-if="candidateKind === 'MappingSpecification'"
          class="candidate-field"
        >
          <label for="simulate-standard-version-ref">
            Standard version reference (optional)
          </label>
          <input
            id="simulate-standard-version-ref"
            v-model="standardVersionRef"
            type="text"
            class="candidate-control candidate-mono"
            :disabled="disabled"
            placeholder="library_item:..."
          />
        </div>
        <div class="candidate-field">
          <label for="simulate-candidate-content">
            Candidate JSON payload (optional override)
          </label>
          <textarea
            id="simulate-candidate-content"
            v-model="candidateContent"
            rows="3"
            class="candidate-control candidate-mono"
            :disabled="disabled"
            :placeholder="'{&quot;rules&quot;:[]}'"
          />
        </div>
      </template>

      <button
        id="btn-run-candidate-simulation"
        type="submit"
        class="btn btn-secondary candidate-submit"
        :disabled="disabled"
      >
        {{ disabled ? "Simulating…" : "Run candidate simulation" }}
      </button>
    </form>
  </details>
</template>

<script setup lang="ts">
import { ref, watch } from "vue";
import type { KernelProtocolVersion } from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";
import type {
  CandidateSimulationOverride,
  SimulationCandidateKind,
} from "./useInspectorSimulation";

const props = defineProps<{
  kernelRef?: string;
  protocolVersion?: KernelProtocolVersion | null;
  disabled?: boolean;
}>();

const emit = defineEmits<{
  (e: "run-candidate", override: CandidateSimulationOverride): void;
}>();

const candidateKind = ref<SimulationCandidateKind>("ProtocolVersion");
const baseVersionRef = ref("");
const libraryItemRef = ref("");
const baseLibraryItemRef = ref("");
const standardVersionRef = ref("");
const candidateContent = ref("");

watch(
  () => [props.kernelRef, props.protocolVersion?.ref] as const,
  ([nextRef]) => {
    const parsed = parseNodeRef(nextRef ?? "");
    if (parsed?.kind === "library_item") {
      candidateKind.value = "RulePack";
      libraryItemRef.value = nextRef ?? "";
    } else {
      candidateKind.value = "ProtocolVersion";
      baseVersionRef.value = props.protocolVersion?.parent_version ?? "";
    }
  },
  { immediate: true }
);

function submitCandidate() {
  if (candidateKind.value === "ProtocolVersion") {
    const hasCustomDraft = Boolean(candidateContent.value.trim());
    emit("run-candidate", {
      kind: "ProtocolVersion",
      storedVersionRef: hasCustomDraft
        ? undefined
        : props.protocolVersion?.ref || props.kernelRef || undefined,
      baseVersionRef: baseVersionRef.value.trim() || undefined,
      candidateContent: candidateContent.value.trim() || undefined,
    });
    return;
  }

  emit("run-candidate", {
    kind: candidateKind.value,
    libraryItemRef: libraryItemRef.value.trim() || undefined,
    baseLibraryItemRef: baseLibraryItemRef.value.trim() || undefined,
    standardVersionRef:
      candidateKind.value === "MappingSpecification"
        ? standardVersionRef.value.trim() || undefined
        : undefined,
    candidateContent: candidateContent.value.trim() || undefined,
  });
}
</script>

<style scoped>
.candidate-runner {
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 6px 8px;
  background: var(--neutral-light);
}

.candidate-runner-summary {
  min-height: var(--semantic-control-target-min, 24px);
  display: flex;
  align-items: center;
  cursor: pointer;
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.candidate-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 8px;
}

.candidate-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.candidate-field label {
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.candidate-control {
  width: 100%;
  box-sizing: border-box;
  min-height: 36px;
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: 0.85rem;
}

.candidate-mono {
  font-family: var(--semantic-font-mono);
}

.candidate-submit {
  width: 100%;
  min-height: 36px;
  justify-content: center;
}

@media (pointer: coarse), (max-width: 767px) {
  .candidate-control {
    min-height: 44px;
    font-size: 1rem;
  }

  .candidate-submit {
    min-height: 44px;
  }
}
</style>
