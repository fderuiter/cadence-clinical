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
      <p class="status-line">{{ studyCode }}'s locks couldn't be loaded.</p>
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

          <div class="field-group">
            <label for="lock-apply-scope-ref">Target reference</label>
            <input
              id="lock-apply-scope-ref"
              v-model="applyScopeRef"
              type="text"
              autocomplete="off"
              list="lock-known-scopes"
              :readonly="applyScopeKind === 'study'"
              placeholder="kind:uuid"
            />
            <datalist id="lock-known-scopes">
              <option
                v-for="opt in scopeSuggestions"
                :key="opt.ref"
                :value="opt.ref"
              >
                {{ opt.label }}
              </option>
            </datalist>
          </div>

          <div class="field-group">
            <label for="lock-apply-kind">Assertion</label>
            <select id="lock-apply-kind" v-model="applyKind">
              <option value="Frozen">
                Frozen (blocks entry & corrections)
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
        <div class="apply-actions">
          <button
            id="btn-apply-study-lock"
            type="submit"
            class="btn btn-primary"
            :disabled="busy || !applyScopeRef.trim() || !applyReason.trim()"
          >
            Sign &amp; apply {{ applyKind }}
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
        <p class="status-line">{{ studyCode }}'s locks couldn't be loaded.</p>
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
                class="btn btn-secondary"
                :disabled="busy || !releaseReason.trim()"
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

    <SignatureCaptureModal
      v-if="modalOp"
      v-bind="signatureModalProps"
      @cancel="closeModal"
      @success="closeModal"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from "vue";
import {
  kernelApi,
  type KernelChild,
  type KernelLock,
  type KernelLockKind,
} from "../../api/kernelApi";
import { fetchStudyLocks } from "../../canvas/kernelLocks";
import { parseNodeRef } from "../../canvas/nodeRef";
import { getNodeDisplay, seedNodeSummary } from "../../canvas/nodeResolver";
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

const props = defineProps<{
  studyCode: string;
}>();

const locks = ref<KernelLock[]>([]);
const locksLoading = ref(false);
const locksError = ref<string | null>(null);
const activeOnly = ref(false);
const asOfInput = ref("");

const studyChildren = ref<KernelChild[]>([]);

const applyScopeKind = ref<string>("study");
const applyScopeRef = ref<string>("");
const applyKind = ref<KernelLockKind>("Frozen");
const applyReason = ref("");
const releaseReason = ref("");

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
  busy,
  outcome,
  modalOp,
  signatureModalProps,
  probePermissions,
  openApply,
  openRelease,
  closeModal,
} = useSignedKernelLock({
  studyRef: () => studyRef.value,
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

const { studyRef, studyLoading, studyError } = useKernelStudyRef(
  () => props.studyCode,
  {
    onReset: () => {
      locks.value = [];
      studyChildren.value = [];
      outcome.value = null;
      closeModal();
      void probePermissions(null);
    },
    onResolved: async (resolved, { code, isCurrent }) => {
      seedNodeSummary(resolved, { kind: "study", code });
      applyScopeKind.value = "study";
      applyScopeRef.value = resolved;

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
              for (const child of studyChildren.value) {
                if (child.node.ref && !child.node.mask) {
                  seedNodeSummary(child.node.ref, {
                    kind: child.node.kind,
                    code: child.node.code,
                    title: child.node.title,
                    status: child.node.status,
                  });
                }
              }
            })
            .catch(() => undefined)
        );
      }
      await Promise.all(tasks);
    },
  }
);

const scopeSuggestions = computed(() => {
  const items: Array<{ ref: string; label: string }> = [];
  if (studyRef.value) {
    items.push({ ref: studyRef.value, label: `Study ${props.studyCode}` });
  }
  for (const child of studyChildren.value) {
    if (child.node.ref && !child.node.mask) {
      items.push({
        ref: child.node.ref,
        label: child.node.code || child.node.title || child.node.ref,
      });
    }
  }
  return items.filter((item) =>
    item.ref.startsWith(`${applyScopeKind.value}:`)
  );
});

function scopeLabel(kind: string): string {
  return SCOPE_LABELS[kind] ?? kind;
}

function onScopeKindChange() {
  if (applyScopeKind.value === "study" && studyRef.value) {
    applyScopeRef.value = studyRef.value;
  } else {
    const first = scopeSuggestions.value[0];
    applyScopeRef.value = first ? first.ref : "";
  }
}

function openApplyModal() {
  const scopeRef = applyScopeRef.value.trim();
  openApply({
    scopeRef,
    kind: applyKind.value,
    reason: applyReason.value,
    targetLabel: () => getNodeDisplay(scopeRef).label,
  });
}

function openReleaseModal(lockRef: string, lockKind: string) {
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
.release-reason-bar label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
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

.apply-actions {
  margin-top: 10px;
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
</style>

<style scoped src="./canvasBanners.css"></style>
