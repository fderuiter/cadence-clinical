<template>
  <section
    id="study-locks-section"
    class="study-locks"
    aria-labelledby="study-locks-title"
    :aria-busy="studyLoading || locksLoading"
  >
    <header class="study-locks-header">
      <div>
        <h2 id="study-locks-title">Locks</h2>
        <p class="study-locks-subtitle">
          Signed <strong>Frozen</strong> and <strong>Locked</strong> assertions
          across study, site, participant, form, and observation scopes.
        </p>
      </div>
      <div class="study-locks-filters" data-testid="study-locks-filters">
        <label class="checkbox-label" for="study-locks-active-only">
          <input
            id="study-locks-active-only"
            v-model="activeOnly"
            type="checkbox"
            @change="reloadLocks"
          />
          Active locks only
        </label>

        <label for="study-locks-as-of">As of (ISO UTC)</label>
        <input
          id="study-locks-as-of"
          v-model="asOfInput"
          type="text"
          autocomplete="off"
          placeholder="Latest truth"
        />
        <button
          id="study-locks-refresh"
          type="button"
          class="btn btn-secondary"
          :disabled="studyLoading || locksLoading"
          @click="reloadLocks"
        >
          Inspect
        </button>
      </div>
    </header>

    <p v-if="studyLoading" class="status-line" role="status">
      Looking up {{ studyCode }}…
    </p>
    <div
      v-else-if="studyError"
      id="study-locks-error"
      class="status-line error-banner"
      role="alert"
    >
      <p class="status-line">
        {{ studyCode }}'s locks couldn't be loaded.
        <button
          id="btn-retry-study-locks"
          type="button"
          class="btn btn-secondary"
          :disabled="studyLoading"
          @click="loadStudyLocksContext(studyCode)"
        >
          Try again
        </button>
      </p>
      <SupportDetails :detail="studyError" />
    </div>
    <p v-else-if="!studyRef" id="study-locks-no-study" class="status-line">
      Study {{ studyCode }} wasn't found or isn't visible to your account, so
      its locks cannot be listed here.
    </p>

    <template v-else>
      <form
        v-if="canApply"
        id="study-locks-apply-form"
        class="apply-card"
        data-testid="study-locks-apply-form"
        @submit.prevent="openApplyModal"
      >
        <h3>Apply signed freeze or lock</h3>
        <div class="apply-grid">
          <div class="field-group">
            <label for="lock-apply-scope-kind">Scope level</label>
            <select
              id="lock-apply-scope-kind"
              v-model="applyScopeKind"
              @change="onScopeKindChange"
            >
              <option value="study">Study</option>
              <option value="study_site">Study site</option>
              <option value="enrollment">Participant enrollment</option>
              <option value="collection_instance">Form instance</option>
              <option value="observation">Observation</option>
            </select>
          </div>

          <div
            v-if="applyScopeKind === 'study'"
            class="field-group"
            data-testid="lock-picker-study-target"
          >
            <span class="picker-label">Study target</span>
            <div class="selected-scope-chip">
              <NodeRefChip
                :node-ref="studyRef"
                :fallback-title="studyCode"
                include-kind
              />
            </div>
          </div>

          <div v-if="needsSitePicker" class="field-group">
            <label for="lock-picker-site">
              {{
                applyScopeKind === "study_site"
                  ? "Study site"
                  : "Filter by study site"
              }}
            </label>
            <select
              id="lock-picker-site"
              v-model="selectedSiteRef"
              data-testid="lock-picker-site"
              @change="onSitePickerChange"
            >
              <option value="">
                {{
                  applyScopeKind === "study_site"
                    ? "Select a study site…"
                    : "All visible sites"
                }}
              </option>
              <option
                v-for="opt in sitePickerOptions"
                :key="opt.key"
                :value="opt.ref"
                :disabled="opt.masked"
              >
                {{ opt.label }}
              </option>
            </select>
          </div>

          <div v-if="needsEnrollmentPicker" class="field-group">
            <label for="lock-picker-enrollment">Participant enrollment</label>
            <select
              id="lock-picker-enrollment"
              v-model="selectedEnrollmentRef"
              data-testid="lock-picker-enrollment"
              :disabled="enrollmentsLoading"
              @change="onEnrollmentPickerChange"
            >
              <option value="">
                {{
                  enrollmentsLoading
                    ? "Loading enrollments…"
                    : "Select a participant enrollment…"
                }}
              </option>
              <option
                v-for="opt in enrollmentPickerOptions"
                :key="opt.key"
                :value="opt.ref"
                :disabled="opt.masked"
              >
                {{ opt.label }}
              </option>
            </select>
          </div>

          <div v-if="needsFormPicker" class="field-group">
            <label for="lock-picker-form">Form instance</label>
            <select
              id="lock-picker-form"
              v-model="selectedFormRef"
              data-testid="lock-picker-form"
              :disabled="!selectedEnrollmentRef || formsLoading"
              @change="onFormPickerChange"
            >
              <option value="">
                {{
                  !selectedEnrollmentRef
                    ? "Choose an enrollment first…"
                    : formsLoading
                      ? "Loading form instances…"
                      : "Select a form instance…"
                }}
              </option>
              <option
                v-for="opt in formPickerOptions"
                :key="opt.key"
                :value="opt.ref"
                :disabled="opt.masked"
              >
                {{ opt.label }}
              </option>
            </select>
          </div>

          <div v-if="needsObservationPicker" class="field-group">
            <label for="lock-picker-observation">Observation</label>
            <select
              id="lock-picker-observation"
              v-model="selectedObservationRef"
              data-testid="lock-picker-observation"
              :disabled="!selectedFormRef || observationsLoading"
            >
              <option value="">
                {{
                  !selectedFormRef
                    ? "Choose a form instance first…"
                    : observationsLoading
                      ? "Loading observations…"
                      : "Select an observation…"
                }}
              </option>
              <option
                v-for="opt in observationPickerOptions"
                :key="opt.key"
                :value="opt.ref"
                :disabled="opt.masked"
              >
                {{ opt.label }}
              </option>
            </select>
          </div>

          <div class="field-group">
            <label for="lock-apply-kind">Assertion</label>
            <select id="lock-apply-kind" v-model="applyKind">
              <option value="Frozen">
                Frozen (blocks entry &amp; corrections; query closure stays
                open)
              </option>
              <option value="Locked">Locked (blocks all changes)</option>
            </select>
          </div>

          <div class="field-group field-group--wide">
            <label for="lock-apply-reason">Reason for change</label>
            <input
              id="lock-apply-reason"
              v-model="applyReason"
              type="text"
              autocomplete="off"
              placeholder="Interim analysis cut / database lock after query closure"
            />
          </div>
        </div>

        <div
          v-if="readinessLoading || readiness"
          class="readiness-panel"
          :class="{
            'readiness-panel--ready': readiness?.ready_for_lock,
            'readiness-panel--blocked': readiness && !readiness.ready_for_lock,
          }"
          data-testid="study-locks-readiness"
        >
          <p v-if="readinessLoading" class="status-line" role="status">
            Checking lock readiness…
          </p>
          <template v-else-if="readiness">
            <div class="readiness-summary">
              <span
                class="readiness-badge"
                :class="
                  readiness.ready_for_lock
                    ? 'readiness-badge--ready'
                    : 'readiness-badge--blocked'
                "
                data-testid="study-locks-readiness-status"
              >
                {{
                  readiness.ready_for_lock
                    ? "Ready for Locked"
                    : "Blocked for Locked · Frozen available"
                }}
              </span>
              <span
                class="readiness-counts"
                data-testid="study-locks-readiness-counts"
              >
                {{ readiness.open_query_count }} open
                {{ readiness.open_query_count === 1 ? "query" : "queries" }} ·
                {{ readiness.missing_required_item_count }} missing required
                {{
                  readiness.missing_required_item_count === 1 ? "item" : "items"
                }}
              </span>
            </div>

            <ul
              v-if="readiness.blockers.length > 0"
              class="readiness-blockers"
              data-testid="study-locks-readiness-blockers"
            >
              <li
                v-for="(blocker, bIdx) in readiness.blockers"
                :key="`${blocker.kind}-${blocker.target_ref ?? bIdx}`"
                class="readiness-blocker-item"
              >
                <template v-if="blocker.masked">
                  <span class="masked-pill">
                    Masked ({{ blocker.mask ?? "Restricted" }})
                  </span>
                  <span v-if="blocker.next_action" class="blocker-action">
                    {{ blocker.next_action }}
                  </span>
                </template>
                <template v-else>
                  <strong v-if="blocker.owner" class="blocker-owner">
                    {{ blocker.owner }}:
                  </strong>
                  <NodeRefChip
                    v-if="blocker.target_ref"
                    :node-ref="blocker.target_ref"
                    include-kind
                  />
                  <span v-if="blocker.detail" class="blocker-detail">
                    {{ blocker.detail }}
                  </span>
                  <span v-if="blocker.next_action" class="blocker-action">
                    — {{ blocker.next_action }}
                  </span>
                </template>
              </li>
            </ul>
          </template>
        </div>

        <div class="apply-actions">
          <button
            id="btn-apply-study-lock"
            type="submit"
            class="btn btn-primary"
            :disabled="isApplyDisabled"
          >
            Sign &amp; apply {{ applyKind }}
          </button>
          <button
            type="button"
            class="btn btn-secondary btn-mini-freeze"
            :disabled="busy || !applyScopeRef.trim()"
            @click="openQuickApplyDialog('Frozen')"
          >
            Quick Freeze…
          </button>
          <button
            type="button"
            class="btn btn-secondary btn-mini-lock"
            :disabled="
              busy ||
              !applyScopeRef.trim() ||
              (readiness !== null && !readiness.ready_for_lock)
            "
            @click="openQuickApplyDialog('Locked')"
          >
            Quick Lock…
          </button>
        </div>
      </form>

      <div v-if="canRelease" class="release-reason-bar">
        <label for="lock-release-reason">Reason when releasing a lock</label>
        <input
          id="lock-release-reason"
          v-model="releaseReason"
          type="text"
          autocomplete="off"
          placeholder="Reopening scope for documented source addendum"
        />
      </div>

      <p
        v-if="outcome"
        id="study-locks-outcome"
        :class="[
          'status-line',
          outcome.kind === 'ok' ? 'ok-banner' : 'error-banner',
        ]"
        :role="outcome.kind === 'ok' ? 'status' : 'alert'"
        aria-live="polite"
      >
        {{ outcome.message }}
      </p>

      <p v-if="locksLoading" class="status-line" role="status">
        Loading locks for {{ studyCode }}…
      </p>
      <div
        v-else-if="locksError"
        id="study-locks-load-error"
        class="status-line error-banner"
        role="alert"
      >
        <p class="status-line">
          {{ studyCode }}'s locks couldn't be loaded.
          <button
            id="btn-retry-locks-list"
            type="button"
            class="btn btn-secondary"
            :disabled="locksLoading"
            @click="reloadLocks"
          >
            Try again
          </button>
        </p>
        <SupportDetails :detail="locksError" />
      </div>
      <p
        v-else-if="locks.length === 0"
        id="study-locks-empty"
        class="status-line"
      >
        No locks recorded on {{ studyCode }} at this point in time.
      </p>
      <table v-else class="locks-table" data-testid="study-locks-table">
        <thead>
          <tr>
            <th scope="col">Kind</th>
            <th scope="col">Scope</th>
            <th scope="col">Participant</th>
            <th scope="col">Signed by</th>
            <th scope="col">Locked at</th>
            <th scope="col">Reason</th>
            <th scope="col">State</th>
            <th v-if="canRelease" scope="col">Action</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(entry, idx) in locks"
            :key="entry.ref ?? `masked-${idx}`"
            :data-masked="entry.masked ? 'true' : undefined"
          >
            <td>
              <span
                :class="[
                  'lock-badge',
                  `lock-badge--${entry.kind.toLowerCase()}`,
                ]"
              >
                {{ entry.kind }}
              </span>
            </td>
            <td>
              <template v-if="entry.masked">
                <span class="masked-pill">
                  Masked ({{ entry.mask ?? "Restricted" }}):
                  {{ scopeLabel(entry.scope_kind) }}
                </span>
              </template>
              <template v-else>
                <NodeRefChip
                  v-if="entry.scope_ref"
                  :node-ref="entry.scope_ref"
                  :fallback-title="entry.subject_code"
                  include-kind
                />
                <span v-else>—</span>
              </template>
            </td>
            <td>
              {{ entry.masked ? "masked" : (entry.subject_code ?? "—") }}
            </td>
            <td>
              <template v-if="entry.masked"> masked </template>
              <template v-else>
                <NodeRefChip
                  v-if="entry.locked_by && parseNodeRef(entry.locked_by)"
                  :node-ref="entry.locked_by"
                />
                <template v-else>{{ entry.locked_by ?? "—" }}</template>
                <span v-if="entry.attestation_meaning" class="meaning-note">
                  ({{ entry.attestation_meaning }})
                </span>
              </template>
            </td>
            <td>{{ entry.masked ? "—" : (entry.locked_at ?? "—") }}</td>
            <td>
              {{ entry.masked ? "masked" : (entry.reason_for_change ?? "—") }}
            </td>
            <td>
              <template v-if="entry.released">
                Released
                <span
                  v-if="!entry.masked && entry.released_at"
                  class="meaning-note"
                >
                  at {{ entry.released_at }}
                </span>
              </template>
              <template v-else> Active </template>
            </td>
            <td v-if="canRelease">
              <button
                v-if="!entry.masked && !entry.released && entry.ref"
                :data-testid="`release-lock-${entry.ref}`"
                type="button"
                class="btn btn-secondary btn-mini-unlock"
                :disabled="busy"
                @click="openReleaseModal(entry.ref, entry.kind)"
              >
                Release<span class="sr-only">
                  lock for
                  {{ entry.subject_code ?? entry.scope_ref ?? entry.ref }}</span
                >
              </button>
            </td>
          </tr>
        </tbody>
      </table>

      <p
        v-if="!canApply && !canRelease"
        id="study-locks-note"
        class="status-line"
      >
        A data manager with electronic signature authority applies or releases
        study locks.
      </p>
    </template>

    <ClinicalModal
      :open="Boolean(quickDialog)"
      :teleport="false"
      :dismissible="!busy"
      :title="quickDialogTitle"
      @close="closeQuickDialog"
    >
      <div class="quick-dialog-body">
        <p class="quick-dialog-target">
          <strong>Study:</strong> {{ studyCode }}
          <template v-if="quickDialog?.mode === 'apply'">
            · <strong>Target scope:</strong>
            <code>{{ applyScopeRef }}</code>
          </template>
          <template v-else-if="quickDialog?.mode === 'release'">
            · <strong>Assertion:</strong>
            <code>{{ quickDialog.lockRef }}</code>
          </template>
        </p>
        <div class="field-group">
          <label for="quick-lock-reason-input">
            GxP reason for change (required)
          </label>
          <textarea
            id="quick-lock-reason-input"
            v-model="quickDialogReason"
            class="quick-dialog-textarea"
            rows="3"
            placeholder="Document the clinical or monitoring rationale for this signed lock action…"
          />
        </div>
      </div>
      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="busy"
          @click="closeQuickDialog"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="busy || !quickDialogReason.trim()"
          @click="confirmQuickDialog"
        >
          Continue to Signature
        </button>
      </template>
    </ClinicalModal>

    <SignatureCaptureModal
      v-if="modalOp"
      :is-open="Boolean(modalOp)"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Data Manager"
      :reasons="LOCK_SIGNING_REASONS"
      :action-url="modalActionUrl"
      :semantic-action="modalSemanticAction"
      :on-sign="executeModalSign"
      @cancel="modalOp = null"
      @success="modalOp = null"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ClinicalModal } from "ui";
