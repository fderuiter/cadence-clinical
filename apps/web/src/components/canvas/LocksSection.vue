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

          <div v-if="applyScopeKind === 'study'" class="field-group">
            <span class="field-label">Target scope</span>
            <span class="scope-static-value" data-testid="lock-study-target">
              Study {{ studyCode }}
            </span>
          </div>

          <div
            v-if="
              applyScopeKind === 'study_site' ||
              applyScopeKind === 'enrollment' ||
              applyScopeKind === 'collection_instance'
            "
            class="field-group"
          >
            <label for="lock-apply-site">Study site</label>
            <select
              id="lock-apply-site"
              v-model="selectedSiteRef"
              @change="onSiteSelectChange"
            >
              <option value="">Select study site…</option>
              <option
                v-for="site in siteOptions"
                :key="site.ref"
                :value="site.ref"
              >
                {{ site.label }}
              </option>
            </select>
          </div>

          <div
            v-if="
              applyScopeKind === 'enrollment' ||
              applyScopeKind === 'collection_instance'
            "
            class="field-group"
          >
            <label for="lock-apply-enrollment">Participant</label>
            <select
              id="lock-apply-enrollment"
              v-model="selectedEnrollmentRef"
              :disabled="!selectedSiteRef || enrollmentsLoading"
              @change="onEnrollmentSelectChange"
            >
              <option value="">
                {{
                  enrollmentsLoading
                    ? "Loading participants…"
                    : "Select participant…"
                }}
              </option>
              <option
                v-for="enr in enrollmentOptions"
                :key="enr.ref"
                :value="enr.ref"
              >
                {{ enr.label }}
              </option>
            </select>
          </div>

          <div
            v-if="applyScopeKind === 'collection_instance'"
            class="field-group"
          >
            <label for="lock-apply-form">Case report form</label>
            <select
              id="lock-apply-form"
              v-model="selectedFormRef"
              :disabled="!selectedEnrollmentRef || formsLoading"
              @change="onFormSelectChange"
            >
              <option value="">
                {{ formsLoading ? "Loading forms…" : "Select form…" }}
              </option>
              <option
                v-for="frm in formOptions"
                :key="frm.ref"
                :value="frm.ref"
              >
                {{ frm.label }}
              </option>
            </select>
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

          <details
            class="direct-ref-disclosure field-group--wide"
            :open="applyScopeKind === 'observation' ? true : undefined"
          >
            <summary class="direct-ref-summary">
              Enter target reference ID directly
            </summary>
            <div class="field-group direct-ref-field">
              <label for="lock-apply-scope-ref">Target reference ID</label>
              <input
                id="lock-apply-scope-ref"
                v-model="applyScopeRef"
                type="text"
                autocomplete="off"
                :readonly="applyScopeKind === 'study'"
                placeholder="Paste target reference ID"
              />
            </div>
          </details>
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
                class="btn btn-secondary"
                :disabled="busy || !releaseReason.trim()"
                @click="openReleaseModal(entry.ref, entry.kind)"
              >
                Release
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
} from "../../api/kernelApi";
import { ecrfFormsApi, type EcrfFormSummary } from "../../api/ecrfFormsApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";
import {
  getNodeDisplay,
  normalizeLookupNodeRef,
  seedNodeSummary,
} from "../../canvas/nodeResolver";
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
const selectedSiteRef = ref("");
const selectedEnrollmentRef = ref("");
const selectedFormRef = ref("");
const siteEnrollments = ref<KernelChild[]>([]);
const enrollmentsLoading = ref(false);
const enrollmentForms = ref<EcrfFormSummary[]>([]);
const formsLoading = ref(false);

const applyScopeKind = ref<string>("study");
const applyScopeRef = ref<string>("");
const applyKind = ref<KernelLockKind>("Frozen");
const applyReason = ref("");
const releaseReason = ref("");
const busy = ref(false);
const outcome = ref<{ kind: "ok" | "error"; message: string } | null>(null);

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

