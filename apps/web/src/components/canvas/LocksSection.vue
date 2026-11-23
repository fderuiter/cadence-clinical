<template>
  <section
    id="study-locks-section"
    class="study-locks"
    aria-labelledby="study-locks-title"
    :aria-busy="studyLoading || locksLoading"
  >
    <header class="study-locks-header">
      <div>
        <h2 id="study-locks-title" tabindex="-1">Locks</h2>
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
          @click="reloadStudy()"
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
                class="clinical-badge readiness-badge"
                :class="
                  readiness.ready_for_lock
                    ? 'clinical-badge--verified readiness-badge--ready'
                    : 'clinical-badge--error readiness-badge--blocked'
                "
                data-testid="study-locks-readiness-status"
              >
                <svg
                  v-if="readiness.ready_for_lock"
                  xmlns="http://www.w3.org/2000/svg"
                  width="14"
                  height="14"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  aria-hidden="true"
                >
                  <circle cx="12" cy="12" r="10" />
                  <path d="m9 12 2 2 4-4" />
                </svg>
                <svg
                  v-else
                  xmlns="http://www.w3.org/2000/svg"
                  width="14"
                  height="14"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  aria-hidden="true"
                >
                  <path
                    d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"
                  />
                  <path d="M12 9v4" />
                  <path d="M12 17h.01" />
                </svg>
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
                  readiness.missing_required_item_count === 1
                    ? "item"
                    : "items"
                }}<template
                  v-if="Number(readiness.open_error_finding_count ?? 0) > 0"
                >
                  · {{ readiness.open_error_finding_count }} error
                  {{
                    Number(readiness.open_error_finding_count) === 1
                      ? "finding"
                      : "findings"
                  }}
                </template>
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
                  <span
                    v-if="formatLockBlockerDetail(blocker)"
                    class="blocker-detail"
                  >
                    {{ formatLockBlockerDetail(blocker) }}
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
                  'clinical-badge',
                  'lock-badge',
                  entry.kind.toLowerCase() === 'locked'
                    ? 'clinical-badge--locked'
                    : 'clinical-badge--frozen',
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
                  v-if="
                    entry.locked_by && normalizeLookupNodeRef(entry.locked_by)
                  "
                  :node-ref="normalizeLookupNodeRef(entry.locked_by)!"
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
      v-bind="signatureModalProps"
      @cancel="closeModal"
      @success="closeModal"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ClinicalModal } from "ui";
import {
  formatLockBlockerDetail,
  kernelApi,
  type KernelChild,
  type KernelLock,
  type KernelLockKind,
} from "../../api/kernelApi";
import { fetchStudyLocks } from "../../canvas/kernelLocks";
import { parseNodeRef } from "../../canvas/nodeRef";
import {
  getNodeDisplay,
  normalizeLookupNodeRef,
  seedNodeSummary,
} from "../../canvas/nodeResolver";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import { useSignedKernelLock } from "../../composables/useSignedKernelLock";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import NodeRefChip from "./NodeRefChip.vue";
import SupportDetails from "./SupportDetails.vue";

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

type QuickDialogState =
  | { mode: "apply"; kind: KernelLockKind }
  | { mode: "release"; lockRef: string; lockKind: string };

const props = defineProps<{
  studyCode: string;
}>();

const locks = ref<KernelLock[]>([]);
const locksLoading = ref(false);
const locksError = ref<string | null>(null);
const activeOnly = ref(false);
const asOfInput = ref("");

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

let siteSeq = 0;
let enrollmentSeq = 0;
let formSeq = 0;

function scopeLabel(kind: string): string {
  return SCOPE_LABELS[kind] ?? kind;
}

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

async function reloadLocks() {
  if (!studyRef.value || typeof kernelApi.listStudyLocks !== "function") return;
  locksLoading.value = true;
  locksError.value = null;
  try {
    locks.value = await fetchStudyLocks(studyRef.value, {
      activeOnly: activeOnly.value || undefined,
      asOf: asOfInput.value.trim() || undefined,
    });
  } catch (err) {
    locksError.value = err instanceof Error ? err.message : String(err);
  } finally {
    locksLoading.value = false;
  }
}

