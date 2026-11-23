<template>
  <section
    id="protocol-versions-section"
    class="versions"
    aria-labelledby="protocol-versions-title"
    :aria-busy="loading"
  >
    <h2 id="protocol-versions-title" tabindex="-1">Protocol versions</h2>
    <div
      v-if="error"
      id="protocol-versions-status"
      class="status-line"
      role="alert"
    >
      <p class="status-line">Protocol versions couldn't be loaded right now.</p>
      <SupportDetails :detail="error" />
    </div>
    <p v-else id="protocol-versions-status" class="status-line" role="status">
      {{ statusLine }}
    </p>

    <div v-if="versions.length" class="scrollable-table-wrapper">
      <table class="versions-table">
        <thead>
          <tr>
            <th scope="col" class="numeric">Version</th>
            <th scope="col">Status</th>
            <th scope="col">Amends</th>
            <th scope="col">Amendment intent</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(version, index) in versions"
            :key="version.ref ?? `masked-${index}`"
            :class="{
              selected:
                version.ref !== null && version.ref === activeSummaryVersionRef,
            }"
          >
            <template v-if="version.ref !== null">
              <th scope="row" class="numeric">
                <button
                  :id="`protocol-version-${version.number}`"
                  type="button"
                  class="row-select"
                  :aria-pressed="version.ref === activeSummaryVersionRef"
                  @click="emit('select', version)"
                >
                  {{ version.number }}
                </button>
              </th>
              <td>{{ version.status }}</td>
              <td>{{ parentNumber(version) }}</td>
              <td>{{ version.amendment_intent || "None recorded" }}</td>
            </template>
            <td v-else colspan="4" class="masked-row">
              A version you may not see ({{ maskWords(version.mask) }})
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div
      v-if="governanceActionMessage"
      id="protocol-governance-feedback"
      class="gov-banner gov-banner-success"
      role="status"
    >
      {{ governanceActionMessage }}
    </div>
    <div
      v-if="governanceErrorMessage"
      id="protocol-governance-error"
      class="gov-banner gov-banner-error"
      role="alert"
    >
      {{ governanceErrorMessage }}
    </div>

    <section
      v-if="showGovernanceControls && activeVersion"
      id="protocol-governance-card"
      class="gov-card"
      aria-labelledby="protocol-governance-heading"
    >
      <div class="gov-card-header">
        <div>
          <h3 id="protocol-governance-heading">
            Version {{ activeVersion.number }} governance
          </h3>
          <p class="gov-subtitle">
            Status:
            <strong id="protocol-active-status">{{
              activeVersion.status
            }}</strong>
            <template v-if="activeVersion.content_hash">
              · Frozen content hash:
              <code id="protocol-content-hash">{{
                activeVersion.content_hash
              }}</code>
            </template>
          </p>
        </div>
        <router-link
          v-if="
            studyCode &&
            (activeVersion.status === 'Approved' || hasEffectiveProtocol)
          "
          id="btn-open-start-site-activation"
          class="btn btn-secondary"
          :to="{
            name: 'study',
            params: { studyCode },
            query: { stage: 'start', section: 'site-activation' },
          }"
        >
          Start site activation
        </router-link>
      </div>

      <div
        v-if="
          activeVersion.status === 'Draft' ||
          activeVersion.status === 'InReview' ||
          activeVersion.status === 'Approved'
        "
        id="protocol-lifecycle-controls"
        class="gov-controls"
      >
        <p
          v-if="deniedGovernanceDecision?.reason"
          id="protocol-governance-denied"
          class="gov-denied-note"
          role="status"
        >
          {{ deniedGovernanceDecision.reason }}
        </p>
        <div
          v-if="
            activeVersion.status === 'Draft' ||
            activeVersion.status === 'InReview'
          "
          class="gov-field"
        >
          <label for="protocol-governance-reason" class="gov-label">
            Reason for change (required for review or return to draft)
          </label>
          <input
            id="protocol-governance-reason"
            v-model="lifecycleReason"
            type="text"
            class="gov-input"
            maxlength="500"
            placeholder="e.g. Freeze protocol design v1 for sponsor clinical and QA review"
            :disabled="
              governanceBusy ||
              (activeVersion.status === 'Draft'
                ? !isSubmitForReviewPermitted
                : !isReturnToDraftPermitted)
            "
          />
        </div>

        <div class="gov-actions">
          <button
            v-if="activeVersion.status === 'Draft'"
            id="btn-submit-protocol-review"
            type="button"
            class="btn btn-primary"
            :disabled="
              governanceBusy ||
              !lifecycleReason.trim() ||
              !isSubmitForReviewPermitted
            "
            @click="submitForReview"
          >
            {{ governanceBusy ? "Submitting…" : "Submit for review" }}
          </button>

          <template v-if="activeVersion.status === 'InReview'">
            <button
              id="btn-approve-protocol-version"
              type="button"
              class="btn btn-primary"
              :disabled="governanceBusy || !isApprovePermitted"
              @click="openApprovalSignature(activeVersion)"
            >
              Sign &amp; approve protocol (Part 11)
            </button>
            <button
              id="btn-return-protocol-draft"
              type="button"
              class="btn btn-secondary"
              :disabled="
                governanceBusy ||
                !lifecycleReason.trim() ||
                !isReturnToDraftPermitted
              "
              @click="returnToDraft"
            >
              Return to draft
            </button>
          </template>

          <button
            v-if="
              activeVersion.status === 'InReview' ||
              activeVersion.status === 'Approved'
            "
            id="btn-withdraw-protocol-version"
            type="button"
            class="btn btn-secondary"
            :disabled="governanceBusy || !isWithdrawPermitted"
            @click="openWithdrawSignature(activeVersion)"
          >
            Sign &amp; withdraw protocol (Part 11)
          </button>

          <button
            v-if="deniedGovernanceDecision"
            id="btn-explain-protocol-governance"
            type="button"
            class="btn btn-secondary"
            :aria-expanded="showGovernanceExplain ? 'true' : 'false'"
            @click="showGovernanceExplain = !showGovernanceExplain"
          >
            {{ showGovernanceExplain ? "Hide explanation" : "Explain" }}
          </button>
        </div>

        <div
          v-if="showGovernanceExplain && deniedGovernanceDecision?.explanation"
          id="protocol-governance-explanation"
          class="gov-explanation-box"
        >
          <KernelExplanation
            :explanation="deniedGovernanceDecision.explanation"
          />
        </div>
      </div>
    </section>

    <section
      v-if="showGovernanceControls && canGovern"
      id="protocol-artifacts-card"
      class="gov-card"
      aria-labelledby="protocol-artifacts-heading"
    >
      <div class="gov-card-header">
        <div>
          <h3 id="protocol-artifacts-heading">
            Approval evidence artifacts (K-TIME-5)
          </h3>
          <p class="gov-subtitle">
            Register IRB/ethics, regulatory authority, or CTA acknowledgement
            artifacts required before transitioning a protocol version to
            <strong>Effective</strong>.
          </p>
        </div>
      </div>

      <ul v-if="artifacts.length" id="protocol-artifacts-list" class="gov-list">
        <li
          v-for="art in artifacts"
          :key="art.artifact_version_ref"
          class="gov-list-item"
        >
          <div>
            <strong>{{ art.title }}</strong>
            <span class="gov-pill">{{ art.kind }}</span>
            <span class="gov-meta">
              v{{ art.version_number }} · <code>{{ art.content_hash }}</code>
            </span>
          </div>
          <code class="gov-ref">{{ art.artifact_version_ref }}</code>
        </li>
      </ul>
      <p v-else class="gov-empty">
        No approval evidence artifacts registered for {{ studyCode }} yet.
      </p>

      <form
        id="protocol-artifact-form"
        class="gov-form"
        @submit.prevent="registerArtifact"
      >
        <div class="gov-grid">
          <div class="gov-field">
            <label for="protocol-artifact-kind" class="gov-label">
              Evidence kind
            </label>
            <select
              id="protocol-artifact-kind"
              v-model="artifactForm.kind"
              class="gov-select"
              :disabled="governanceBusy"
            >
              <option value="EthicsApproval">
                Ethics / IRB approval (EthicsApproval)
              </option>
              <option value="RegulatoryApproval">
                Regulatory approval (RegulatoryApproval)
              </option>
              <option value="CtaAcknowledgement">
                CTA acknowledgement (CtaAcknowledgement)
              </option>
            </select>
          </div>

          <div class="gov-field">
            <label for="protocol-artifact-title" class="gov-label">
              Document title
            </label>
            <input
              id="protocol-artifact-title"
              v-model="artifactForm.title"
              type="text"
              class="gov-input"
              maxlength="500"
              placeholder="e.g. Central IRB Approval Letter v1.0"
              :disabled="governanceBusy"
            />
          </div>
        </div>

        <div class="gov-grid">
          <div class="gov-field">
            <label for="protocol-artifact-file" class="gov-label">
              Compute SHA-256 from file (optional)
            </label>
            <input
              id="protocol-artifact-file"
              type="file"
              class="gov-file-input"
              :disabled="governanceBusy"
              @change="onArtifactFileSelected"
            />
          </div>

          <div class="gov-field">
            <label for="protocol-artifact-hash" class="gov-label">
              Content SHA-256 digest
            </label>
            <input
              id="protocol-artifact-hash"
              v-model="artifactForm.contentHash"
              type="text"
              class="gov-input"
              placeholder="sha256:<64 lower-case hex characters>"
              :disabled="governanceBusy"
            />
          </div>
        </div>

        <div class="gov-grid">
          <div class="gov-field">
            <label for="protocol-artifact-storage" class="gov-label">
              Storage reference
            </label>
            <input
              id="protocol-artifact-storage"
              v-model="artifactForm.storageRef"
              type="text"
              class="gov-input"
              placeholder="e.g. s3://cadence-etmf/irb-approval-v1.pdf"
              :disabled="governanceBusy"
            />
          </div>

          <div class="gov-field">
            <label for="protocol-artifact-reason" class="gov-label">
              Reason for registration
            </label>
            <input
              id="protocol-artifact-reason"
              v-model="artifactForm.reason"
              type="text"
              class="gov-input"
              maxlength="500"
              placeholder="e.g. Record central IRB approval for protocol go-live"
              :disabled="governanceBusy"
            />
          </div>
        </div>

        <div class="gov-actions">
          <button
            id="btn-register-artifact"
            type="submit"
            class="btn btn-secondary"
            :disabled="!canSubmitArtifact || governanceBusy"
          >
            {{ governanceBusy ? "Registering…" : "Register evidence artifact" }}
          </button>
        </div>
      </form>
    </section>

    <section
      v-if="showGovernanceControls"
      id="protocol-effectivity-card"
      class="gov-card"
      aria-labelledby="protocol-effectivity-heading"
    >
      <div class="gov-card-header">
        <div>
          <h3 id="protocol-effectivity-heading">Protocol effectivity</h3>
          <p class="gov-subtitle">
            Declare scope effectivity and transition an approved protocol
            version to <strong>Effective</strong> with approval evidence so
            sites can open casebook forms.
          </p>
        </div>
      </div>

      <ul
        v-if="effectivities.length"
        id="protocol-effectivities-list"
        class="gov-list"
      >
        <li
          v-for="eff in effectivities"
          :key="eff.effectivity_ref"
          class="gov-effectivity-item"
        >
          <div class="gov-effectivity-summary">
            <strong>
              Version {{ effectivityVersionLabel(eff) }} ·
              {{ formatEffectivityScope(eff) }}
            </strong>
            <span
              class="gov-pill"
              :class="{ 'gov-pill-effective': eff.status === 'Effective' }"
            >
              {{ eff.status }}
            </span>
          </div>

          <div
            v-if="
              canGovern &&
              eff.status !== 'Effective' &&
              eff.status !== 'Withdrawn'
            "
            class="gov-transition-box"
          >
            <div class="gov-grid">
              <div class="gov-field">
                <label
                  :for="`effectivity-target-status-${eff.effectivity_id}`"
                  class="gov-label"
                >
                  Target status
                </label>
                <select
                  :id="`effectivity-target-status-${eff.effectivity_id}`"
                  v-model="targetStatusByRef[eff.effectivity_ref]"
                  class="gov-select"
                  :disabled="governanceBusy"
                >
                  <option value="Effective">Effective</option>
                  <option value="EthicsApproved">EthicsApproved</option>
                  <option value="RegulatoryApproved">RegulatoryApproved</option>
                  <option value="Withdrawn">Withdrawn</option>
                </select>
              </div>

              <div class="gov-field">
                <label
                  :for="`effectivity-reason-${eff.effectivity_id}`"
                  class="gov-label"
                >
                  Reason for status transition
                </label>
                <input
                  :id="`effectivity-reason-${eff.effectivity_id}`"
                  v-model="transitionReasonByRef[eff.effectivity_ref]"
                  type="text"
                  class="gov-input"
                  maxlength="500"
                  placeholder="e.g. Mark protocol effective following IRB approval"
                  :disabled="governanceBusy"
                />
              </div>
            </div>

            <fieldset
              v-if="approvalArtifacts.length"
              class="gov-evidence-fieldset"
            >
              <legend class="gov-label">
                Supporting approval evidence (select at least one for Effective)
              </legend>
              <label
                v-for="art in approvalArtifacts"
                :key="`${eff.effectivity_ref}-${art.artifact_version_ref}`"
                class="gov-checkbox-row"
              >
                <input
                  type="checkbox"
                  :value="art.artifact_version_ref"
                  :checked="
                    isEvidenceSelected(
                      eff.effectivity_ref,
                      art.artifact_version_ref
                    )
                  "
                  :disabled="governanceBusy"
                  @change="
                    toggleEvidence(
                      eff.effectivity_ref,
                      art.artifact_version_ref
                    )
                  "
                />
                <span>{{ art.title }} ({{ art.kind }})</span>
              </label>
            </fieldset>
            <p v-else class="gov-empty">
              Register an ethics, regulatory, or CTA artifact above before
              marking this scope Effective.
            </p>

            <div class="gov-actions">
              <button
                :id="`btn-transition-effectivity-${eff.effectivity_id}`"
                data-testid="btn-transition-effectivity"
                type="button"
                class="btn btn-primary"
                :disabled="!canTransitionEffectivity(eff) || governanceBusy"
                @click="transitionEffectivity(eff)"
              >
                {{
                  governanceBusy
                    ? "Updating…"
                    : `Mark ${targetStatusByRef[eff.effectivity_ref] || "Effective"}`
                }}
              </button>
            </div>
          </div>
        </li>
      </ul>
      <p v-else class="gov-empty">
        No protocol effectivities declared for {{ studyCode }} yet.
      </p>

      <form
        v-if="canGovern && activeVersion && activeVersion.ref"
        id="protocol-declare-effectivity-form"
        class="gov-form"
        @submit.prevent="declareEffectivity"
      >
        <div class="gov-grid">
          <div class="gov-field">
            <label for="effectivity-scope-level" class="gov-label">
              Scope for Version {{ activeVersion.number }}
            </label>
            <select
              id="effectivity-scope-level"
              v-model="declareForm.scopeLevel"
              class="gov-select"
              :disabled="governanceBusy"
            >
              <option value="Study">Whole study (Study)</option>
              <option value="Country">Country (ISO alpha-2)</option>
              <option value="StudySite">Single study site</option>
            </select>
          </div>

          <div v-if="declareForm.scopeLevel === 'Country'" class="gov-field">
            <label for="effectivity-country" class="gov-label">
              Country code (ISO 3166-1 alpha-2)
            </label>
            <input
              id="effectivity-country"
              v-model="declareForm.country"
              type="text"
              class="gov-input"
              maxlength="2"
              placeholder="US"
              :disabled="governanceBusy"
            />
          </div>

          <div v-if="declareForm.scopeLevel === 'StudySite'" class="gov-field">
            <label for="effectivity-study-site" class="gov-label">
              Study site
            </label>
            <select
              v-if="studySites.length"
              id="effectivity-study-site"
              v-model="declareForm.studySiteRef"
              class="gov-select"
              :disabled="governanceBusy"
            >
              <option value="">Select a linked study site</option>
              <option
                v-for="site in studySites"
                :key="site.study_site_ref"
                :value="site.study_site_ref"
              >
                {{ site.site_code || site.study_site_ref }} ({{ site.status }})
              </option>
            </select>
            <input
              v-else
              id="effectivity-study-site"
              v-model="declareForm.studySiteRef"
              type="text"
              class="gov-input"
              placeholder="study_site:<uuid>"
              :disabled="governanceBusy"
            />
          </div>

          <div class="gov-field">
            <label for="effectivity-declare-reason" class="gov-label">
              Reason for declaration
            </label>
            <input
              id="effectivity-declare-reason"
              v-model="declareForm.reason"
              type="text"
              class="gov-input"
              maxlength="500"
              placeholder="e.g. Declare study-wide effectivity for approved v1"
              :disabled="governanceBusy"
            />
          </div>
        </div>

        <div class="gov-actions">
          <button
            id="btn-declare-effectivity"
            type="submit"
            class="btn btn-secondary"
            :disabled="!canDeclareEffectivity || governanceBusy"
          >
            {{
              governanceBusy
                ? "Declaring…"
                : `Declare effectivity for Version ${activeVersion.number}`
            }}
          </button>
        </div>
      </form>
    </section>

    <ProtocolImportPanel
      v-if="canShowImportPanel"
      :study-ref="studyNodeRef"
      :study-code="studyCode"
      :selected-version-ref="activeSummaryVersionRef"
      :disabled="importDisabled"
      :disabled-reason="importDisabledReason"
      :explanation="importCanI.decision.value?.explanation ?? null"
      @imported="onDraftImported"
      @govern="onGovernVersion"
    />

    <SignatureCaptureModal
      v-if="approvingVersion && approvingVersion.ref"
      :is-open="Boolean(approvingVersion)"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Sponsor Designer"
      :reasons="PROTOCOL_APPROVE_REASONS"
      :action-url="protocolVersionApproveAction(approvingVersion.ref)"
      :semantic-action="PROTOCOL_APPROVE_SEMANTIC_ACTION"
      :on-sign="approveProtocolWithToken"
      @cancel="approvingVersion = null"
      @success="approvingVersion = null"
    />

    <SignatureCaptureModal
      v-if="withdrawingVersion && withdrawingVersion.ref"
      :is-open="Boolean(withdrawingVersion)"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Sponsor Designer"
      :reasons="PROTOCOL_WITHDRAW_REASONS"
      :action-url="resolveWithdrawActionUrl(withdrawingVersion.ref)"
      :semantic-action="withdrawSemanticAction"
      :on-sign="withdrawProtocolWithToken"
      @cancel="withdrawingVersion = null"
      @success="withdrawingVersion = null"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { handoffFocus } from "ui";