import {
  describeLockError,
  kernelApi,
  LOCK_APPLY_ACTION,
  LOCK_APPLY_SEMANTIC_ACTION,
  LOCK_RELEASE_SEMANTIC_ACTION,
  lockReleaseAction,
  type KernelChild,
  type KernelLock,
  type KernelLockKind,
  type KernelLockMeaning,
  type KernelLockReadiness,
} from "../../api/kernelApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";
import { getNodeDisplay, seedNodeSummary } from "../../canvas/nodeResolver";
import { useAuthStore } from "../../stores/auth.js";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import NodeRefChip from "./NodeRefChip.vue";
import SupportDetails from "./SupportDetails.vue";

const LOCK_SIGNING_REASONS = ["Approved", "Responsibility"];

const SCOPE_LABELS: Record<string, string> = {
  study: "study",
  study_site: "site",
  enrollment: "participant",
  collection_instance: "form",
  observation: "observation",
};

interface PickerOption {
  key: string;
  ref: string;
  label: string;
  masked: boolean;
}

const props = defineProps<{
  studyCode: string;
}>();

const authStore = useAuthStore();

const studyRef = ref<string | null>(null);
const studyLoading = ref(false);
const studyError = ref<string | null>(null);

const locks = ref<KernelLock[]>([]);
const locksLoading = ref(false);
const locksError = ref<string | null>(null);
const activeOnly = ref(false);
const asOfInput = ref("");