const {
  canApply,
  canRelease,
  readiness,
  readinessLoading,
  isLockBlockedByReadiness,
  busy,
  outcome,
  modalOp,
  signatureModalProps,
  probePermissions,
  reloadReadiness,
  openApply,
  openRelease,
  closeModal,
} = useSignedKernelLock({
  studyRef: () => studyRef.value,
  scopeRef: () => applyScopeRef.value,
  watchPermissions: false,
  onApplied: async () => {
    applyReason.value = "";
    await reloadLocks();
  },
  onReleased: async () => {
    releaseReason.value = "";
    await reloadLocks();
  },
});

const {
  studyRef,
  studyLoading,
  studyError,
  reload: reloadStudy,
} = useKernelStudyRef(() => props.studyCode, {
  onReset: () => {
    siteSeq++;
    enrollmentSeq++;
    formSeq++;
    locks.value = [];
    locksError.value = null;
    studyChildren.value = [];
    siteEnrollments.value = null;
    enrollmentForms.value = [];
    formObservations.value = [];
    enrollmentsLoading.value = false;
    formsLoading.value = false;
    observationsLoading.value = false;
    selectedSiteRef.value = "";
    selectedEnrollmentRef.value = "";
    selectedFormRef.value = "";
    selectedObservationRef.value = "";
    quickDialog.value = null;
    outcome.value = null;
    closeModal();
    void probePermissions(null);
  },
  onResolved: async (resolved, { code, isCurrent }) => {
    seedNodeSummary(resolved, { kind: "study", code });
    const unversionedStudy = unversionedNodeRef(resolved);
    if (unversionedStudy && unversionedStudy !== resolved) {
      seedNodeSummary(unversionedStudy, { kind: "study", code });
    }
    applyScopeKind.value = "study";

    const tasks: Promise<unknown>[] = [
      reloadLocks(),
      probePermissions(resolved),
    ];
    if (typeof kernelApi.getChildren === "function") {
      tasks.push(
        kernelApi
          .getChildren(resolved)
          .then((children) => {
            if (!isCurrent()) return;
            studyChildren.value = children ?? [];
            seedVisibleChildren(studyChildren.value);
          })
          .catch(() => undefined)
      );
    }
    await Promise.all(tasks);
  },
});

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
      return unversionedNodeRef(studyRef.value);
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
  () => applyKind.value === "Locked" && isLockBlockedByReadiness.value
);

const isApplyDisabled = computed(
  () =>
    busy.value ||
    !applyScopeRef.value.trim() ||
    !applyReason.value.trim() ||
    isLockedBlockedByReadiness.value
);

watch([studyRef, applyScopeRef, canApply], () => {
  void reloadReadiness();
});

function firstUnmaskedRef(options: PickerOption[]): string {
  return options.find((opt) => !opt.masked)?.ref ?? "";
}

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

function openQuickApplyDialog(kind: KernelLockKind) {
  if (busy.value || !applyScopeRef.value.trim()) return;
  if (kind === "Locked" && isLockBlockedByReadiness.value) {
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
  const scopeRef = applyScopeRef.value.trim();
  openApply({
    scopeRef,
    kind: applyKind.value,
    reason: applyReason.value,
    targetLabel: () => getNodeDisplay(scopeRef).label,
  });
}

function openReleaseModal(lockRef: string, lockKind: string) {
  if (!releaseReason.value.trim()) {
    quickDialogReason.value = "";
    quickDialog.value = { mode: "release", lockRef, lockKind };
    return;
  }
  openRelease({
    lockRef,
    lockKind,
    reason: releaseReason.value,
  });
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
  min-height: var(--semantic-control-target-min, 24px);
  gap: 6px;
  cursor: pointer;
}

.checkbox-label input[type="checkbox"] {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  min-height: var(--semantic-control-target-min, 24px);
  margin: 0;
  cursor: pointer;
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
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
  box-sizing: border-box;
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
</style>

<style scoped src="./canvasBanners.css"></style>
