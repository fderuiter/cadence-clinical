<template>
  <section
    id="study-validation-section"
    class="study-validation"
    aria-labelledby="study-validation-title"
    :aria-busy="loading"
  >
    <header class="validation-header">
      <div>
        <h2 id="study-validation-title">Validation &amp; edit checks</h2>
        <p class="validation-subtitle">
          Governed edit-check rule packs, in-memory study previews, active study
          pins, and post-commit validation findings for {{ studyCode }}.
        </p>
      </div>
      <button
        id="btn-refresh-validation"
        type="button"
        class="btn btn-secondary"
        :disabled="loading || busy"
        @click="refreshAll"
      >
        Refresh
      </button>
    </header>

    <p v-if="loading && !studyRef" class="status-line" role="status">
      Looking up {{ studyCode }}…
    </p>
    <p
      v-else-if="errorMessage"
      id="study-validation-error"
      class="banner banner-error"
      role="alert"
    >
      {{ errorMessage }}
    </p>
    <p v-else-if="!studyRef" id="study-validation-no-study" class="status-line">
      Study {{ studyCode }} wasn't found or isn't visible to your account, so
      its validation rules and findings cannot be listed here.
    </p>

    <template v-else>
      <div
        v-if="actionMessage"
        id="study-validation-outcome"
        data-testid="study-validation-outcome"
        class="banner banner-success"
        role="status"
      >
        {{ actionMessage }}
      </div>

      <section
        id="study-rule-packs"
        class="validation-card"
        aria-labelledby="study-rule-packs-heading"
      >
        <div class="card-header-row">
          <h3 id="study-rule-packs-heading">
            Rule packs ({{ rulePacks.length }})
          </h3>
          <span class="pinned-summary" data-testid="pinned-rule-packs-count">
            {{ pinnedRefs.length }} pinned on {{ studyCode }}
          </span>
        </div>

        <p v-if="rulePacks.length === 0" data-testid="no-rule-packs">
          No edit-check rule packs are registered in this tenant yet. Import an
          ODM v1.3.2 XML protocol with RangeCheck or ConditionDef rules in
          Design → Protocol versions to draft one automatically.
        </p>

        <div v-else class="rule-pack-list">
          <article
            v-for="pack in rulePacks"
            :key="pack.ref"
            :data-testid="`rule-pack-${pack.key}-v${pack.version}`"
            class="rule-pack-item"
          >
            <div class="rule-pack-top">
              <div>
                <strong class="rule-pack-title">{{ pack.title }}</strong>
                <div class="rule-pack-meta">
                  <code>{{ pack.key }}</code> · v{{ pack.version }} ·
                  {{ pack.rule_count }}
                  {{ Number(pack.rule_count) === 1 ? "rule" : "rules" }}
                </div>
              </div>
              <div class="rule-pack-badges">
                <span
                  class="status-pill"
                  :data-status="pack.status.toLowerCase()"
                >
                  {{ pack.status }}
                </span>
                <span
                  v-if="pack.pinned"
                  class="status-pill status-pill--pinned"
                  data-testid="rule-pack-pinned-badge"
                >
                  Pinned on study
                </span>
              </div>
            </div>

            <div class="rule-pack-actions">
              <button
                :id="`btn-toggle-rules-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-secondary"
                @click="toggleExpanded(pack.ref)"
              >
                {{ expandedRef === pack.ref ? "Hide rules" : "Inspect rules" }}
              </button>
              <button
                :id="`btn-preview-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="runPackPreview(pack)"
              >
                Preview on study
              </button>
              <button
                v-if="canManageRules && pack.status === 'Draft'"
                :id="`btn-approve-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy"
                @click="approvingPack = pack"
              >
                Approve rule pack
              </button>
              <button
                v-if="canManageRules && pack.status === 'Approved'"
                :id="`btn-pin-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy"
                @click="beginPinToggle(pack)"
              >
                {{ pack.pinned ? "Unpin from study" : "Pin on study" }}
              </button>
            </div>

            <div
              v-if="pinningPack?.ref === pack.ref"
              class="pin-reason-row"
              data-testid="pin-reason-form"
            >
              <label for="validation-pin-reason-input">
                Reason for {{ pack.pinned ? "unpinning" : "pinning" }}
                {{ pack.key }}
                <input
                  id="validation-pin-reason-input"
                  v-model="pinReason"
                  type="text"
                  autocomplete="off"
                  placeholder="Activate approved edit checks for study collection"
                  :disabled="busy"
                />
              </label>
              <button
                :id="`btn-confirm-pin-${pack.key}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy || !pinReason.trim()"
                @click="confirmPinToggle(pack)"
              >
                Confirm
              </button>
              <button
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="pinningPack = null"
              >
                Cancel
              </button>
            </div>

            <ul
              v-if="expandedRef === pack.ref && pack.rules.length > 0"
              class="compiled-rules-list"
              :data-testid="`compiled-rules-${pack.key}`"
            >
              <li v-for="rule in pack.rules" :key="rule.id">
                <code>{{ rule.id }}</code>
                <span class="severity-tag">[{{ rule.severity }}]</span>
                <span>{{ rule.explanation }}</span>
                <div class="rule-expr">
                  <code v-if="rule.dataset">dataset: {{ rule.dataset }}</code>
                  <code v-if="rule.condition"
                    >condition: {{ rule.condition }}</code
                  >
                </div>
              </li>
            </ul>

            <div
              v-if="previewByRef[pack.ref]"
              class="pack-preview-box"
              :data-testid="`pack-preview-${pack.key}`"
            >
              <strong>
                In-memory study preview ({{
                  previewByRef[pack.ref].forms_evaluated
                }}
                forms, {{ previewByRef[pack.ref].rules_evaluated }} rules):
              </strong>
              <span v-if="previewByRef[pack.ref].violations.length === 0">
                0 discrepancies across current study forms.
              </span>
              <ul v-else>
                <li
                  v-for="(v, idx) in previewByRef[pack.ref].violations"
                  :key="`${v.form_id}-${v.rule_id}-${idx}`"
                >
                  <strong>[{{ v.severity }}] {{ v.rule_id }}</strong>
                  <template v-if="v.dataset">
                    on <code>{{ v.dataset }}</code>
                  </template>
                  <template v-if="v.item_id">
                    · <code>{{ v.item_id }}</code> </template
                  >: {{ v.message }}
                </li>
              </ul>
            </div>
          </article>
        </div>
      </section>

      <section
        id="study-validation-findings"
        class="validation-card"
        aria-labelledby="study-findings-heading"
      >
        <div class="card-header-row">
          <h3 id="study-findings-heading">
            Validation findings ({{ findings.length }})
          </h3>
          <div class="findings-filters">
            <label for="validation-status-filter">
              Status
              <select
                id="validation-status-filter"
                v-model="statusFilter"
                @change="loadFindings"
              >
                <option value="">All statuses</option>
                <option value="Open">Open</option>
                <option value="Investigating">Investigating</option>
                <option value="Accepted">Accepted</option>
                <option value="Explained">Explained</option>
                <option value="Resolved">Resolved</option>
              </select>
            </label>
            <label for="validation-severity-filter">
              Severity
              <select
                id="validation-severity-filter"
                v-model="severityFilter"
                @change="loadFindings"
              >
                <option value="">All severities</option>
                <option value="Error">Error</option>
                <option value="Warning">Warning</option>
                <option value="Notice">Notice</option>
              </select>
            </label>
          </div>
        </div>

        <div
          v-if="canManageRules"
          id="study-validation-run-bar"
          class="pin-reason-row"
        >
          <label for="validation-run-reason">
            Reason for validation run
            <input
              id="validation-run-reason"
              v-model="validationRunReason"
              type="text"
              autocomplete="off"
              placeholder="Evaluate pinned edit checks across study forms"
              :disabled="busy"
            />
          </label>
          <button
            id="btn-run-study-validation"
            type="button"
            class="btn btn-primary"
            :disabled="busy || !validationRunReason.trim()"
            @click="executeStudyValidationRun"
          >
            Run study validation
          </button>
        </div>

        <p v-if="findings.length === 0" data-testid="no-validation-findings">
          No validation findings match the current filter.
        </p>

        <ul v-else class="findings-list" data-testid="validation-findings-list">
          <li
            v-for="finding in findings"
            :key="finding.ref"
            :data-testid="`finding-${finding.rule_id}-${finding.status}`"
            class="finding-item"
          >
            <div class="finding-head">
              <span class="status-pill">{{ finding.status }}</span>
              <span class="severity-tag">[{{ finding.severity }}]</span>
              <code>{{ finding.rule_id }}</code>
              <span>{{ finding.message }}</span>
            </div>
            <div class="finding-actions">
              <router-link
                v-if="findingFormLocation(finding)"
                class="form-link"
                :to="findingFormLocationOrDefault(finding)"
              >
                Open {{ finding.variable ?? "form" }}
              </router-link>
              <button
                type="button"
                class="btn btn-secondary"
                @click="inspectFinding(finding.ref)"
              >
                {{
                  selectedFinding?.finding.ref === finding.ref
                    ? "Hide explanation"
                    : "Explain finding"
                }}
              </button>
            </div>
            <div
              v-if="selectedFinding?.finding.ref === finding.ref"
              class="finding-explanation"
              data-testid="finding-explanation"
            >
              <p>
                <strong>{{ selectedFinding.explanation.claim }}</strong>
              </p>
              <p
                v-if="selectedFinding.finding.classification"
                data-testid="finding-classification-badge"
              >
                Classification:
                <strong>{{ selectedFinding.finding.classification }}</strong>
              </p>
              <p
                v-if="
                  selectedFinding.finding.status === 'Accepted' &&
                  selectedFinding.finding.decision_text
                "
                data-testid="finding-acceptance-rationale"
              >
                Acceptance rationale:
                {{ selectedFinding.finding.decision_text }}
              </p>
              <p
                v-else-if="
                  selectedFinding.finding.status === 'Explained' &&
                  selectedFinding.finding.decision_text
                "
                data-testid="finding-reviewer-explanation"
              >
                Reviewer explanation:
                {{ selectedFinding.finding.decision_text }}
              </p>
              <ul v-if="selectedFinding.explanation.steps.length > 0">
                <li
                  v-for="(step, idx) in selectedFinding.explanation.steps"
                  :key="idx"
                >
                  {{ step.claim }}
                </li>
              </ul>

              <div
                v-if="canManageRules"
                class="finding-triage-controls"
                data-testid="finding-triage-controls"
              >
                <label
                  :for="`finding-triage-reason-${findingDomKey(finding.ref)}`"
                >
                  Triage rationale / explanation
                  <input
                    :id="`finding-triage-reason-${findingDomKey(finding.ref)}`"
                    v-model="triageReason"
                    type="text"
                    autocomplete="off"
                    placeholder="Document investigation, acceptance rationale, or reviewer note"
                    :disabled="busy"
                  />
                </label>

                <div class="finding-actions">
                  <button
                    v-if="selectedFinding.finding.status === 'Open'"
                    id="btn-investigate-finding"
                    type="button"
                    class="btn btn-secondary"
                    :disabled="busy || !triageReason.trim()"
                    @click="markFindingInvestigating(finding.ref)"
                  >
                    Start investigation
                  </button>

                  <label
                    :for="`finding-classification-${findingDomKey(finding.ref)}`"
                  >
                    Classification
                    <select
                      :id="`finding-classification-${findingDomKey(finding.ref)}`"
                      v-model="triageClassification"
                      :disabled="busy"
                    >
                      <option value="DataIssue">Data issue</option>
                      <option value="MappingIssue">Mapping issue</option>
                      <option value="MetadataIssue">Metadata issue</option>
                      <option value="RuleIssue">Rule issue</option>
                      <option value="NotAnIssue">Not an issue</option>
                    </select>
                  </label>
                  <button
                    id="btn-classify-finding"
                    type="button"
                    class="btn btn-secondary"
                    :disabled="busy || !triageReason.trim()"
                    @click="classifySelectedFinding(finding.ref)"
                  >
                    Classify finding
                  </button>

                  <button
                    v-if="
                      selectedFinding.finding.status === 'Open' ||
                      selectedFinding.finding.status === 'Investigating'
                    "
                    id="btn-accept-finding"
                    type="button"
                    class="btn btn-primary"
                    :disabled="busy || !triageReason.trim()"
                    @click="beginSignedFindingAction('accept', finding)"
                  >
                    Accept finding
                  </button>

                  <button
                    v-if="
                      selectedFinding.finding.status === 'Open' ||
                      selectedFinding.finding.status === 'Investigating'
                    "
                    id="btn-explain-finding-sign"
                    type="button"
                    class="btn btn-primary"
                    :disabled="busy || !triageReason.trim()"
                    @click="beginSignedFindingAction('explain', finding)"
                  >
                    Sign reviewer explanation
                  </button>

                  <button
                    v-if="
                      selectedFinding.finding.status === 'Accepted' ||
                      selectedFinding.finding.status === 'Explained'
                    "
                    id="btn-reopen-finding"
                    type="button"
                    class="btn btn-secondary"
                    :disabled="busy || !triageReason.trim()"
                    @click="reopenSelectedFinding(finding.ref)"
                  >
                    Reopen finding
                  </button>
                </div>
              </div>
            </div>
          </li>
        </ul>
      </section>
    </template>

    <SignatureCaptureModal
      v-if="approvingPack"
      :is-open="Boolean(approvingPack)"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Study Designer"
      :reasons="APPROVE_REASONS"
      :action-url="rulePackApproveAction(approvingPack.ref)"
      :semantic-action="RULE_PACK_APPROVE_SEMANTIC_ACTION"
      :on-sign="approveWithToken"
      @cancel="approvingPack = null"
      @success="approvingPack = null"
    />

    <SignatureCaptureModal
      v-if="signingFinding && signingFindingMode"
      :is-open="Boolean(signingFinding && signingFindingMode)"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Data Manager"
      :reasons="FINDING_SIGN_REASONS"
      :action-url="
        signingFindingMode === 'accept'
          ? findingAcceptAction(signingFinding.ref)
          : findingExplainAction(signingFinding.ref)
      "
      :semantic-action="
        signingFindingMode === 'accept'
          ? FINDING_ACCEPT_SEMANTIC_ACTION
          : FINDING_EXPLAIN_SEMANTIC_ACTION
      "
      :on-sign="completeSignedFindingAction"
      @cancel="closeSignedFindingModal"
      @success="closeSignedFindingModal"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  describeKernelError,
  FINDING_ACCEPT_SEMANTIC_ACTION,
  FINDING_EXPLAIN_SEMANTIC_ACTION,
  findingAcceptAction,
  findingExplainAction,
  kernelApi,
  RULE_PACK_APPROVE_SEMANTIC_ACTION,
  rulePackApproveAction,
  type KernelRulePack,
  type KernelRulePackPreview,
  type KernelValidationFinding,
  type KernelValidationFindingDetail,
} from "../../api/kernelApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { collectionInstanceRef, parseNodeRef } from "../../canvas/nodeRef";
import { useAuthStore } from "../../stores/auth";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";

