<template>
  <div
    v-if="canGovern"
    id="simulation-governance-actions"
    class="simulation-gov"
  >
    <p
      v-if="hasMaskedParts"
      id="simulation-governance-masked-note"
      class="simulation-gov-note"
      role="status"
    >
      Withheld sections prevent saving this simulation as an attestable report
      (K-SIM-5). Sign in with full study visibility to persist and sign review.
    </p>
    <p
      v-else-if="!isFrozenVersion"
      id="simulation-governance-draft-note"
      class="simulation-gov-note"
      role="status"
    >
      Submit Version {{ protocolVersion?.number }} for review to freeze its
      content digest before saving and signing the formal amendment impact
      review.
    </p>

    <div
      v-if="savedArtifact || existingArtifactRef"
      id="simulation-saved-report-status"
      class="simulation-gov-status"
      role="status"
    >
      <div class="simulation-gov-status-row">
        <strong>Simulation report saved</strong>
        <NodeRefChip
          v-if="activeArtifactVersionRef"
          :node-ref="activeArtifactVersionRef"
        />
      </div>
      <div
        v-if="reviewedAttestationRef"
        id="simulation-review-signed-status"
        class="simulation-gov-status-row simulation-gov-signed"
      >
        <span>21 CFR Part 11 Reviewed</span>
        <NodeRefChip :node-ref="reviewedAttestationRef" />
      </div>
    </div>

    <div
      v-if="feedbackMessage"
      id="simulation-governance-feedback"
      class="simulation-gov-banner simulation-gov-banner--success"
      role="status"
    >
      {{ feedbackMessage }}
    </div>
    <div
      v-if="errorMessage"
      id="simulation-governance-error"
      class="simulation-gov-banner simulation-gov-banner--error"
      role="alert"
    >
      {{ errorMessage }}
    </div>

    <ClinicalInput
      id="simulation-governance-reason"
      v-model="saveReason"
      label="Audit reason"
      type="text"
      density="compact"
      :show-query-flag="false"
      extra-class="simulation-gov-reason-field"
      :maxlength="500"
      :disabled="busy || hasMaskedParts"
      placeholder="Reason recorded when saving the simulation report"
    />

    <div class="simulation-gov-buttons">
      <button
        id="btn-save-simulation-report"
        type="button"
        class="btn btn-secondary simulation-gov-btn"
        :disabled="!canSaveReport"
        @click="saveReport"
      >
        {{
          saving
            ? "Saving simulation report…"
            : activeArtifactVersionRef
              ? "Simulation report saved"
              : "Save simulation report"
        }}
      </button>
      <button
        id="btn-sign-simulation-review"
        type="button"
        class="btn btn-primary simulation-gov-btn"
        :disabled="!canSignReview"
        @click="openReviewSignature"
      >
        {{
          reviewedAttestationRef
            ? "Review signed (Part 11)"
            : "Sign review (Part 11)"
        }}
      </button>
    </div>

    <SignatureCaptureModal
      v-if="signingReview && activeArtifactVersionRef"
      :is-open="signingReview"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Sponsor Designer"
      :reasons="SIMULATION_REVIEW_REASONS"
      :action-url="artifactVersionReviewAction(activeArtifactVersionRef)"
      :semantic-action="ARTIFACT_VERSION_REVIEW_SEMANTIC_ACTION"
      :on-sign="signReviewWithToken"
      @cancel="signingReview = false"
      @success="handleReviewSigned"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ClinicalInput } from "ui";
import {
  describeKernelError,
  type KernelProtocolVersion,
  type KernelSimulationResult,
} from "../../api/kernelApi";
import {
  ARTIFACT_VERSION_REVIEW_SEMANTIC_ACTION,
  artifactVersionReviewAction,
  kernelGovernanceApi,
  type KernelArtifact,
} from "../../api/kernelGovernanceApi";
import { canGovernProtocols } from "../../canvas/authority";
import { seedNodeSummary } from "../../canvas/nodeResolver";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import { useAuthStore } from "../../stores/auth";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import NodeRefChip from "./NodeRefChip.vue";