const canApply = ref(false);
const canRelease = ref(false);
const studyChildren = ref<KernelChild[]>([]);
const siteEnrollments = ref<KernelChild[] | null>(null);
const enrollmentsLoading = ref(false);
const enrollmentForms = ref<KernelChild[]>([]);
const formsLoading = ref(false);
const formObservations = ref<KernelChild[]>([]);
const observationsLoading = ref(false);

const applyScopeKind = ref<string>("study");
const selectedSiteRef = ref("");
const selectedEnrollmentRef = ref("");
const selectedFormRef = ref("");
const selectedObservationRef = ref("");

const applyKind = ref<KernelLockKind>("Frozen");
const applyReason = ref("");
const releaseReason = ref("");
const busy = ref(false);
const outcome = ref<{ kind: "ok" | "error"; message: string } | null>(null);

const readiness = ref<KernelLockReadiness | null>(null);
const readinessLoading = ref(false);

type ModalOperation =
  | {
      mode: "apply";
      idempotencyKey: string;
      lockId: string;
      attestationId: string;
    }
  | {
      mode: "release";
      lockRef: string;
      lockKind: string;
      idempotencyKey: string;
      assertionId: string;
      attestationId: string;
    };

const modalOp = ref<ModalOperation | null>(null);

type QuickDialogState =
  | { mode: "apply"; kind: KernelLockKind }
  | { mode: "release"; lockRef: string; lockKind: string };