const props = defineProps<{
  studyCode: string;
  canManageRules: boolean;
}>();

const authStore = useAuthStore();
const studyRef = ref<string | null>(null);
const loading = ref(false);
const busy = ref(false);
const errorMessage = ref("");
const actionMessage = ref("");

const rulePacks = ref<KernelRulePack[]>([]);
const pinnedRefs = ref<string[]>([]);
const findings = ref<KernelValidationFinding[]>([]);
const statusFilter = ref("");
const severityFilter = ref("");
const expandedRef = ref<string | null>(null);
const previewByRef = reactive<Record<string, KernelRulePackPreview>>({});
const selectedFinding = ref<KernelValidationFindingDetail | null>(null);

const approvingPack = ref<KernelRulePack | null>(null);
const pinningPack = ref<KernelRulePack | null>(null);
const pinReason = ref("");

const validationRunReason = ref("Run on-demand study edit-check validation");
const triageReason = ref("");
const triageClassification = ref("DataIssue");
const signingFinding = ref<KernelValidationFinding | null>(null);
const signingFindingMode = ref<"accept" | "explain" | null>(null);

const APPROVE_REASONS = [
  "Rule pack governance approval",
  "APPROVAL",
  "CLINICAL_QC",
];

const FINDING_SIGN_REASONS = [
  "Validation finding disposition",
  "CLINICAL_QC",
  "APPROVAL",
];

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

