<template>
  <section
    id="study-validation-section"
    class="study-validation"
    aria-labelledby="study-validation-title"
    :aria-busy="loading"
  >
    <header class="validation-header">
      <div>
        <h2 id="study-validation-title" tabindex="-1">
          Validation &amp; edit checks
        </h2>
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
                :aria-expanded="expandedRef === pack.ref ? 'true' : 'false'"
                @click="toggleExpanded(pack.ref)"
              >
                {{ expandedRef === pack.ref ? "Hide rules" : "Inspect rules"
                }}<span class="sr-only">
                  for {{ pack.key }} v{{ pack.version }}</span
                >
              </button>
              <button
                :id="`btn-preview-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="runPackPreview(pack)"
              >
                Preview on study<span class="sr-only">
                  for {{ pack.key }} v{{ pack.version }}</span
                >
              </button>
              <button
                v-if="canManageRules && pack.status === 'Draft'"
                :id="`btn-approve-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy"
                @click="approvingPack = pack"
              >
                Approve rule pack<span class="sr-only">
                  {{ pack.key }} v{{ pack.version }}</span
                >
              </button>
              <button
                v-if="canManageRules && pack.status === 'Approved'"
                :id="`btn-pin-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy"
                @click="beginPinToggle(pack)"
              >
                {{ pack.pinned ? "Unpin from study" : "Pin on study"
                }}<span class="sr-only">
                  {{ pack.key }} v{{ pack.version }}</span
                >
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
                :id="`btn-cancel-pin-${pack.key}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="cancelPinToggle(pack)"
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
              <span
                v-if="finding.classification"
                class="status-pill status-pill--classification"
                data-testid="finding-classification"
              >
                {{ finding.classification }}
              </span>
              <span class="severity-tag">[{{ finding.severity }}]</span>
              <code>{{ finding.rule_id }}</code>
              <span>{{ finding.message }}</span>
            </div>
            <p
              v-if="finding.decision"
              class="finding-decision-note"
              data-testid="finding-decision"
            >
              <strong>{{ finding.decision.status }}</strong> by
              {{ finding.decision.decided_by }} at
              {{ finding.decision.decided_at }}: {{ finding.decision.text }}
            </p>
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
              <button
                v-if="canInvestigate(finding) && finding.status === 'Open'"
                :data-testid="`btn-investigate-finding-${finding.ref}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="beginFindingAction(finding, 'investigate')"
              >
                Investigate
              </button>
              <button
                v-if="
                  canClassify(finding) &&
                  (finding.status === 'Open' ||
                    finding.status === 'Investigating')
                "
                :data-testid="`btn-classify-finding-${finding.ref}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="beginFindingAction(finding, 'classify')"
              >
                Classify
              </button>
              <button
                v-if="
                  canAccept(finding) &&
                  (finding.status === 'Open' ||
                    finding.status === 'Investigating')
                "
                :data-testid="`btn-accept-finding-${finding.ref}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy"
                @click="beginFindingAction(finding, 'accept')"
              >
                Accept finding
              </button>
              <button
                v-if="
                  canExplain(finding) &&
                  (finding.status === 'Open' ||
                    finding.status === 'Investigating')
                "
                :data-testid="`btn-explain-finding-${finding.ref}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy"
                @click="beginFindingAction(finding, 'explain')"
              >
                Record explanation
              </button>
              <button
                v-if="
                  canReopen(finding) &&
                  (finding.status === 'Accepted' ||
                    finding.status === 'Explained')
                "
                :data-testid="`btn-reopen-finding-${finding.ref}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="beginFindingAction(finding, 'reopen')"
              >
                Reopen
              </button>
            </div>
            <div
              v-if="findingActionDraft?.finding.ref === finding.ref"
              class="finding-action-box"
              :data-testid="`finding-action-form-${findingActionDraft.mode}`"
            >
              <label
                v-if="findingActionDraft.mode === 'classify'"
                :for="`finding-classification-${finding.ref}`"
              >
                Classification
                <select
                  :id="`finding-classification-${finding.ref}`"
                  v-model="findingActionClassification"
                  :disabled="busy"
                >
                  <option value="DataIssue">DataIssue</option>
                  <option value="MappingIssue">MappingIssue</option>
                  <option value="MetadataIssue">MetadataIssue</option>
                  <option value="RuleIssue">RuleIssue</option>
                  <option value="NotAnIssue">NotAnIssue</option>
                </select>
              </label>
              <label :for="`finding-action-text-${finding.ref}`">
                {{ findingActionPrompt(findingActionDraft.mode) }}
                <input
                  :id="`finding-action-text-${finding.ref}`"
                  v-model="findingActionText"
                  type="text"
                  autocomplete="off"
                  :placeholder="
                    findingActionPlaceholder(findingActionDraft.mode)
                  "
                  :disabled="busy"
                />
              </label>
              <button
                :data-testid="`btn-confirm-finding-${findingActionDraft.mode}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy || !findingActionText.trim()"
                @click="submitFindingAction"
              >
                {{
                  findingActionDraft.mode === "accept" ||
                  findingActionDraft.mode === "explain"
                    ? "Sign & confirm"
                    : "Confirm"
                }}
              </button>
              <button
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="findingActionDraft = null"
              >
                Cancel
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
              <ul v-if="selectedFinding.explanation.steps.length > 0">
                <li
                  v-for="(step, idx) in selectedFinding.explanation.steps"
                  :key="idx"
                >
                  {{ step.claim }}
                </li>
              </ul>
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
      v-if="signingFindingOp"
      :is-open="Boolean(signingFindingOp)"
      :username="signerUsername"
      :signer-name="signerFullName"
      :signer-role="
        signingFindingOp.mode === 'accept'
          ? 'Data Manager'
          : 'Clinical Research Associate'
      "
      :reasons="
        signingFindingOp.mode === 'accept'
          ? ACCEPT_FINDING_REASONS
          : EXPLAIN_FINDING_REASONS
      "
      :action-url="
        signingFindingOp.mode === 'accept'
          ? findingAcceptAction(signingFindingOp.finding.ref)
          : findingExplainAction(signingFindingOp.finding.ref)
      "
      :semantic-action="
        signingFindingOp.mode === 'accept'
          ? FINDING_ACCEPT_SEMANTIC_ACTION
          : FINDING_EXPLAIN_SEMANTIC_ACTION
      "
      :on-sign="signFindingWithToken"
      @cancel="signingFindingOp = null"
      @success="signingFindingOp = null"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import { useInlineFocusHandoff } from "ui";