const quickDialog = ref<QuickDialogState | null>(null);
const quickDialogReason = ref("");

const quickDialogTitle = computed(() => {
  if (!quickDialog.value) return "Clinical Lock Justification";
  if (quickDialog.value.mode === "release") {
    return `Release ${quickDialog.value.lockKind} Assertion`;
  }
  return quickDialog.value.kind === "Frozen"
    ? "Soft Freeze Data Scope"
    : "Apply Cryptographic Hard Lock";
});

const signerUsername = computed(
  () => authStore?.identity?.username ?? authStore?.user?.username ?? ""
);
const signerFullName = computed(() => {
  const identity = authStore?.identity ?? authStore?.user;
  if (!identity) return "";
  const fullName = [identity.firstName, identity.lastName]
    .filter(Boolean)
    .join(" ")
    .trim();
  return fullName || identity.username || "";
});

function unversionedNodeRef(raw: string | null | undefined): string {
  if (!raw) return "";
  const parsed = parseNodeRef(raw);
  if (parsed) return `${parsed.kind}:${parsed.id}`;
  const at = raw.indexOf("@");
  return at < 0 ? raw : raw.slice(0, at);
}

function seedVisibleChildren(children: KernelChild[]) {
  for (const child of children) {
    if (child.node.ref && !child.node.mask) {
      const summary = {
        kind: child.node.kind,
        code: child.node.code,
        title: child.node.title,
        status: child.node.status,
      };
      seedNodeSummary(child.node.ref, summary);
      const unversioned = unversionedNodeRef(child.node.ref);
      if (unversioned && unversioned !== child.node.ref) {
        seedNodeSummary(unversioned, summary);
      }
    }
  }
}