import {
  describeKernelError,
  isMasked,
  kernelApi,
  type KernelProtocolDraftSummary,
  type KernelProtocolVersion,
} from "../../api/kernelApi";
import {
  kernelGovernanceApi,
  PROTOCOL_APPROVE_SEMANTIC_ACTION,
  protocolVersionApproveAction,
  type KernelArtifact,
  type KernelEffectivity,
  type KernelStudySite,
} from "../../api/kernelGovernanceApi";
import { canGovernProtocols } from "../../canvas/authority";
import { clearKernelStudyCache } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";
import { maskReasonWords, seedNodeSummary } from "../../canvas/nodeResolver";
import { useKernelCanI } from "../../composables/useKernelCanI";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import { useAuthStore } from "../../stores/auth";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import KernelExplanation from "./KernelExplanation.vue";
import ProtocolImportPanel from "./ProtocolImportPanel.vue";
import SupportDetails from "./SupportDetails.vue";

/**
 * The study's protocol versions, newest first (ADR-2540), together with review, 21 CFR Part 11 signed approval,
 * evidence artifact registration, and bitemporal effectivity governance controls.
 */
const props = defineProps<{ studyCode: string; selectedRef: string }>();
const emit = defineEmits<{ select: [version: KernelProtocolVersion] }>();