import {
  describeKernelError,
  FINDING_ACCEPT_SEMANTIC_ACTION,
  FINDING_EXPLAIN_SEMANTIC_ACTION,
  findingAcceptAction,
  findingExplainAction,
  kernelApi,
  RULE_PACK_APPROVE_SEMANTIC_ACTION,
  rulePackApproveAction,
  type KernelFindingClassification,
  type KernelRulePack,
  type KernelRulePackPreview,
  type KernelValidationFinding,
  type KernelValidationFindingDetail,
} from "../../api/kernelApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import {
  collectionInstanceRef,
  observationNodeRef,
  parseNodeRef,
} from "../../canvas/nodeRef";
import { useAuthStore } from "../../stores/auth";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";

type FindingActionMode =
  "investigate" | "classify" | "accept" | "explain" | "reopen";

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

interface FindingCapabilityFlags {
  investigate: boolean;
  classify: boolean;
  accept: boolean;
  explain: boolean;
  reopen: boolean;
}

const canInvestigateFinding = ref(false);
const canClassifyFinding = ref(false);
const canAcceptFinding = ref(false);
const canExplainFinding = ref(false);
const canReopenFinding = ref(false);
const perFindingCapabilities = reactive<Record<string, FindingCapabilityFlags>>(
  {}
);

const findingActionDraft = ref<{
  finding: KernelValidationFinding;
  mode: FindingActionMode;
} | null>(null);
const findingActionText = ref("");
const findingActionClassification =
  ref<KernelFindingClassification>("DataIssue");
const signingFindingOp = ref<{
  finding: KernelValidationFinding;
  mode: "accept" | "explain";
  text: string;
  idempotencyKey: string;
  attestationId: string;
} | null>(null);