const needsSitePicker = computed(() => applyScopeKind.value !== "study");
const needsEnrollmentPicker = computed(
  () =>
    applyScopeKind.value === "enrollment" ||
    applyScopeKind.value === "collection_instance" ||
    applyScopeKind.value === "observation"
);
const needsFormPicker = computed(
  () =>
    applyScopeKind.value === "collection_instance" ||
    applyScopeKind.value === "observation"
);
const needsObservationPicker = computed(
  () => applyScopeKind.value === "observation"
);

function buildPickerOptions(
  children: KernelChild[],
  expectedKind: string
): PickerOption[] {
  return children
    .filter((child) => child.node.kind === expectedKind)
    .map((child, idx) => {
      if (child.node.mask || !child.node.ref) {
        return {
          key: `masked-${expectedKind}-${idx}`,
          ref: `__masked_${expectedKind}_${idx}__`,
          label: `Masked (${child.node.mask ?? "Restricted"}): ${scopeLabel(expectedKind)}`,
          masked: true,
        };
      }
      const cleanRef = unversionedNodeRef(child.node.ref);
      const primary = child.node.code || child.node.title || cleanRef;
      const detail =
        child.node.title &&
        child.node.code &&
        child.node.title !== child.node.code
          ? ` — ${child.node.title}`
          : "";
      const statusSuffix = child.node.status ? ` (${child.node.status})` : "";
      return {
        key: cleanRef,
        ref: cleanRef,
        label: `${primary}${detail}${statusSuffix}`,
        masked: false,
      };
    });
}

const sitePickerOptions = computed(() =>
  buildPickerOptions(studyChildren.value, "study_site")
);

const enrollmentPickerOptions = computed(() => {
  const source =
    selectedSiteRef.value && siteEnrollments.value !== null
      ? siteEnrollments.value
      : studyChildren.value;
  return buildPickerOptions(source, "enrollment");
});

const STANDALONE_OBSERVATIONS_REF = "__standalone_observations__";

const formPickerOptions = computed(() => {
  const forms = buildPickerOptions(
    enrollmentForms.value,
    "collection_instance"
  );
  if (
    applyScopeKind.value === "observation" &&
    enrollmentForms.value.some((child) => child.node.kind === "observation")
  ) {
    return [
      ...forms,
      {
        key: STANDALONE_OBSERVATIONS_REF,
        ref: STANDALONE_OBSERVATIONS_REF,
        label: "Standalone observations (not in a form)",
        masked: false,
      },
    ];
  }
  return forms;
});

const observationPickerOptions = computed(() => {
  if (selectedFormRef.value === STANDALONE_OBSERVATIONS_REF) {
    return buildPickerOptions(enrollmentForms.value, "observation");
  }
  return buildPickerOptions(formObservations.value, "observation");
});

const applyScopeRef = computed(() => {
  switch (applyScopeKind.value) {
    case "study":
      return studyRef.value ?? "";
    case "study_site":
      return selectedSiteRef.value.startsWith("study_site:")
        ? selectedSiteRef.value
        : "";
    case "enrollment":
      return selectedEnrollmentRef.value.startsWith("enrollment:")
        ? selectedEnrollmentRef.value
        : "";
    case "collection_instance":
      return selectedFormRef.value.startsWith("collection_instance:")
        ? selectedFormRef.value
        : "";
    case "observation":
      return selectedObservationRef.value.startsWith("observation:")
        ? selectedObservationRef.value
        : "";
    default:
      return "";
  }
});

const isLockedBlockedByReadiness = computed(
  () =>
    applyKind.value === "Locked" &&
    readiness.value !== null &&
    !readiness.value.ready_for_lock
);

const isApplyDisabled = computed(
  () =>
    busy.value ||
    !applyScopeRef.value.trim() ||
    !applyReason.value.trim() ||
    isLockedBlockedByReadiness.value
);

const modalActionUrl = computed(() => {
  if (!modalOp.value) return LOCK_APPLY_ACTION;
  return modalOp.value.mode === "apply"
    ? LOCK_APPLY_ACTION
    : lockReleaseAction(modalOp.value.lockRef);
});

const modalSemanticAction = computed(() =>
  modalOp.value?.mode === "release"
    ? LOCK_RELEASE_SEMANTIC_ACTION
    : LOCK_APPLY_SEMANTIC_ACTION
);