const APPROVAL_EVIDENCE_KINDS = new Set([
  "EthicsApproval",
  "RegulatoryApproval",
  "CtaAcknowledgement",
]);

const PROTOCOL_APPROVE_REASONS = [
  "Approve frozen protocol version for clinical conduct",
  "APPROVAL",
  "CLINICAL_QC",
];

const PROTOCOL_WITHDRAW_REASONS = [
  "Withdraw superseded or erroneous protocol version",
  "PROTOCOL_WITHDRAWAL",
  "CLINICAL_QC",
];

const authStore = useAuthStore();
const canvasStore = useStudyCanvasStore();

const versions = ref<KernelProtocolVersion[]>([]);
const artifacts = ref<KernelArtifact[]>([]);
const effectivities = ref<KernelEffectivity[]>([]);
const studySites = ref<KernelStudySite[]>([]);

const governanceBusy = ref(false);
const governanceActionMessage = ref("");
const governanceErrorMessage = ref("");
const lifecycleReason = ref("");
const approvingVersion = ref<KernelProtocolVersion | null>(null);
const withdrawingVersion = ref<KernelProtocolVersion | null>(null);
const showGovernanceExplain = ref(false);

const importCanI = useKernelCanI();
const submitCanI = useKernelCanI();
const returnCanI = useKernelCanI();
const approveCanI = useKernelCanI();
const withdrawCanI = useKernelCanI();
const artifactCanI = useKernelCanI();
const declareEffectivityCanI = useKernelCanI();
const changeEffectivityCanI = useKernelCanI();