const approvingPack = ref<KernelRulePack | null>(null);
const pinningPack = ref<KernelRulePack | null>(null);
const pinReason = ref("");
const pinHandoff = useInlineFocusHandoff({
  openTarget: "#validation-pin-reason-input",
  closeFallback: "#study-validation-title",
  scrollIfOffscreen: true,
});

const APPROVE_REASONS = [
  "Rule pack governance approval",
  "APPROVAL",
  "CLINICAL_QC",
];
const ACCEPT_FINDING_REASONS = [
  "Responsibility",
  "Data management acceptance of verified source value",
];
const EXPLAIN_FINDING_REASONS = [
  "Approved",
  "Monitor verified source explanation",
];

const signerUsername = computed(
  () =>
    authStore.identity?.username ||
    authStore.user?.email ||
    authStore.user?.username ||
    "operator"
);
const signerFullName = computed(() => {
  const identity = authStore.identity;
  if (identity) {
    const full = [identity.firstName, identity.lastName]
      .filter(Boolean)
      .join(" ")
      .trim();
    if (full || identity.username) return full || identity.username || "";
  }
  return (
    authStore.user?.name || authStore.user?.full_name || signerUsername.value
  );
});

function unversionedPackRef(refText: string): string {
  const parsed = parseNodeRef(refText);
  return parsed ? `${parsed.kind}:${parsed.id}` : refText;
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
  await probePerFindingCapabilities(page.findings);
}

function canInvestigate(finding: KernelValidationFinding): boolean {
  return (
    canInvestigateFinding.value ||
    Boolean(perFindingCapabilities[finding.ref]?.investigate)
  );
}

function canClassify(finding: KernelValidationFinding): boolean {
  return (
    canClassifyFinding.value ||
    Boolean(perFindingCapabilities[finding.ref]?.classify)
  );
}

function canAccept(finding: KernelValidationFinding): boolean {
  return (
    canAcceptFinding.value ||
    Boolean(perFindingCapabilities[finding.ref]?.accept)
  );
}

function canExplain(finding: KernelValidationFinding): boolean {
  return (
    canExplainFinding.value ||
    Boolean(perFindingCapabilities[finding.ref]?.explain)
  );
}

function canReopen(finding: KernelValidationFinding): boolean {
  return (
    canReopenFinding.value ||
    Boolean(perFindingCapabilities[finding.ref]?.reopen)
  );
}

