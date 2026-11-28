<template>
  <aside
    id="training-attestation-panel"
    class="attestation-card"
    aria-labelledby="training-attestation-heading"
  >
    <div class="attestation-header">
      <div class="attestation-title-row">
        <span class="attestation-icon" aria-hidden="true">
          <ClinicalIcon name="shield-check" :size="18" />
        </span>
        <h4 id="training-attestation-heading" class="attestation-title">
          21 CFR Part 11 Qualification Attestation
        </h4>
      </div>
      <p class="attestation-subtitle">
        Files signed certificate to eTMF
        <code class="mono-ref">05.02.04</code> &amp; issues bitemporal Kernel
        Qualification.
      </p>
    </div>

    <div class="capability-unlock-box">
      <span class="capability-unlock-label">
        Unlocked Capabilities Post-Signoff
      </span>
      <div class="capability-chip-list">
        <span class="capability-chip">Participant Screening</span>
        <span class="capability-chip">Case Report Form Entry</span>
        <span class="capability-chip">Form Signature</span>
      </div>
    </div>

    <div
      v-if="isCompleted"
      data-testid="training-completion-manifest"
      class="completion-manifest"
      role="status"
    >
      <div class="completion-manifest-lead">
        <ClinicalIcon name="check-circle" :size="18" />
        <strong>Qualification Attested &amp; Filed (TMF 05.02.04)</strong>
      </div>
      <dl class="completion-meta-list">
        <div v-if="resolvedEtmfDocId" class="completion-meta-row">
          <dt>eTMF 05.02.04 Document</dt>
          <dd class="mono-ref">{{ resolvedEtmfDocId }}</dd>
        </div>
        <div v-if="resolvedQualificationRef" class="completion-meta-row">
          <dt>Kernel Qualification</dt>
          <dd>
            <NodeRefChip
              v-if="normalizeLookupNodeRef(resolvedQualificationRef)"
              :node-ref="normalizeLookupNodeRef(resolvedQualificationRef)"
            />
            <code v-else class="mono-ref">{{ resolvedQualificationRef }}</code>
          </dd>
        </div>
        <div v-if="resolvedArtifactRef" class="completion-meta-row">
          <dt>Training Record</dt>
          <dd>
            <NodeRefChip
              v-if="normalizeLookupNodeRef(resolvedArtifactRef)"
              :node-ref="normalizeLookupNodeRef(resolvedArtifactRef)"
            />
            <code v-else class="mono-ref">{{ resolvedArtifactRef }}</code>
          </dd>
        </div>
      </dl>
      <SupportDetails
        v-if="completionResult?.sha256_seal"
        summary-label="Part 11 SHA-256 certificate seal"
        :reference="completionResult.sha256_seal"
      />
    </div>

    <p
      v-else-if="!activeAssignment"
      data-testid="training-no-personal-assignment"
      class="attestation-idle-note"
      role="note"
    >
      Assign this curriculum to your account via
      <strong>Batch Assignments (Kernel Site Grants)</strong> to record a
      personal 21 CFR Part 11 qualification signature.
    </p>

    <form
      v-else
      id="training-attestation-form"
      class="attestation-form"
      @submit.prevent="emit('submit')"
    >
      <div class="form-group">
        <label for="training-attestation-meaning">Signing Meaning</label>
        <select
          id="training-attestation-meaning"
          :value="meaning"
          class="select-input control-touch"
          :disabled="submittingCompletion"
          @change="emit('update:meaning', $event.target.value)"
        >
          <option
            value="I have read, understood, and completed protocol training"
          >
            I have read, understood, and completed protocol training
          </option>
          <option
            value="I attest qualification for delegated study duties under ICH GCP E6(R2)"
          >
            I attest qualification for delegated study duties under ICH GCP
            E6(R2)
          </option>
        </select>
      </div>

      <div class="form-group">
        <label for="training-attestation-reason">Reason for Change</label>
        <input
          id="training-attestation-reason"
          :value="reasonForChange"
          type="text"
          class="text-input control-touch"
          :disabled="submittingCompletion"
          placeholder="Initial v2.0 amendment qualification"
          @input="emit('update:reasonForChange', $event.target.value)"
        />
      </div>

      <div class="form-group">
        <label for="training-attestation-credential">
          Step-Up Credential / OIDC Re-authentication
        </label>
        <input
          id="training-attestation-credential"
          :value="signerCredential"
          type="password"
          autocomplete="current-password"
          class="text-input control-touch"
          :disabled="submittingCompletion"
          placeholder="Re-enter account credential"
          @input="emit('update:signerCredential', $event.target.value)"
        />
      </div>

      <p
        v-if="!quizReady"
        data-testid="training-quiz-incomplete-hint"
        class="quiz-gate-hint tabular-nums"
      >
        Answer all {{ totalQuestions }} verification quiz
        {{ totalQuestions === 1 ? "question" : "questions" }} before signing.
      </p>

      <p
        v-if="completionError"
        data-testid="training-completion-error"
        class="badge badge-error"
        role="alert"
      >
        {{ completionError }}
      </p>

      <button
        id="btn-complete-training-assignment"
        type="submit"
        class="btn btn-primary btn-attest-submit"
        :disabled="
          submittingCompletion ||
          !quizReady ||
          !meaning.trim() ||
          !reasonForChange.trim() ||
          !signerCredential
        "
      >
        <ClinicalIcon name="shield-check" :size="16" />
        <span>
          {{
            submittingCompletion
              ? "Signing & Filing TMF 05.02.04..."
              : "Sign & File TMF 05.02.04 Certificate"
          }}
        </span>
      </button>

      <p class="attestation-legal-note">
        21 CFR § 11.100: Electronic signature is legally equivalent to your
        handwritten signature.
      </p>
    </form>
  </aside>
</template>

<script setup>
import { computed } from "vue";
import { ClinicalIcon } from "ui";
import { normalizeLookupNodeRef } from "../../../canvas/nodeResolver";
import NodeRefChip from "../../canvas/NodeRefChip.vue";
import SupportDetails from "../../canvas/SupportDetails.vue";

const props = defineProps({
  activeAssignment: {
    type: Object,
    default: null,
  },
  completionResult: {
    type: Object,
    default: null,
  },
  completionError: {
    type: String,
    default: "",
  },
  submittingCompletion: {
    type: Boolean,
    default: false,
  },
  quizReady: {
    type: Boolean,
    default: true,
  },
  totalQuestions: {
    type: Number,
    default: 0,
  },
  meaning: {
    type: String,
    default: "I have read, understood, and completed protocol training",
  },
  reasonForChange: {
    type: String,
    default: "",
  },
  signerCredential: {
    type: String,
    default: "",
  },
});

const emit = defineEmits([
  "update:meaning",
  "update:reasonForChange",
  "update:signerCredential",
  "submit",
]);

const isCompleted = computed(
  () =>
    props.activeAssignment?.status === "COMPLETED" ||
    Boolean(props.completionResult)
);

const resolvedEtmfDocId = computed(
  () =>
    props.completionResult?.etmf_document_id ||
    props.activeAssignment?.etmf_document_id ||
    ""
);

const resolvedQualificationRef = computed(
  () =>
    props.completionResult?.kernel_qualification_ref ||
    props.activeAssignment?.kernel_qualification_ref ||
    ""
);

const resolvedArtifactRef = computed(
  () =>
    props.completionResult?.kernel_artifact_version_ref ||
    props.activeAssignment?.kernel_artifact_version_ref ||
    ""
);
</script>

<style scoped src="../protocolTrainingConsole.css"></style>