const withdrawSemanticAction =
  (
    kernelGovernanceApi as {
      PROTOCOL_WITHDRAW_SEMANTIC_ACTION?: string;
    }
  ).PROTOCOL_WITHDRAW_SEMANTIC_ACTION ?? "kernel.protocol_version.withdraw";

function resolveWithdrawActionUrl(versionRef: string): string {
  const helper = (
    kernelGovernanceApi as {
      protocolVersionWithdrawAction?: (ref: string) => string;
    }
  ).protocolVersionWithdrawAction;
  if (typeof helper === "function") {
    return helper(versionRef);
  }
  const at = versionRef.indexOf("@");
  const clean = at < 0 ? versionRef : versionRef.slice(0, at);
  return `/api/v1/kernel/protocol-versions/${clean}/withdraw`;
}

function seedVersions(listed: KernelProtocolVersion[]) {
  versions.value = listed;
  for (const v of listed) {
    if (v.ref) {
      seedNodeSummary(v.ref, {
        kind: "protocol_version",
        title: `Version ${v.number}`,
        status: v.status,
      });
    }
  }
}

const {
  studyRef,
  studyLoading: loading,
  studyError: error,
  noStudy,
} = useKernelStudyRef(() => props.studyCode, {
  onReset: () => {
    versions.value = [];
    artifacts.value = [];
    effectivities.value = [];
    studySites.value = [];
    governanceActionMessage.value = "";
    governanceErrorMessage.value = "";
  },
  onResolved: async (study, { isCurrent }) => {
    const listed = await kernelApi.listProtocolVersions(study);
    if (isCurrent()) {
      seedVersions(listed);
      void refreshGovernanceLists(study, isCurrent);
    }
  },
});

const studyNodeRef = computed(() => studyRef.value ?? "");

const artifactForm = reactive({
  kind: "EthicsApproval",
  title: "",
  contentHash: "",
  sizeBytes: 1,
  mediaType: "application/pdf",
  storageRef: "",
  reason: "",
});