function scopeLabel(kind: string): string {
  return SCOPE_LABELS[kind] ?? kind;
}

function firstUnmaskedRef(options: PickerOption[]): string {
  return options.find((opt) => !opt.masked)?.ref ?? "";
}

let siteSeq = 0;
let enrollmentSeq = 0;
let formSeq = 0;
let readinessSeq = 0;

async function loadEnrollmentsForSelectedSite(siteRef: string) {
  const seq = ++siteSeq;
  selectedEnrollmentRef.value = "";
  selectedFormRef.value = "";
  selectedObservationRef.value = "";
  enrollmentForms.value = [];
  formObservations.value = [];

  if (!siteRef || typeof kernelApi.getChildren !== "function") {
    siteEnrollments.value = null;
    if (needsEnrollmentPicker.value) {
      const autoEnrollment = firstUnmaskedRef(enrollmentPickerOptions.value);
      if (autoEnrollment) {
        selectedEnrollmentRef.value = autoEnrollment;
        await onEnrollmentPickerChange();
      }
    }
    return;
  }

  enrollmentsLoading.value = true;
  try {
    const children = await kernelApi.getChildren(unversionedNodeRef(siteRef));
    if (seq !== siteSeq) return;
    siteEnrollments.value = children ?? [];
    seedVisibleChildren(siteEnrollments.value);
    if (needsEnrollmentPicker.value) {
      const autoEnrollment = firstUnmaskedRef(enrollmentPickerOptions.value);
      if (autoEnrollment) {
        selectedEnrollmentRef.value = autoEnrollment;
        await onEnrollmentPickerChange();
      }
    }
  } catch {
    if (seq === siteSeq) siteEnrollments.value = [];
  } finally {
    if (seq === siteSeq) enrollmentsLoading.value = false;
  }
}

async function loadFormsForSelectedEnrollment(enrollmentRef: string) {
  const seq = ++enrollmentSeq;
  selectedFormRef.value = "";
  selectedObservationRef.value = "";
  enrollmentForms.value = [];
  formObservations.value = [];

  if (
    !enrollmentRef ||
    !needsFormPicker.value ||
    typeof kernelApi.getChildren !== "function"
  ) {
    return;
  }

  formsLoading.value = true;
  try {
    const children = await kernelApi.getChildren(
      unversionedNodeRef(enrollmentRef)
    );
    if (seq !== enrollmentSeq) return;
    enrollmentForms.value = children ?? [];
    seedVisibleChildren(enrollmentForms.value);
    const autoForm = firstUnmaskedRef(formPickerOptions.value);
    if (autoForm) {
      selectedFormRef.value = autoForm;
      await onFormPickerChange();
    }
  } catch {
    if (seq === enrollmentSeq) enrollmentForms.value = [];
  } finally {
    if (seq === enrollmentSeq) formsLoading.value = false;
  }
}

async function loadObservationsForSelectedForm(formRef: string) {
  const seq = ++formSeq;
  selectedObservationRef.value = "";
  formObservations.value = [];

  if (
    !formRef ||
    !needsObservationPicker.value ||
    typeof kernelApi.getChildren !== "function"
  ) {
    return;
  }

  if (formRef === STANDALONE_OBSERVATIONS_REF) {
    const autoStandalone = firstUnmaskedRef(observationPickerOptions.value);
    if (autoStandalone) {
      selectedObservationRef.value = autoStandalone;
    }
    return;
  }

  observationsLoading.value = true;
  try {
    const children = await kernelApi.getChildren(unversionedNodeRef(formRef));
    if (seq !== formSeq) return;
    formObservations.value = children ?? [];
    seedVisibleChildren(formObservations.value);
    const autoObs = firstUnmaskedRef(observationPickerOptions.value);
    if (autoObs) {
      selectedObservationRef.value = autoObs;
    }
  } catch {
    if (seq === formSeq) formObservations.value = [];
  } finally {
    if (seq === formSeq) observationsLoading.value = false;
  }
}

async function onScopeKindChange() {
  if (applyScopeKind.value === "study") {
    return;
  }
  if (applyScopeKind.value === "study_site") {
    if (!selectedSiteRef.value) {
      selectedSiteRef.value = firstUnmaskedRef(sitePickerOptions.value);
    }
    return;
  }
  if (!selectedEnrollmentRef.value) {
    const firstEnrollment = firstUnmaskedRef(enrollmentPickerOptions.value);
    if (firstEnrollment) {
      selectedEnrollmentRef.value = firstEnrollment;
    } else if (!selectedSiteRef.value) {
      const firstSite = firstUnmaskedRef(sitePickerOptions.value);
      if (firstSite) {
        selectedSiteRef.value = firstSite;
        await loadEnrollmentsForSelectedSite(firstSite);
        return;
      }
    }
  }
  if (needsFormPicker.value && selectedEnrollmentRef.value) {
    await loadFormsForSelectedEnrollment(selectedEnrollmentRef.value);
  }
}