function unversionedPackRef(refText: string): string {
  const parsed = parseNodeRef(refText);
  return parsed ? `${parsed.kind}:${parsed.id}` : refText;
}

function findingDomKey(refText: string): string {
  return parseNodeRef(refText)?.id ?? refText;
}

function toggleExpanded(refText: string) {
  expandedRef.value = expandedRef.value === refText ? null : refText;
}

async function loadRulePacks() {
  if (!studyRef.value) return;
  const response = await kernelApi.listStudyRulePacks(studyRef.value);
  rulePacks.value = response.rule_packs;
  pinnedRefs.value = response.pinned_rule_pack_refs.map(unversionedPackRef);
}

async function loadFindings() {
  if (!studyRef.value) return;
  const page = await kernelApi.listStudyValidationFindings(studyRef.value, {
    status: statusFilter.value || undefined,
    severity: severityFilter.value || undefined,
  });
  findings.value = page.findings;
}

async function refreshAll() {
  if (!studyRef.value) return;
  loading.value = true;
  errorMessage.value = "";
  try {
    await Promise.all([loadRulePacks(), loadFindings()]);
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    loading.value = false;
  }
}

watch(
  () => props.studyCode,
  async (code) => {
    studyRef.value = null;
    rulePacks.value = [];
    findings.value = [];
    selectedFinding.value = null;
    errorMessage.value = "";
    actionMessage.value = "";
    if (!code) return;
    loading.value = true;
    try {
      studyRef.value = await findKernelStudyRef(code);
      if (studyRef.value) {
        await Promise.all([loadRulePacks(), loadFindings()]);
      }
    } catch (error) {
      errorMessage.value = describeKernelError(error);
    } finally {
      loading.value = false;
    }
  },
  { immediate: true }
);