const declareForm = reactive({
  scopeLevel: "Study" as "Study" | "Country" | "StudySite",
  country: "US",
  studySiteRef: "",
  reason: "",
});

const targetStatusByRef = reactive<Record<string, string>>({});
const transitionReasonByRef = reactive<Record<string, string>>({});
const selectedEvidenceByRef = reactive<Record<string, string[]>>({});

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

const showGovernanceControls = computed(
  () =>
    !loading.value &&
    !error.value &&
    !noStudy.value &&
    Boolean(studyNodeRef.value) &&
    typeof kernelGovernanceApi?.submitProtocolVersionForReview === "function"
);

const canShowImportPanel = computed(
  () =>
    !loading.value &&
    !error.value &&
    !noStudy.value &&
    Boolean(studyNodeRef.value) &&
    typeof kernelApi.importProtocolDraft === "function"
);

const activeSummaryVersionRef = computed(() => {
  if (props.selectedRef) return props.selectedRef;
  const firstVisible = versions.value.find((v) => v.ref && !isMasked(v));
  return firstVisible?.ref ?? "";
});

const activeVersion = computed<KernelProtocolVersion | null>(() => {
  const targetRef = activeSummaryVersionRef.value;
  if (!targetRef) return null;
  const targetId = parseNodeRef(targetRef)?.id;
  return (
    versions.value.find((v) => {
      if (!v.ref || isMasked(v)) return false;
      if (v.ref === targetRef) return true;
      return Boolean(targetId && parseNodeRef(v.ref)?.id === targetId);
    }) ?? null
  );
});

const latestNonWithdrawnVersion = computed<KernelProtocolVersion | null>(
  () =>
    versions.value.find(
      (v) => v.ref && !isMasked(v) && v.status !== "Withdrawn"
    ) ?? null
);

const isLatestVersionInReview = computed(
  () =>
    latestNonWithdrawnVersion.value?.status === "InReview" ||
    activeVersion.value?.status === "InReview"
);

const isImportPermittedByKernel = computed(() =>
  importCanI.decision.value
    ? Boolean(importCanI.decision.value.permitted)
    : true
);

const importDisabled = computed(
  () => isLatestVersionInReview.value || !isImportPermittedByKernel.value
);

const importDisabledReason = computed(() => {
  if (isLatestVersionInReview.value) {
    const num =
      activeVersion.value?.status === "InReview"
        ? activeVersion.value.number
        : latestNonWithdrawnVersion.value?.number;
    return `Version ${num ?? ""} is frozen in review (InReview). Return it to Draft or approve it before importing protocol changes.`.replace(
      /\s+/g,
      " "
    );
  }
  if (!isImportPermittedByKernel.value) {
    return (
      importCanI.decision.value?.reason ||
      "Your account does not hold authorization to import protocol drafts in this study."
    );
  }
  return "";
});

const isSubmitForReviewPermitted = computed(() =>
  submitCanI.decision.value
    ? Boolean(submitCanI.decision.value.permitted)
    : canGovern.value
);
const isReturnToDraftPermitted = computed(() =>
  returnCanI.decision.value
    ? Boolean(returnCanI.decision.value.permitted)
    : canGovern.value
);
const isApprovePermitted = computed(() =>
  approveCanI.decision.value
    ? Boolean(approveCanI.decision.value.permitted)
    : canGovern.value
);
const isWithdrawPermitted = computed(() =>
  withdrawCanI.decision.value
    ? Boolean(withdrawCanI.decision.value.permitted)
    : canGovern.value
);
const isArtifactPermitted = computed(() =>
  artifactCanI.decision.value
    ? Boolean(artifactCanI.decision.value.permitted)
    : canGovern.value
);
const isDeclareEffectivityPermitted = computed(() =>
  declareEffectivityCanI.decision.value
    ? Boolean(declareEffectivityCanI.decision.value.permitted)
    : canGovern.value
);
const isChangeEffectivityPermitted = computed(() =>
  changeEffectivityCanI.decision.value
    ? Boolean(changeEffectivityCanI.decision.value.permitted)
    : canGovern.value
);

const hasEffectiveProtocol = computed(() =>
  effectivities.value.some((eff) => eff.status === "Effective")
);

const deniedGovernanceDecision = computed(() => {
  for (const probeState of [
    submitCanI,
    approveCanI,
    returnCanI,
    withdrawCanI,
  ]) {
    if (probeState.decision.value && !probeState.decision.value.permitted) {
      return probeState.decision.value;
    }
  }
  return null;
});

watch(
  () =>
    [
      studyNodeRef.value,
      latestNonWithdrawnVersion.value?.ref ?? "",
      latestNonWithdrawnVersion.value?.status ?? "",
    ] as const,
  ([study, latestRef, latestStatus]) => {
    importCanI.reset();
    artifactCanI.reset();
    changeEffectivityCanI.reset();
    if (!study) return;
    void artifactCanI.probe("artifact.register", study);
    void changeEffectivityCanI.probe("effectivity.change_status", study);
    if (latestStatus === "Draft" && latestRef) {
      void importCanI.probe("protocol.revise_draft", latestRef);
    } else if (latestStatus === "Approved" && latestRef) {
      void importCanI.probe("protocol.amend", latestRef);
    } else if (latestStatus !== "InReview") {
      void importCanI.probe("protocol.draft", study);
    }
  },
  { immediate: true }
);

watch(
  () =>
    [
      activeVersion.value?.ref ?? "",
      activeVersion.value?.status ?? "",
    ] as const,
  ([verRef, status]) => {
    submitCanI.reset();
    returnCanI.reset();
    approveCanI.reset();
    withdrawCanI.reset();
    declareEffectivityCanI.reset();
    showGovernanceExplain.value = false;
    if (!verRef) return;
    if (status !== "Withdrawn") {
      void declareEffectivityCanI.probe("effectivity.declare", verRef);
    }
    if (status === "Draft") {
      void submitCanI.probe("protocol.submit_for_review", verRef);
    } else if (status === "InReview") {
      void approveCanI.probe("protocol.approve", verRef);
      void returnCanI.probe("protocol.return_to_draft", verRef);
      void withdrawCanI.probe("protocol.withdraw", verRef);
    } else if (status === "Approved") {
      void withdrawCanI.probe("protocol.withdraw", verRef);
    }
  },
  { immediate: true }
);