async function onSitePickerChange() {
  if (needsEnrollmentPicker.value) {
    await loadEnrollmentsForSelectedSite(selectedSiteRef.value);
  }
}

async function onEnrollmentPickerChange() {
  if (needsFormPicker.value) {
    await loadFormsForSelectedEnrollment(selectedEnrollmentRef.value);
  }
}

async function onFormPickerChange() {
  if (needsObservationPicker.value) {
    await loadObservationsForSelectedForm(selectedFormRef.value);
  }
}

async function reloadReadiness() {
  const seq = ++readinessSeq;
  const currentStudy = studyRef.value;
  const targetScope = applyScopeRef.value.trim();
  if (
    !currentStudy ||
    !targetScope ||
    !canApply.value ||
    typeof kernelApi.getStudyLockReadiness !== "function"
  ) {
    readiness.value = null;
    readinessLoading.value = false;
    return;
  }

  readinessLoading.value = true;
  try {
    const result = await kernelApi.getStudyLockReadiness(
      currentStudy,
      targetScope
    );
    if (seq !== readinessSeq) return;
    readiness.value = result ?? null;
  } catch {
    if (seq === readinessSeq) readiness.value = null;
  } finally {
    if (seq === readinessSeq) readinessLoading.value = false;
  }
}

watch([studyRef, applyScopeRef, canApply], () => {
  void reloadReadiness();
});
let loadSeq = 0;

async function reloadLocks() {
  if (!studyRef.value || typeof kernelApi.listStudyLocks !== "function") return;
  locksLoading.value = true;
  locksError.value = null;
  try {
    const res = await kernelApi.listStudyLocks(studyRef.value, {
      activeOnly: activeOnly.value || undefined,
      asOf: asOfInput.value.trim() || undefined,
    });
    locks.value = res.locks ?? [];
  } catch (err) {
    locksError.value = err instanceof Error ? err.message : String(err);
  } finally {
    locksLoading.value = false;
  }
}

async function loadStudyLocksContext(code: string) {
  const seq = ++loadSeq;
  studyRef.value = null;
  studyError.value = null;
  locks.value = [];
  locksError.value = null;
  studyChildren.value = [];
  siteEnrollments.value = null;
  enrollmentForms.value = [];
  formObservations.value = [];
  selectedSiteRef.value = "";
  selectedEnrollmentRef.value = "";
  selectedFormRef.value = "";
  selectedObservationRef.value = "";
  readiness.value = null;
  outcome.value = null;
  if (!code) return;

  studyLoading.value = true;
  try {
    const resolved = await findKernelStudyRef(code);
    if (seq !== loadSeq) return;
    studyRef.value = resolved;
    if (!resolved) return;
    seedNodeSummary(resolved, { kind: "study", code });

    applyScopeKind.value = "study";

    const tasks: Promise<unknown>[] = [reloadLocks()];
    if (typeof kernelApi.canI === "function") {
      tasks.push(
        Promise.all([
          kernelApi.canI("lock.apply", resolved),
          kernelApi.canI("lock.release", resolved),
        ]).then(([applyDec, releaseDec]) => {
          if (seq !== loadSeq) return;
          canApply.value = Boolean(applyDec?.permitted);
          canRelease.value = Boolean(releaseDec?.permitted);
        })
      );
    }
    if (typeof kernelApi.getChildren === "function") {
      tasks.push(
        kernelApi
          .getChildren(resolved)
          .then((children) => {
            if (seq !== loadSeq) return;
            studyChildren.value = children ?? [];
            seedVisibleChildren(studyChildren.value);
          })
          .catch(() => undefined)
      );
    }
    await Promise.all(tasks);
  } catch (err) {
    if (seq !== loadSeq) return;
    studyError.value = err instanceof Error ? err.message : String(err);
  } finally {
    if (seq === loadSeq) studyLoading.value = false;
  }
}

watch(
  () => props.studyCode,
  (code) => {
    void loadStudyLocksContext(code);
  },
  { immediate: true }
);

function openQuickApplyDialog(kind: KernelLockKind) {
  if (busy.value || !applyScopeRef.value.trim()) return;
  if (
    kind === "Locked" &&
    readiness.value !== null &&
    !readiness.value.ready_for_lock
  ) {
    return;
  }
  applyKind.value = kind;
  quickDialogReason.value = applyReason.value;
  quickDialog.value = { mode: "apply", kind };
}

function closeQuickDialog() {
  quickDialog.value = null;
}

function confirmQuickDialog() {
  const state = quickDialog.value;
  const reason = quickDialogReason.value.trim();
  if (!state || !reason) return;
  quickDialog.value = null;
  if (state.mode === "apply") {
    applyKind.value = state.kind;
    applyReason.value = reason;
    openApplyModal();
  } else {
    releaseReason.value = reason;
    openReleaseModal(state.lockRef, state.lockKind);
  }
}