async function runPackPreview(pack: KernelRulePack) {
  if (!studyRef.value || busy.value) return;
  busy.value = true;
  errorMessage.value = "";
  try {
    previewByRef[pack.ref] = await kernelApi.previewStudyRulePack(
      studyRef.value,
      pack.ref
    );
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

async function approveWithToken(sigToken: string, reason: string) {
  const target = approvingPack.value;
  if (!target) return;
  busy.value = true;
  errorMessage.value = "";
  try {
    const approved = await kernelApi.approveLibraryItem({
      itemRef: target.ref,
      meaning: "Approved",
      sigToken,
      reason,
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    actionMessage.value = `Approved rule pack ${approved.key} v${approved.version}.`;
    await loadRulePacks();
    return approved;
  } finally {
    busy.value = false;
  }
}

function beginPinToggle(pack: KernelRulePack) {
  pinningPack.value = pack;
  pinReason.value = "";
}

async function confirmPinToggle(pack: KernelRulePack) {
  if (!studyRef.value || !pinReason.value.trim() || busy.value) return;
  const targetRef = unversionedPackRef(pack.ref);
  const current = new Set(pinnedRefs.value);
  if (pack.pinned) {
    current.delete(targetRef);
  } else {
    current.add(targetRef);
  }

  busy.value = true;
  errorMessage.value = "";
  try {
    const updated = await kernelApi.declareStudyRulePacks({
      studyRef: studyRef.value,
      rulePackRefs: [...current],
      reason: pinReason.value.trim(),
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    rulePacks.value = updated.rule_packs;
    pinnedRefs.value = updated.pinned_rule_pack_refs.map(unversionedPackRef);
    pinningPack.value = null;
    pinReason.value = "";
    actionMessage.value = pack.pinned
      ? `Unpinned ${pack.key} from ${props.studyCode}.`
      : `Pinned ${pack.key} v${pack.version} on ${props.studyCode}.`;
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

async function inspectFinding(findingRef: string) {
  if (selectedFinding.value?.finding.ref === findingRef) {
    selectedFinding.value = null;
    return;
  }
  try {
    const detail = await kernelApi.getValidationFinding(findingRef);
    selectedFinding.value = detail;
    if (detail.finding.classification) {
      triageClassification.value = detail.finding.classification;
    }
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  }
}

async function executeStudyValidationRun() {
  if (!studyRef.value || !validationRunReason.value.trim() || busy.value) {
    return;
  }
  busy.value = true;
  errorMessage.value = "";
  try {
    const run = await kernelApi.runStudyValidation({
      studyRef: studyRef.value,
      reason: validationRunReason.value.trim(),
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    await loadFindings();
    actionMessage.value = `Validation run complete: ${run.detected} detected (${run.new} new, ${run.resolved} resolved).`;
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

async function markFindingInvestigating(findingRef: string) {
  if (!triageReason.value.trim() || busy.value) return;
  busy.value = true;
  errorMessage.value = "";
  try {
    const updated = await kernelApi.investigateFinding({
      findingRef,
      reason: triageReason.value.trim(),
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    selectedFinding.value = updated;
    await loadFindings();
    actionMessage.value = `Marked finding ${updated.finding.rule_id} as Investigating.`;
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

async function classifySelectedFinding(findingRef: string) {
  if (!triageReason.value.trim() || busy.value) return;
  busy.value = true;
  errorMessage.value = "";
  try {
    const updated = await kernelApi.classifyFinding({
      findingRef,
      classification: triageClassification.value,
      reason: triageReason.value.trim(),
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    selectedFinding.value = updated;
    await loadFindings();
    actionMessage.value = `Classified finding ${updated.finding.rule_id} as ${updated.finding.classification}.`;
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

async function reopenSelectedFinding(findingRef: string) {
  if (!triageReason.value.trim() || busy.value) return;
  busy.value = true;
  errorMessage.value = "";
  try {
    const updated = await kernelApi.reopenFinding({
      findingRef,
      reason: triageReason.value.trim(),
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    selectedFinding.value = updated;
    await loadFindings();
    actionMessage.value = `Reopened finding ${updated.finding.rule_id} (${updated.finding.status}).`;
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

function beginSignedFindingAction(
  mode: "accept" | "explain",
  finding: KernelValidationFinding
) {
  signingFinding.value = finding;
  signingFindingMode.value = mode;
}

function closeSignedFindingModal() {
  signingFinding.value = null;
  signingFindingMode.value = null;
}

async function completeSignedFindingAction(sigToken: string, reason: string) {
  const target = signingFinding.value;
  const mode = signingFindingMode.value;
  if (!target || !mode) return null;
  const text = triageReason.value.trim() || reason;
  busy.value = true;
  errorMessage.value = "";
  try {
    const updated =
      mode === "accept"
        ? await kernelApi.acceptFinding({
            findingRef: target.ref,
            rationale: text,
            meaning: "Responsibility",
            sigToken,
            reason: text,
            idempotencyKey: globalThis.crypto.randomUUID(),
          })
        : await kernelApi.explainFinding({
            findingRef: target.ref,
            explanation: text,
            meaning: "Approved",
            sigToken,
            reason: text,
            idempotencyKey: globalThis.crypto.randomUUID(),
          });
    selectedFinding.value = updated;
    await loadFindings();
    actionMessage.value =
      mode === "accept"
        ? `Accepted finding ${updated.finding.rule_id}.`
        : `Recorded reviewer explanation for ${updated.finding.rule_id}.`;
    return updated;
  } finally {
    busy.value = false;
  }
}

function findingFormLocationOrDefault(
  finding: KernelValidationFinding
): RouteLocationRaw {
  return (
    findingFormLocation(finding) ?? {
      name: "study",
      params: { studyCode: props.studyCode },
    }
  );
}

function findingFormLocation(
  finding: KernelValidationFinding
): RouteLocationRaw | null {
  if (finding.target_kind !== "Record") return null;
  const formRef = collectionInstanceRef(finding.record_key);
  if (!formRef) return null;
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "data",
      section: "validation",
      form: formRef,
      ...(finding.variable ? { item: finding.variable } : {}),
    },
  };
}
</script>

<style scoped>
.study-validation {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.validation-header,
.card-header-row,
.rule-pack-top,
.rule-pack-actions,
.pin-reason-row,
.findings-filters,
.finding-head,
.finding-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.rule-pack-actions,
.pin-reason-row,
.finding-head,
.finding-actions {
  justify-content: flex-start;
}
.validation-subtitle,
.status-line,
.rule-pack-meta {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}
.validation-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 14px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}
.rule-pack-list,
.findings-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  list-style: none;
  margin: 0;
  padding: 0;
}
.rule-pack-item,
.finding-item {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
}
.status-pill {
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
  background: var(--neutral-light);
}
.status-pill--pinned {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}
.severity-tag {
  font-weight: 600;
  font-size: 0.8rem;
}
.compiled-rules-list,
.pack-preview-box {
  margin: 4px 0 0;
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  font-size: 0.82rem;
}
.rule-expr {
  display: flex;
  gap: 12px;
  margin-top: 2px;
}
.banner {
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.85rem;
}
.banner-error {
  background: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}
.banner-success {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}
</style>