const approvalArtifacts = computed(() =>
  artifacts.value.filter((a) => APPROVAL_EVIDENCE_KINDS.has(a.kind))
);

const canSubmitArtifact = computed(
  () =>
    isArtifactPermitted.value &&
    Boolean(studyNodeRef.value) &&
    Boolean(artifactForm.title.trim()) &&
    Boolean(artifactForm.contentHash.trim()) &&
    Boolean(artifactForm.reason.trim())
);

const canDeclareEffectivity = computed(() => {
  if (!isDeclareEffectivityPermitted.value) return false;
  if (!activeVersion.value?.ref || !declareForm.reason.trim()) return false;
  if (activeVersion.value.status === "Withdrawn") return false;
  if (declareForm.scopeLevel === "Country") {
    return /^[A-Za-z]{2}$/.test(declareForm.country.trim());
  }
  if (declareForm.scopeLevel === "StudySite") {
    return Boolean(declareForm.studySiteRef.trim());
  }
  return true;
});

/** Visible versions by id: a parent reference may name the version with or without its revision. */
const byId = computed(
  () =>
    new Map(
      versions.value.flatMap((v) => {
        const node = parseNodeRef(v.ref);
        return node ? [[node.id, v] as const] : [];
      })
    )
);

function parentNumber(version: KernelProtocolVersion): string {
  if (!version.parent_version) return "Nothing (first version)";
  const parentId = parseNodeRef(version.parent_version)?.id;
  const parent = parentId ? byId.value.get(parentId) : undefined;
  return parent ? `Version ${parent.number}` : "A version you may not see";
}

function maskWords(mask: string | null): string {
  return maskReasonWords(mask);
}

function effectivityVersionLabel(eff: KernelEffectivity): string | number {
  if (eff.protocol_version_number) return eff.protocol_version_number;
  const verId = parseNodeRef(eff.protocol_version_ref)?.id;
  const found = verId ? byId.value.get(verId) : undefined;
  return found?.number ?? eff.protocol_version_ref;
}

function formatEffectivityScope(eff: KernelEffectivity): string {
  if (eff.scope_level === "Country" && eff.country) {
    return `Country ${eff.country}`;
  }
  if (eff.scope_level === "StudySite" && eff.study_site_ref) {
    const matched = studySites.value.find(
      (s) => s.study_site_ref === eff.study_site_ref
    );
    return matched?.site_code
      ? `Site ${matched.site_code}`
      : eff.study_site_ref;
  }
  return "Whole study";
}

function syncEffectivityDefaults(items: KernelEffectivity[]) {
  const defaultEvidence = approvalArtifacts.value[0]?.artifact_version_ref
    ? [approvalArtifacts.value[0].artifact_version_ref]
    : [];
  for (const eff of items) {
    if (!targetStatusByRef[eff.effectivity_ref]) {
      targetStatusByRef[eff.effectivity_ref] = "Effective";
    }
    if (
      !selectedEvidenceByRef[eff.effectivity_ref] ||
      selectedEvidenceByRef[eff.effectivity_ref].length === 0
    ) {
      selectedEvidenceByRef[eff.effectivity_ref] = [...defaultEvidence];
    }
  }
}

function isEvidenceSelected(
  effectivityRef: string,
  artifactVersionRef: string
): boolean {
  return (selectedEvidenceByRef[effectivityRef] ?? []).includes(
    artifactVersionRef
  );
}

function toggleEvidence(effectivityRef: string, artifactVersionRef: string) {
  const current = new Set(selectedEvidenceByRef[effectivityRef] ?? []);
  if (current.has(artifactVersionRef)) {
    current.delete(artifactVersionRef);
  } else {
    current.add(artifactVersionRef);
  }
  selectedEvidenceByRef[effectivityRef] = [...current];
}

function canTransitionEffectivity(eff: KernelEffectivity): boolean {
  if (!isChangeEffectivityPermitted.value) return false;
  const target = targetStatusByRef[eff.effectivity_ref] || "Effective";
  const why = (transitionReasonByRef[eff.effectivity_ref] ?? "").trim();
  if (!why) return false;
  if (
    target === "Effective" ||
    target === "EthicsApproved" ||
    target === "RegulatoryApproved"
  ) {
    return (selectedEvidenceByRef[eff.effectivity_ref] ?? []).length > 0;
  }
  return true;
}

const statusLine = computed(() => {
  if (loading.value) return "Loading protocol versions...";
  if (noStudy.value) {
    return `Study ${props.studyCode} wasn't found or isn't visible to your account.`;
  }
  const count = versions.value.length;
  if (count === 0) return `${props.studyCode} has no protocol versions yet.`;
  const hidden = versions.value.filter((v) => isMasked(v)).length;
  const noun = count === 1 ? "version" : "versions";
  return hidden
    ? `${count} ${noun}, ${hidden} of them hidden from you.`
    : `${count} ${noun}.`;
});