function openApplyModal() {
  if (isApplyDisabled.value) return;
  outcome.value = null;
  modalOp.value = {
    mode: "apply",
    idempotencyKey: globalThis.crypto.randomUUID(),
    lockId: globalThis.crypto.randomUUID(),
    attestationId: globalThis.crypto.randomUUID(),
  };
}

function openReleaseModal(lockRef: string, lockKind: string) {
  if (!releaseReason.value.trim()) {
    quickDialogReason.value = "";
    quickDialog.value = { mode: "release", lockRef, lockKind };
    return;
  }
  outcome.value = null;
  modalOp.value = {
    mode: "release",
    lockRef,
    lockKind,
    idempotencyKey: globalThis.crypto.randomUUID(),
    assertionId: globalThis.crypto.randomUUID(),
    attestationId: globalThis.crypto.randomUUID(),
  };
}

async function executeModalSign(sigToken: string, signingReason: string) {
  const op = modalOp.value;
  if (!op || !studyRef.value) return { kind: "signed" as const };
  const meaning: KernelLockMeaning =
    signingReason === "Responsibility" ? "Responsibility" : "Approved";
  busy.value = true;
  try {
    if (op.mode === "apply") {
      await kernelApi.applyLock({
        studyRef: studyRef.value,
        scopeRef: applyScopeRef.value.trim(),
        kind: applyKind.value,
        meaning,
        lockId: op.lockId,
        attestationId: op.attestationId,
        sigToken,
        reason: applyReason.value.trim(),
        idempotencyKey: op.idempotencyKey,
      });
      const scopeDisplay = getNodeDisplay(applyScopeRef.value.trim());
      outcome.value = {
        kind: "ok",
        message: `${applyKind.value} applied to ${scopeDisplay.label}.`,
      };
      applyReason.value = "";
    } else {
      await kernelApi.releaseLock({
        lockRef: op.lockRef,
        studyRef: studyRef.value,
        meaning,
        assertionId: op.assertionId,
        attestationId: op.attestationId,
        sigToken,
        reason: releaseReason.value.trim(),
        idempotencyKey: op.idempotencyKey,
      });
      outcome.value = {
        kind: "ok",
        message: `${op.lockKind} released.`,
      };
      releaseReason.value = "";
    }
    await Promise.all([reloadLocks(), reloadReadiness()]);
    return { kind: "signed" as const };
  } catch (err) {
    const message = describeLockError(err);
    outcome.value = { kind: "error", message };
    throw new Error(message, { cause: err });
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.study-locks {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.study-locks-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-end;
  gap: 12px;
}

.study-locks-header h2 {
  margin: 0;
  font-size: 1.2rem;
}

.study-locks-subtitle {
  margin: 4px 0 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
}

.study-locks-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
}

.checkbox-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.apply-card {
  padding: 14px 16px;
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 8px;
  background: var(--semantic-color-surface-muted);
}

.apply-card h3 {
  margin: 0 0 10px;
  font-size: 0.95rem;
}

.apply-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 10px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field-group--wide {
  grid-column: 1 / -1;
}

.field-group label,
.picker-label,
.release-reason-bar label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.selected-scope-chip {
  display: flex;
  align-items: center;
  min-height: 32px;
}

.field-group input,
.field-group select,
.release-reason-bar input,
.study-locks-filters input[type="text"] {
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.readiness-panel {
  margin-top: 12px;
  padding: 10px 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface);
  font-size: 0.83rem;
}

.readiness-panel--ready {
  border-color: var(--semantic-color-success-dark);
}

.readiness-panel--blocked {
  border-color: var(--semantic-color-error-dark);
}

.readiness-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.readiness-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.76rem;
  font-weight: 600;
}

.readiness-badge--ready {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.readiness-badge--blocked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}

.readiness-counts {
  color: var(--semantic-color-text-body);
  font-variant-numeric: tabular-nums;
}

.readiness-blockers {
  margin: 8px 0 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.readiness-blocker-item {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.blocker-owner {
  color: var(--semantic-color-text-dark);
}

.blocker-action {
  color: var(--semantic-color-text-body);
}

.apply-actions {
  margin-top: 10px;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.quick-dialog-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.quick-dialog-target {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
}

.quick-dialog-textarea {
  box-sizing: border-box;
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  font-family: inherit;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.release-reason-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.release-reason-bar input {
  flex: 1 1 260px;
}

.locks-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  font-variant-numeric: tabular-nums;
}

.locks-table th,
.locks-table td {
  padding: 8px 10px;
  border-bottom: 1px solid var(--semantic-color-border);
  text-align: left;
}

.locks-table th {
  font-weight: 600;
  color: var(--semantic-color-text-dark);
  background: var(--semantic-color-surface-muted);
}

.lock-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 600;
}

.lock-badge--frozen {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
}

.lock-badge--locked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}

.masked-pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-size: 0.8rem;
}

.meaning-note {
  color: var(--semantic-color-text-muted);
  font-size: 0.8rem;
}

.status-line {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
}

.ok-banner {
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.error-banner {
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}
</style>