const SIMULATION_REVIEW_REASONS = [
  "Reviewed protocol amendment simulation and schedule impact",
  "CLINICAL_QC",
  "APPROVAL",
];

const props = defineProps<{
  studyCode: string;
  protocolVersion: KernelProtocolVersion | null;
  simulationResult: KernelSimulationResult;
}>();

const authStore = useAuthStore();
const canvasStore = useStudyCanvasStore();

const canGovern = computed(() => canGovernProtocols(authStore));
const signerUsername = computed(() => authStore.identity?.username ?? "");
const signerFullName = computed(() => {
  const identity = authStore.identity;
  if (!identity) return "";
  const full = [identity.firstName, identity.lastName]
    .filter(Boolean)
    .join(" ")
    .trim();
  return full || identity.username || "";
});

const { studyRef } = useKernelStudyRef(() => props.studyCode, {
  studyRef: () =>
    props.protocolVersion?.study ||
    (props.simulationResult.scope?.startsWith("study:")
      ? props.simulationResult.scope
      : null),
});

const saving = ref(false);
const signingBusy = ref(false);
const signingReview = ref(false);
const savedArtifact = ref<KernelArtifact | null>(null);
const existingArtifactRef = ref<string | null>(null);
const existingArtifactContentHash = ref<string | null>(null);
const reviewedAttestationRef = ref<string | null>(null);
const feedbackMessage = ref("");
const errorMessage = ref("");
const saveReason = ref(
  "Persist unmasked protocol schedule simulation report for amendment impact review"
);

const busy = computed(() => saving.value || signingBusy.value);
const hasMaskedParts = computed(() =>
  Boolean(props.simulationResult.masked?.length)
);
const isFrozenVersion = computed(() =>
  Boolean(
    props.protocolVersion &&
    props.protocolVersion.status !== "Draft" &&
    props.protocolVersion.content_hash
  )
);

const activeArtifactVersionRef = computed(
  () =>
    savedArtifact.value?.artifact_version_ref ?? existingArtifactRef.value ?? ""
);

const activeArtifactContentHash = computed(
  () =>
    savedArtifact.value?.content_hash ??
    existingArtifactContentHash.value ??
    undefined
);

const canSaveReport = computed(
  () =>
    canGovern.value &&
    !busy.value &&
    !hasMaskedParts.value &&
    isFrozenVersion.value &&
    Boolean(studyRef.value) &&
    Boolean(saveReason.value.trim()) &&
    !activeArtifactVersionRef.value &&
    typeof kernelGovernanceApi?.saveSimulationReport === "function"
);

const canSignReview = computed(
  () =>
    canGovern.value &&
    !busy.value &&
    Boolean(activeArtifactVersionRef.value) &&
    !reviewedAttestationRef.value &&
    typeof kernelGovernanceApi?.reviewArtifactVersion === "function"
);