function nextIdempotencyKey(): string {
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `gov-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

async function refreshGovernanceLists(
  study: string,
  isCurrent: () => boolean = () => studyNodeRef.value === study
) {
  const tasks: Promise<void>[] = [];
  let nextArtifacts: KernelArtifact[] | null = null;
  let nextEffectivities: KernelEffectivity[] | null = null;
  let nextSites: KernelStudySite[] | null = null;
  if (typeof kernelGovernanceApi?.listStudyArtifacts === "function") {
    tasks.push(
      kernelGovernanceApi
        .listStudyArtifacts(study)
        .then((res) => {
          nextArtifacts = Array.isArray(res) ? res : [];
        })
        .catch(() => {
          // Optional governance read: do not fail main protocol version view.
        })
    );
  }
  if (typeof kernelGovernanceApi?.listStudyEffectivities === "function") {
    tasks.push(
      kernelGovernanceApi
        .listStudyEffectivities(study)
        .then((res) => {
          nextEffectivities = Array.isArray(res) ? res : [];
        })
        .catch(() => {
          // Optional governance read.
        })
    );
  }
  if (typeof kernelGovernanceApi?.listStudySites === "function") {
    tasks.push(
      kernelGovernanceApi
        .listStudySites(study)
        .then((res) => {
          nextSites = Array.isArray(res) ? res : [];
        })
        .catch(() => {
          // Optional governance read.
        })
    );
  }
  await Promise.all(tasks);
  if (!isCurrent()) return;
  if (nextArtifacts !== null) artifacts.value = nextArtifacts;
  if (nextEffectivities !== null) effectivities.value = nextEffectivities;
  if (nextSites !== null) studySites.value = nextSites;
  syncEffectivityDefaults(effectivities.value);
}

async function refreshVersions() {
  if (!studyNodeRef.value) return;
  const listed = await kernelApi.listProtocolVersions(studyNodeRef.value);
  seedVersions(listed);
}

async function submitForReview() {
  const target = activeVersion.value;
  const reason = lifecycleReason.value.trim();
  if (!target?.ref || !reason || governanceBusy.value) return;
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const updated = await kernelGovernanceApi.submitProtocolVersionForReview({
      versionRef: target.ref,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    lifecycleReason.value = "";
    await refreshVersions();
    emit("select", updated);
    governanceActionMessage.value = `Submitted Version ${updated.number} for review (${updated.status}).`;
  } catch (err) {
    governanceErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function returnToDraft() {
  const target = activeVersion.value;
  const reason = lifecycleReason.value.trim();
  if (!target?.ref || !reason || governanceBusy.value) return;
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const updated = await kernelGovernanceApi.returnProtocolVersionToDraft({
      versionRef: target.ref,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    lifecycleReason.value = "";
    await refreshVersions();
    emit("select", updated);
    governanceActionMessage.value = `Returned Version ${updated.number} to Draft.`;
  } catch (err) {
    governanceErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

function openApprovalSignature(version: KernelProtocolVersion) {
  governanceErrorMessage.value = "";
  approvingVersion.value = version;
}

function openWithdrawSignature(version: KernelProtocolVersion) {
  governanceErrorMessage.value = "";
  withdrawingVersion.value = version;
}

async function approveProtocolWithToken(sigToken: string, reason: string) {
  const target = approvingVersion.value;
  if (!target?.ref) return;
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const approved = await kernelGovernanceApi.approveProtocolVersion({
      versionRef: target.ref,
      meaning: "Approved",
      expectedContentHash: target.content_hash,
      sigToken,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    clearKernelStudyCache();
    await refreshVersions();
    if (props.studyCode) {
      void canvasStore.loadScheduleOptions(props.studyCode, { force: true });
    }
    emit("select", approved);
    governanceActionMessage.value = `Signed and approved Version ${approved.number} under 21 CFR Part 11.`;
    return approved;
  } finally {
    governanceBusy.value = false;
  }
}

async function withdrawProtocolWithToken(sigToken: string, reason: string) {
  const target = withdrawingVersion.value;
  if (!target?.ref) return;
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const withdrawn = await kernelGovernanceApi.withdrawProtocolVersion({
      versionRef: target.ref,
      meaning: "Withdrawn",
      expectedContentHash: target.content_hash,
      sigToken,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    clearKernelStudyCache();
    await refreshVersions();
    if (props.studyCode) {
      void canvasStore.loadScheduleOptions(props.studyCode, { force: true });
    }
    emit("select", withdrawn);
    governanceActionMessage.value = `Signed and withdrew Version ${withdrawn.number} under 21 CFR Part 11.`;
    return withdrawn;
  } finally {
    governanceBusy.value = false;
  }
}

async function onArtifactFileSelected(event: Event) {
  const input = event.target as HTMLInputElement | null;
  const file = input?.files?.[0];
  if (!file) return;
  if (!artifactForm.title.trim()) {
    artifactForm.title = file.name;
  }
  artifactForm.sizeBytes = Math.max(1, file.size);
  artifactForm.mediaType =
    (file.type || "application/pdf").trim().toLowerCase() || "application/pdf";
  if (!artifactForm.storageRef.trim()) {
    artifactForm.storageRef = `urn:cadence:artifact:${encodeURIComponent(file.name)}`;
  }
  if (typeof globalThis.crypto?.subtle?.digest === "function") {
    const bytes = await file.arrayBuffer();
    const digest = await globalThis.crypto.subtle.digest("SHA-256", bytes);
    const hex = Array.from(new Uint8Array(digest))
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");
    artifactForm.contentHash = `sha256:${hex}`;
  }
}

async function registerArtifact() {
  if (!studyNodeRef.value || !canSubmitArtifact.value || governanceBusy.value) {
    return;
  }
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const key = nextIdempotencyKey();
    const created = await kernelGovernanceApi.registerStudyArtifact({
      studyRef: studyNodeRef.value,
      kind: artifactForm.kind,
      title: artifactForm.title.trim(),
      contentHash: artifactForm.contentHash.trim(),
      sizeBytes: artifactForm.sizeBytes,
      mediaType: artifactForm.mediaType,
      storageRef:
        artifactForm.storageRef.trim() || `urn:cadence:artifact:${key}`,
      reason: artifactForm.reason.trim(),
      idempotencyKey: key,
    });
    artifactForm.title = "";
    artifactForm.contentHash = "";
    artifactForm.storageRef = "";
    artifactForm.reason = "";
    artifactForm.sizeBytes = 1;
    await refreshGovernanceLists(studyNodeRef.value);
    for (const eff of effectivities.value) {
      if (eff.status !== "Effective" && eff.status !== "Withdrawn") {
        const current = new Set(
          selectedEvidenceByRef[eff.effectivity_ref] ?? []
        );
        current.add(created.artifact_version_ref);
        selectedEvidenceByRef[eff.effectivity_ref] = [...current];
      }
    }
    governanceActionMessage.value = `Registered ${created.kind} artifact "${created.title}".`;
  } catch (err) {
    governanceErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function declareEffectivity() {
  const target = activeVersion.value;
  if (!target?.ref || !canDeclareEffectivity.value || governanceBusy.value) {
    return;
  }
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const declared = await kernelGovernanceApi.declareProtocolEffectivity({
      versionRef: target.ref,
      scopeLevel: declareForm.scopeLevel,
      country:
        declareForm.scopeLevel === "Country"
          ? declareForm.country.trim().toUpperCase()
          : null,
      studySiteRef:
        declareForm.scopeLevel === "StudySite"
          ? declareForm.studySiteRef.trim()
          : null,
      reason: declareForm.reason.trim(),
      idempotencyKey: nextIdempotencyKey(),
    });
    declareForm.reason = "";
    if (studyNodeRef.value) {
      await refreshGovernanceLists(studyNodeRef.value);
    } else {
      effectivities.value = [declared, ...effectivities.value];
      syncEffectivityDefaults(effectivities.value);
    }
    governanceActionMessage.value = `Declared ${declared.scope_level} effectivity (${declared.status}) for Version ${target.number}.`;
  } catch (err) {
    governanceErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function transitionEffectivity(eff: KernelEffectivity) {
  if (!canTransitionEffectivity(eff) || governanceBusy.value) return;
  const targetStatus = targetStatusByRef[eff.effectivity_ref] || "Effective";
  const reason = (transitionReasonByRef[eff.effectivity_ref] ?? "").trim();
  const evidenceRefs =
    targetStatus === "Withdrawn"
      ? []
      : (selectedEvidenceByRef[eff.effectivity_ref] ?? []);
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const updated = await kernelGovernanceApi.changeEffectivityStatus({
      effectivityRef: eff.effectivity_ref,
      status: targetStatus,
      evidenceRefs,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    transitionReasonByRef[eff.effectivity_ref] = "";
    clearKernelStudyCache();
    if (studyNodeRef.value) {
      await refreshGovernanceLists(studyNodeRef.value);
    }
    if (props.studyCode) {
      void canvasStore.loadScheduleOptions(props.studyCode, { force: true });
    }
    governanceActionMessage.value = `Effectivity moved to ${updated.status}.`;
  } catch (err) {
    governanceErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function onDraftImported(summary: KernelProtocolDraftSummary) {
  if (studyNodeRef.value) {
    try {
      await refreshVersions();
      await refreshGovernanceLists(studyNodeRef.value);
    } catch {
      // Keep the imported version visible even if refreshing the list fails.
    }
  }
  if (summary.version?.ref) {
    seedNodeSummary(summary.version.ref, {
      kind: "protocol_version",
      title: `Version ${summary.version.number}`,
      status: summary.version.status,
    });
    emit("select", summary.version);
  }
}
function onGovernVersion(version: KernelProtocolVersion) {
  if (version?.ref) {
    emit("select", version);
  }
  void handoffFocus([
    "#protocol-governance-reason",
    "#btn-submit-protocol-review",
    "#protocol-governance-heading",
  ]);
}
</script>

<style scoped>
.versions {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.versions h2 {
  margin: 0;
  font-size: 1rem;
}

.status-line {
  margin: 0;
  color: var(--neutral-dark);
}

.versions-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.versions-table th,
.versions-table td {
  padding: 6px 8px;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.versions-table .numeric {
  text-align: right;
}

.versions-table tr.selected {
  background: var(--neutral-light);
}

.row-select {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  border: none;
  border-radius: 4px;
  background: none;
  padding: 2px 4px;
  font: inherit;
  font-weight: 600;
  color: var(--primary);
  cursor: pointer;
  box-sizing: border-box;
}

.masked-row {
  color: var(--neutral-dark);
  font-style: italic;
}
.gov-banner {
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.9rem;
}

.gov-banner-success {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.gov-banner-error {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error);
}

.gov-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 14px 16px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.gov-card-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-start;
  gap: 10px;
}

.gov-card-header h3 {
  margin: 0 0 4px;
  font-size: 0.95rem;
}

.gov-denied-note {
  margin: 0;
  padding: 8px 10px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-warning-border, var(--border));
  background: var(
    --semantic-color-warning-bg-soft,
    var(--semantic-color-surface-muted)
  );
  color: var(--semantic-color-warning-dark, var(--semantic-color-text));
  font-size: 0.86rem;
}

.gov-subtitle {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.gov-controls,
.gov-form {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.gov-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
  gap: 10px;
}

.gov-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.gov-label {
  font-size: 0.82rem;
  font-weight: 600;
}

.gov-input,
.gov-select {
  padding: 7px 9px;
  border: 1px solid var(--border);
  border-radius: 4px;
  font: inherit;
  font-size: 1rem;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.gov-explanation-box {
  padding: 10px 12px;
  border-radius: 6px;
  border: 1px solid var(--border);
  background: var(--semantic-color-surface-muted);
}

.gov-file-input {
  font-size: 1rem;
}

.gov-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.gov-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.gov-list-item,
.gov-effectivity-item {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  font-size: 0.88rem;
}

.gov-effectivity-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.gov-transition-box {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 8px;
  border-top: 1px solid var(--border);
}

.gov-evidence-fieldset {
  margin: 0;
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: 4px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.gov-checkbox-row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
}

.gov-pill {
  display: inline-block;
  margin-left: 6px;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.76rem;
  font-weight: 600;
  background: var(--semantic-color-surface-header);
}

.gov-pill-effective {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}

.gov-meta,
.gov-ref,
.gov-empty {
  margin: 0;
  font-size: 0.82rem;
  color: var(--neutral-dark);
}
</style>