const signerUsername = computed(
  () => authStore?.user?.email || authStore?.user?.username || "data-manager"
);
const signerFullName = computed(
  () =>
    authStore?.user?.name || authStore?.user?.full_name || signerUsername.value
);

const siteOptions = computed(() =>
  studyChildren.value
    .filter(
      (child) =>
        Boolean(child.node.ref) &&
        !child.node.mask &&
        child.node.ref!.startsWith("study_site:")
    )
    .map((child) => ({
      ref: child.node.ref as string,
      label:
        child.node.code ||
        child.node.title ||
        getNodeDisplay(child.node.ref).label,
    }))
);

const enrollmentOptions = computed(() =>
  siteEnrollments.value
    .filter(
      (child) =>
        Boolean(child.node.ref) &&
        !child.node.mask &&
        child.node.ref!.startsWith("enrollment:")
    )
    .map((child) => ({
      ref: child.node.ref as string,
      label:
        child.node.code ||
        child.node.title ||
        getNodeDisplay(child.node.ref).label,
    }))
);

const formOptions = computed(() =>
  enrollmentForms.value.map((summary) => {
    const base = summary.collection_title || summary.collection_spec_id;
    const label = summary.planned_activity_id
      ? `${base} · ${summary.planned_activity_id}`
      : base;
    return {
      ref: `collection_instance:${summary.form_id}`,
      label,
    };
  })
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

async function loadSiteEnrollments(siteRef: string) {
  siteEnrollments.value = [];
  if (!siteRef || typeof kernelApi.getChildren !== "function") return;
  enrollmentsLoading.value = true;
  try {
    const children = await kernelApi.getChildren(siteRef);
    siteEnrollments.value = children ?? [];
    for (const child of siteEnrollments.value) {
      if (child.node.ref && !child.node.mask) {
        seedNodeSummary(child.node.ref, {
          kind: child.node.kind,
          code: child.node.code,
          title: child.node.title,
          status: child.node.status,
        });
      }
    }
    if (
      (applyScopeKind.value === "enrollment" ||
        applyScopeKind.value === "collection_instance") &&
      !selectedEnrollmentRef.value &&
      enrollmentOptions.value.length > 0
    ) {
      selectedEnrollmentRef.value = enrollmentOptions.value[0].ref;
      if (applyScopeKind.value === "enrollment") {
        applyScopeRef.value = selectedEnrollmentRef.value;
      } else {
        await loadEnrollmentForms(selectedEnrollmentRef.value);
      }
    }
  } catch {
    siteEnrollments.value = [];
  } finally {
    enrollmentsLoading.value = false;
  }
}

async function loadEnrollmentForms(enrollmentRef: string) {
  enrollmentForms.value = [];
  const parsed = parseNodeRef(enrollmentRef);
  const enrollmentId = parsed?.id ?? "";
  if (!enrollmentId || typeof ecrfFormsApi.listForEnrollment !== "function") {
    return;
  }
  formsLoading.value = true;
  try {
    const summaries = await ecrfFormsApi.listForEnrollment(enrollmentId);
    enrollmentForms.value = Array.isArray(summaries) ? summaries : [];
    for (const summary of enrollmentForms.value) {
      const base = summary.collection_title || summary.collection_spec_id;
      const title = summary.planned_activity_id
        ? `${base} · ${summary.planned_activity_id}`
        : base;
      seedNodeSummary(`collection_instance:${summary.form_id}`, {
        kind: "collection_instance",
        title,
      });
    }
    if (
      applyScopeKind.value === "collection_instance" &&
      !selectedFormRef.value &&
      formOptions.value.length > 0
    ) {
      selectedFormRef.value = formOptions.value[0].ref;
      applyScopeRef.value = selectedFormRef.value;
    }
  } catch {
    enrollmentForms.value = [];
  } finally {
    formsLoading.value = false;
  }
}

function onScopeKindChange() {
  if (applyScopeKind.value === "study" && studyRef.value) {
    applyScopeRef.value = studyRef.value;
    return;
  }
  if (applyScopeKind.value === "study_site") {
    if (!selectedSiteRef.value && siteOptions.value.length > 0) {
      selectedSiteRef.value = siteOptions.value[0].ref;
    }
    applyScopeRef.value = selectedSiteRef.value;
    return;
  }
  if (
    applyScopeKind.value === "enrollment" ||
    applyScopeKind.value === "collection_instance"
  ) {
    if (!selectedSiteRef.value && siteOptions.value.length > 0) {
      selectedSiteRef.value = siteOptions.value[0].ref;
    }
    applyScopeRef.value =
      applyScopeKind.value === "enrollment"
        ? selectedEnrollmentRef.value
        : selectedFormRef.value;
    if (selectedSiteRef.value && siteEnrollments.value.length === 0) {
      void loadSiteEnrollments(selectedSiteRef.value);
    } else if (
      applyScopeKind.value === "collection_instance" &&
      selectedEnrollmentRef.value &&
      enrollmentForms.value.length === 0
    ) {
      void loadEnrollmentForms(selectedEnrollmentRef.value);
    }
    return;
  }
  applyScopeRef.value = "";
}

function onSiteSelectChange() {
  selectedEnrollmentRef.value = "";
  selectedFormRef.value = "";
  enrollmentForms.value = [];
  if (applyScopeKind.value === "study_site") {
    applyScopeRef.value = selectedSiteRef.value;
  } else {
    applyScopeRef.value = "";
    void loadSiteEnrollments(selectedSiteRef.value);
  }
}

function onEnrollmentSelectChange() {
  selectedFormRef.value = "";
  if (applyScopeKind.value === "enrollment") {
    applyScopeRef.value = selectedEnrollmentRef.value;
  } else {
    applyScopeRef.value = "";
    void loadEnrollmentForms(selectedEnrollmentRef.value);
  }
}

function onFormSelectChange() {
  if (applyScopeKind.value === "collection_instance") {
    applyScopeRef.value = selectedFormRef.value;
  }
}

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

watch(
  () => props.studyCode,
  async (code) => {
    const seq = ++loadSeq;
    studyRef.value = null;
    studyError.value = null;
    locks.value = [];
    outcome.value = null;
    selectedSiteRef.value = "";
    selectedEnrollmentRef.value = "";
    selectedFormRef.value = "";
    siteEnrollments.value = [];
    enrollmentForms.value = [];
    if (!code) return;

    studyLoading.value = true;
    try {
      const resolved = await findKernelStudyRef(code);
      if (seq !== loadSeq) return;
      studyRef.value = resolved;
      if (!resolved) return;
      seedNodeSummary(resolved, { kind: "study", code });

      applyScopeKind.value = "study";
      applyScopeRef.value = resolved;

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
    } catch (err) {
      if (seq !== loadSeq) return;
      studyError.value = err instanceof Error ? err.message : String(err);
    } finally {
      if (seq === loadSeq) studyLoading.value = false;
    }
  },
  { immediate: true }
);

function openApplyModal() {
  if (!applyScopeRef.value.trim() || !applyReason.value.trim()) return;
  outcome.value = null;
  modalOp.value = {
    mode: "apply",
    idempotencyKey: globalThis.crypto.randomUUID(),
    lockId: globalThis.crypto.randomUUID(),
    attestationId: globalThis.crypto.randomUUID(),
  };
}

function openReleaseModal(lockRef: string, lockKind: string) {
  if (!releaseReason.value.trim()) return;
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
    await reloadLocks();
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
.field-label,
.release-reason-bar label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.scope-static-value {
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text-dark);
  background: var(--semantic-color-surface);
}

.direct-ref-disclosure {
  font-size: 0.82rem;
  color: var(--semantic-color-text-body);
}

.direct-ref-summary {
  cursor: pointer;
  font-weight: 500;
  color: var(--semantic-color-text-muted);
}

.direct-ref-field {
  margin-top: 6px;
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