function nextIdempotencyKey(): string {
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `sim-gov-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

let readinessProbeSeq = 0;

async function syncExistingReadiness() {
  const versionRef = props.protocolVersion?.ref;
  if (
    !versionRef ||
    !canGovern.value ||
    typeof kernelGovernanceApi?.getProtocolApprovalReadiness !== "function"
  ) {
    return;
  }
  const seq = ++readinessProbeSeq;
  try {
    const readiness =
      await kernelGovernanceApi.getProtocolApprovalReadiness(versionRef);
    if (seq !== readinessProbeSeq) return;
    if (readiness.simulation_report_artifact_version_ref) {
      existingArtifactRef.value =
        readiness.simulation_report_artifact_version_ref;
      seedNodeSummary(readiness.simulation_report_artifact_version_ref, {
        kind: "artifact_version",
        title:
          `Version ${props.protocolVersion?.number ?? ""} simulation report`.trim(),
      });
    }
    if (readiness.review_attestation_ref) {
      reviewedAttestationRef.value = readiness.review_attestation_ref;
      seedNodeSummary(readiness.review_attestation_ref, {
        kind: "attestation",
        title: "Reviewed attestation",
      });
    }
  } catch {
    // Readiness probe is advisory here; explicit Save / Sign actions surface actionable errors.
  }
}

watch(
  () => [
    props.protocolVersion?.ref,
    props.protocolVersion?.status,
    props.simulationResult.result_hash,
  ],
  () => {
    savedArtifact.value = null;
    existingArtifactRef.value = null;
    existingArtifactContentHash.value = null;
    reviewedAttestationRef.value = null;
    feedbackMessage.value = "";
    errorMessage.value = "";
    signingReview.value = false;
    void syncExistingReadiness();
  },
  { immediate: true }
);

async function saveReport() {
  if (!canSaveReport.value || !studyRef.value) return;
  saving.value = true;
  errorMessage.value = "";
  feedbackMessage.value = "";
  try {
    const versionNumber = props.protocolVersion?.number ?? 1;
    const created = await kernelGovernanceApi.saveSimulationReport({
      studyRef: studyRef.value,
      title: `Protocol Version ${versionNumber} Simulation Report`,
      result: props.simulationResult,
      reason: saveReason.value.trim(),
      idempotencyKey: nextIdempotencyKey(),
    });
    savedArtifact.value = created;
    existingArtifactRef.value = created.artifact_version_ref;
    existingArtifactContentHash.value = created.content_hash;
    seedNodeSummary(created.artifact_version_ref, {
      kind: "artifact_version",
      title: created.title,
    });
    feedbackMessage.value = `Saved simulation report v${created.version_number} as attestable evidence.`;
    canvasStore.notifyProtocolGovernanceChanged();
  } catch (err) {
    errorMessage.value = describeKernelError(err);
  } finally {
    saving.value = false;
  }
}

function openReviewSignature() {
  if (!canSignReview.value) return;
  errorMessage.value = "";
  signingReview.value = true;
}

function handleReviewSigned() {
  signingReview.value = false;
}

async function signReviewWithToken(sigToken: string, reason: string) {
  const artifactRef = activeArtifactVersionRef.value;
  if (!artifactRef) return;
  signingBusy.value = true;
  errorMessage.value = "";
  feedbackMessage.value = "";
  try {
    const attestation = await kernelGovernanceApi.reviewArtifactVersion({
      artifactVersionRef: artifactRef,
      meaning: "Reviewed",
      expectedContentHash: activeArtifactContentHash.value,
      sigToken,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    reviewedAttestationRef.value = attestation.attestation_ref;
    seedNodeSummary(attestation.attestation_ref, {
      kind: "attestation",
      title: "Reviewed attestation",
    });
    feedbackMessage.value =
      "Signed 21 CFR Part 11 Reviewed attestation on simulation report.";
    canvasStore.notifyProtocolGovernanceChanged();
    return attestation;
  } finally {
    signingBusy.value = false;
  }
}
</script>

<style scoped>
.simulation-gov {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 8px;
  padding-top: 10px;
  border-top: 1px solid var(--border);
}

.simulation-gov-note {
  margin: 0;
  font-size: 0.8rem;
  color: var(--neutral-dark);
}

.simulation-gov-status {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--neutral-light);
  font-size: 0.82rem;
}

.simulation-gov-status-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.simulation-gov-signed {
  color: var(--semantic-color-success-dark, var(--semantic-color-success));
  font-weight: 600;
}

.simulation-gov-banner {
  padding: 8px 10px;
  border-radius: 6px;
  font-size: 0.82rem;
}

.simulation-gov-banner--success {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.simulation-gov-banner--error {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error);
}

.simulation-gov-reason-field {
  margin: 0;
}

.simulation-gov-buttons {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.simulation-gov-btn {
  width: 100%;
  justify-content: center;
}
</style>