async function probePerFindingCapabilities(
  items: KernelValidationFinding[]
): Promise<void> {
  if (typeof kernelApi.canI !== "function" || items.length === 0) return;
  if (
    canInvestigateFinding.value &&
    canClassifyFinding.value &&
    canAcceptFinding.value &&
    canExplainFinding.value &&
    canReopenFinding.value
  ) {
    return;
  }

  await Promise.all(
    items.map(async (item) => {
      if (!item.ref || perFindingCapabilities[item.ref]) return;
      try {
        const [inv, cls, acc, exp, reo] = await Promise.all([
          canInvestigateFinding.value
            ? Promise.resolve({ permitted: true })
            : kernelApi.canI("validation_finding.investigate", item.ref),
          canClassifyFinding.value
            ? Promise.resolve({ permitted: true })
            : kernelApi.canI("validation_finding.classify", item.ref),
          canAcceptFinding.value
            ? Promise.resolve({ permitted: true })
            : kernelApi.canI("validation_finding.accept", item.ref),
          canExplainFinding.value
            ? Promise.resolve({ permitted: true })
            : kernelApi.canI("validation_finding.explain", item.ref),
          canReopenFinding.value
            ? Promise.resolve({ permitted: true })
            : kernelApi.canI("validation_finding.reopen", item.ref),
        ]);
        perFindingCapabilities[item.ref] = {
          investigate: Boolean(inv?.permitted),
          classify: Boolean(cls?.permitted),
          accept: Boolean(acc?.permitted),
          explain: Boolean(exp?.permitted),
          reopen: Boolean(reo?.permitted),
        };
      } catch {
        perFindingCapabilities[item.ref] = {
          investigate: false,
          classify: false,
          accept: false,
          explain: false,
          reopen: false,
        };
      }
    })
  );
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

async function loadFindingCapabilities(targetStudyRef: string) {
  if (typeof kernelApi.canI !== "function") {
    const fallback = Boolean(props.canManageRules);
    canInvestigateFinding.value = fallback;
    canClassifyFinding.value = fallback;
    canAcceptFinding.value = fallback;
    canExplainFinding.value = fallback;
    canReopenFinding.value = fallback;
    return;
  }
  try {
    const [inv, cls, acc, exp, reo] = await Promise.all([
      kernelApi.canI("validation_finding.investigate", targetStudyRef),
      kernelApi.canI("validation_finding.classify", targetStudyRef),
      kernelApi.canI("validation_finding.accept", targetStudyRef),
      kernelApi.canI("validation_finding.explain", targetStudyRef),
      kernelApi.canI("validation_finding.reopen", targetStudyRef),
    ]);
    canInvestigateFinding.value = Boolean(inv?.permitted);
    canClassifyFinding.value = Boolean(cls?.permitted);
    canAcceptFinding.value = Boolean(acc?.permitted);
    canExplainFinding.value = Boolean(exp?.permitted);
    canReopenFinding.value = Boolean(reo?.permitted);
  } catch {
    canInvestigateFinding.value = false;
    canClassifyFinding.value = false;
    canAcceptFinding.value = false;
    canExplainFinding.value = false;
    canReopenFinding.value = false;
  }
}

watch(
  () => props.studyCode,
  async (code) => {
    studyRef.value = null;
    rulePacks.value = [];
    findings.value = [];
    selectedFinding.value = null;
    findingActionDraft.value = null;
    signingFindingOp.value = null;
    errorMessage.value = "";
    actionMessage.value = "";
    for (const key of Object.keys(perFindingCapabilities)) {
      delete perFindingCapabilities[key];
    }
    if (!code) return;
    loading.value = true;
    try {
      studyRef.value = await findKernelStudyRef(code);
      if (studyRef.value) {
        const [, loadedItems] = await Promise.all([
          loadRulePacks(),
          (async () => {
            const page = await kernelApi.listStudyValidationFindings(
              studyRef.value!,
              {
                status: statusFilter.value || undefined,
                severity: severityFilter.value || undefined,
              }
            );
            findings.value = page.findings;
            return page.findings;
          })(),
          loadFindingCapabilities(studyRef.value),
        ]);
        await probePerFindingCapabilities(loadedItems);
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
  pinHandoff.rememberTrigger(`#btn-pin-pack-${pack.key}-${pack.version}`);
  pinningPack.value = pack;
  pinReason.value = "";
  void pinHandoff.handoff("#validation-pin-reason-input", {
    scrollIfOffscreen: true,
  });
}

function cancelPinToggle(pack: KernelRulePack) {
  pinningPack.value = null;
  pinReason.value = "";
  void pinHandoff.restoreTrigger([
    `#btn-pin-pack-${pack.key}-${pack.version}`,
    `#btn-toggle-rules-${pack.key}-${pack.version}`,
    "#study-validation-title",
  ]);
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
    void pinHandoff.restoreTrigger([
      `#btn-pin-pack-${pack.key}-${pack.version}`,
      `#btn-toggle-rules-${pack.key}-${pack.version}`,
      "#study-validation-title",
    ]);
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
    selectedFinding.value = await kernelApi.getValidationFinding(findingRef);
  } catch (error) {
    errorMessage.value = describeKernelError(error);
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

function findingActionPrompt(mode: FindingActionMode): string {
  switch (mode) {
    case "investigate":
      return "Reason for opening investigation";
    case "classify":
      return "Reason for classification";
    case "accept":
      return "Rationale for accepting discrepancy";
    case "explain":
      return "Clinical explanation against source";
    case "reopen":
      return "Reason for reopening finding";
  }
}

function findingActionPlaceholder(mode: FindingActionMode): string {
  switch (mode) {
    case "investigate":
      return "Reviewing source worksheet with site coordinator";
    case "classify":
      return "Confirmed protocol-permitted exception";
    case "accept":
      return "Confirmed out-of-window lab accepted per data review plan";
    case "explain":
      return "Verified against source chart; clinically expected baseline value";
    case "reopen":
      return "Additional source documentation requires re-evaluation";
  }
}

function beginFindingAction(
  finding: KernelValidationFinding,
  mode: FindingActionMode
) {
  findingActionDraft.value = { finding, mode };
  findingActionText.value = "";
  findingActionClassification.value =
    (finding.classification as KernelFindingClassification | null) ??
    "DataIssue";
  errorMessage.value = "";
}

async function submitFindingAction() {
  const draft = findingActionDraft.value;
  const text = findingActionText.value.trim();
  if (!draft || !text || busy.value) return;

  if (draft.mode === "accept" || draft.mode === "explain") {
    signingFindingOp.value = {
      finding: draft.finding,
      mode: draft.mode,
      text,
      idempotencyKey: globalThis.crypto.randomUUID(),
      attestationId: globalThis.crypto.randomUUID(),
    };
    return;
  }

  busy.value = true;
  errorMessage.value = "";
  try {
    if (draft.mode === "investigate") {
      await kernelApi.investigateValidationFinding({
        findingRef: draft.finding.ref,
        reason: text,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      actionMessage.value = `Marked finding ${draft.finding.rule_id} as Investigating.`;
    } else if (draft.mode === "classify") {
      await kernelApi.classifyValidationFinding({
        findingRef: draft.finding.ref,
        classification: findingActionClassification.value,
        reason: text,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      actionMessage.value = `Classified finding ${draft.finding.rule_id} as ${findingActionClassification.value}.`;
    } else if (draft.mode === "reopen") {
      await kernelApi.reopenValidationFinding({
        findingRef: draft.finding.ref,
        reason: text,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      actionMessage.value = `Reopened finding ${draft.finding.rule_id}.`;
    }
    findingActionDraft.value = null;
    findingActionText.value = "";
    await loadFindings();
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

async function signFindingWithToken(sigToken: string, signingReason: string) {
  const op = signingFindingOp.value;
  if (!op) return;
  busy.value = true;
  errorMessage.value = "";
  try {
    const reasonHeader = signingReason.trim() || op.text;
    if (op.mode === "accept") {
      await kernelApi.acceptValidationFinding({
        findingRef: op.finding.ref,
        rationale: op.text,
        attestationId: op.attestationId,
        sigToken,
        reason: reasonHeader,
        idempotencyKey: op.idempotencyKey,
      });
      actionMessage.value = `Accepted finding ${op.finding.rule_id}.`;
    } else {
      await kernelApi.explainValidationFinding({
        findingRef: op.finding.ref,
        explanation: op.text,
        attestationId: op.attestationId,
        sigToken,
        reason: reasonHeader,
        idempotencyKey: op.idempotencyKey,
      });
      actionMessage.value = `Recorded signed explanation for finding ${op.finding.rule_id}.`;
    }
    findingActionDraft.value = null;
    findingActionText.value = "";
    await loadFindings();
    return { kind: "signed" as const };
  } catch (error) {
    const message = describeKernelError(error);
    errorMessage.value = message;
    throw new Error(message, { cause: error });
  } finally {
    busy.value = false;
  }
}

function findingFormLocation(
  finding: KernelValidationFinding
): RouteLocationRaw | null {
  if (
    finding.target_kind === "DatasetRecord" ||
    finding.target_kind === "Record"
  ) {
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
  if (finding.target_kind === "Observation") {
    const obsRef = observationNodeRef(finding.record_key);
    if (!obsRef) return null;
    return {
      name: "study",
      params: { studyCode: props.studyCode },
      query: {
        stage: "data",
        section: "validation",
        observation: obsRef,
      },
    };
  }
  return null;
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
.status-pill--classification {
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
}
.finding-decision-note {
  margin: 0;
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
}
.finding-action-box {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  font-size: 0.82rem;
}
.finding-action-box label {
  display: flex;
  flex-direction: column;
  gap: 4px;
  flex: 1 1 220px;
  font-weight: 600;
}
.finding-action-box input,
.finding-action-box select {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  font-size: 0.84rem;
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
